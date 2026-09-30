using AgentEnvManager.Core.Adoption;
using AgentEnvManager.Core.Inspection;
using AgentEnvManager.Core.Operations;

namespace AgentEnvManager.Core.Activation;

internal sealed class VersionSwitcher(
    IEnvironmentManifestStore manifestStore,
    IEnvironmentRecoveryPointStore recoveryPointStore,
    IOperationJournal operationJournal,
    IStableActivationPathFactory activationPathFactory,
    IEnvironmentActivationLink activationLink,
    IRuntimeHealthCheck healthCheck,
    TimeProvider timeProvider)
{
    public async Task<VersionSwitchPreview> PreviewAsync(
        EnvironmentFingerprint fingerprint,
        CancellationToken cancellationToken = default)
    {
        var target = await manifestStore.FindByFingerprintAsync(
            fingerprint,
            cancellationToken)
            ?? throw new KeyNotFoundException("未找到要切换的已纳管环境。");
        var currentTarget = await activationLink.GetTargetAsync(
            target.StableActivationPath,
            cancellationToken);
        var manifests = await manifestStore.ReadAllAsync(cancellationToken);
        var active = currentTarget is null
            ? null
            : manifests.FirstOrDefault(manifest =>
                PathsEqual(
                    activationLink.GetActivationTarget(manifest.Location),
                    currentTarget));
        var targetLocation = activationLink.GetActivationTarget(
            target.Location);
        var managedEntryPath = GetManagedEntryPath(target);

        return new VersionSwitchPreview(
            target.Fingerprint,
            target,
            active,
            target.StableActivationPath,
            managedEntryPath,
            active is null
                ? $"切换只更新 {target.StableActivationPath} 激活点，不重写 PATH。"
                : $"从 {active.Name} {active.Version} 切换到 {target.Name} {target.Version}，只更新 {target.StableActivationPath} 激活点。",
            currentTarget is not null
                && PathsEqual(currentTarget, targetLocation));
    }

    public async Task<OperationRecord> SwitchAsync(
        VersionSwitchPreview preview,
        CancellationToken cancellationToken = default)
    {
        if (preview.IsAlreadyActive)
        {
            throw new InvalidOperationException("该版本已经是当前激活版本。");
        }

        preview = await EnsurePreviewIsCurrentAsync(
            preview,
            cancellationToken);
        var operation = OperationStateMachine.Create(
            OperationType.Switch,
            $"切换 {preview.Target.Name} 到 {preview.Target.Version}",
            timeProvider);
        await operationJournal.SaveAsync(operation, cancellationToken);
        var executionStarted = false;
        try
        {
            await ValidateSwitchAsync(preview, cancellationToken);
            operation = await SaveTransitionAsync(
                OperationStateMachine.MarkValidated(operation, timeProvider),
                cancellationToken);

            var recoveryPoint = await recoveryPointStore.CreateAsync(
                ToAdoptionPreview(preview.Target),
                operation.Id,
                preview.Active,
                cancellationToken);
            operation = await SaveTransitionAsync(
                OperationStateMachine.MarkRecoveryReady(
                    operation,
                    recoveryPoint.Id,
                    timeProvider),
                cancellationToken);

            operation = await SaveTransitionAsync(
                OperationStateMachine.BeginExecution(operation, timeProvider),
                cancellationToken);
            executionStarted = true;
            await activationLink.SetTargetAsync(
                preview.ActivationPath,
                preview.Target.Location,
                cancellationToken);
            await EnsureManagedEntryAsync(preview, cancellationToken);

            operation = await SaveTransitionAsync(
                OperationStateMachine.BeginVerification(
                    operation,
                    timeProvider),
                cancellationToken);
            var health = await healthCheck.CheckAsync(
                preview.Target,
                preview.ManagedEntryPath,
                cancellationToken);
            if (!health.IsHealthy)
            {
                throw new InvalidOperationException(
                    $"健康检查失败: {health.Message}");
            }

            operation = OperationStateMachine.Complete(operation, timeProvider);
            await operationJournal.SaveAsync(operation, cancellationToken);
            return operation;
        }
        catch (Exception exception)
        {
            var failureReason = exception.Message;
            var rollbackSucceeded = true;
            if (executionStarted)
            {
                try
                {
                    await RestoreActivationAsync(
                        preview,
                        CancellationToken.None);
                    failureReason = $"{failureReason}；已恢复原版本。";
                }
                catch (Exception restoreException)
                {
                    rollbackSucceeded = false;
                    failureReason =
                        $"{failureReason}；恢复原版本失败: {restoreException.Message}";
                }
            }

            operation = OperationStateMachine.Fail(
                operation,
                failureReason,
                timeProvider);
            await operationJournal.SaveAsync(operation, CancellationToken.None);
            if (rollbackSucceeded)
            {
                operation = OperationStateMachine.Rollback(
                    operation,
                    timeProvider);
                await operationJournal.SaveAsync(
                    operation,
                    CancellationToken.None);
            }

            throw new InvalidOperationException(failureReason, exception);
        }
    }

    public async Task<OperationRecord> RollbackAsync(
        string operationId,
        CancellationToken cancellationToken = default)
    {
        var operation = await operationJournal.GetAsync(
            operationId,
            cancellationToken)
            ?? throw new KeyNotFoundException("未找到操作记录。");
        if (operation.State is OperationState.Succeeded
            or OperationState.RolledBack)
        {
            operation = OperationStateMachine.Reject(
                operation,
                OperationState.RolledBack,
                "该操作当前状态不能回滚。",
                timeProvider);
            await operationJournal.SaveAsync(operation, CancellationToken.None);
            throw new InvalidOperationException("该操作当前状态不能回滚。");
        }

        if (operation.RecoveryPointId is null)
        {
            throw new InvalidOperationException("操作没有可用的恢复点。");
        }

        var recoveryPoint = await recoveryPointStore.GetAsync(
            operation.RecoveryPointId,
            cancellationToken)
            ?? throw new InvalidOperationException("恢复点不存在，无法回滚。");
        var target = await manifestStore.FindByFingerprintAsync(
            recoveryPoint.Fingerprint,
            cancellationToken)
            ?? throw new InvalidOperationException("目标环境不存在，无法回滚。");
        var managedEntryPath = GetManagedEntryPath(target);

        if (recoveryPoint.PreviousManifest is null)
        {
            await activationLink.DeleteAsync(
                managedEntryPath,
                cancellationToken);
            await activationLink.DeleteAsync(
                target.StableActivationPath,
                cancellationToken);
        }
        else
        {
            await activationLink.SetTargetAsync(
                target.StableActivationPath,
                recoveryPoint.PreviousManifest.Location,
                cancellationToken);
            await EnsureManagedEntryAsync(
                managedEntryPath,
                target.StableActivationPath,
                cancellationToken);
        }

        var expectedTarget = recoveryPoint.PreviousManifest is null
            ? null
            : activationLink.GetActivationTarget(
                recoveryPoint.PreviousManifest.Location);
        var actualTarget = await activationLink.GetTargetAsync(
            target.StableActivationPath,
            cancellationToken);
        if (expectedTarget is null
            ? actualTarget is not null
            : actualTarget is null
                || !PathsEqual(expectedTarget, actualTarget))
        {
            throw new InvalidOperationException("恢复激活点后校验失败。");
        }

        if (operation.State != OperationState.Failed)
        {
            operation = OperationStateMachine.Fail(
                operation,
                "用户请求从恢复点回滚。",
                timeProvider);
            await operationJournal.SaveAsync(operation, cancellationToken);
        }

        operation = OperationStateMachine.Rollback(operation, timeProvider);
        await operationJournal.SaveAsync(operation, cancellationToken);
        return operation;
    }

    private async Task<OperationRecord> SaveTransitionAsync(
        OperationRecord operation,
        CancellationToken cancellationToken)
    {
        await operationJournal.SaveAsync(operation, cancellationToken);
        return operation;
    }

    private async Task ValidateSwitchAsync(
        VersionSwitchPreview preview,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(preview.Target.Location))
        {
            throw new InvalidOperationException("目标环境物理路径不能为空。");
        }

        if (string.IsNullOrWhiteSpace(preview.ActivationPath))
        {
            throw new InvalidOperationException("稳定激活路径不能为空。");
        }

        if (string.IsNullOrWhiteSpace(preview.ManagedEntryPath))
        {
            throw new InvalidOperationException("受管入口路径不能为空。");
        }

        if (preview.Target.IsSystemComponent)
        {
            throw new InvalidOperationException("系统组件不能切换。");
        }

        if (!await activationLink.TargetExistsAsync(
            preview.Target.Location,
            cancellationToken))
        {
            throw new InvalidOperationException("目标环境路径不存在。");
        }
    }

    private static AdoptionPreview ToAdoptionPreview(EnvironmentManifest manifest)
    {
        return new AdoptionPreview(
            manifest.Fingerprint,
            manifest.Identity,
            ToAsset(manifest),
            manifest.AssetHash,
            manifest.StableActivationPath,
            "切换版本前记录当前激活点。",
            IsAlreadyManaged: true,
            ExistingIdentity: manifest.Identity,
            ManagedEntryPath: manifest.ManagedEntryPath);
    }

    private static EnvironmentAsset ToAsset(EnvironmentManifest manifest)
    {
        return new EnvironmentAsset(
            manifest.Kind,
            manifest.Name,
            manifest.Version,
            manifest.Location,
            manifest.IsSystemComponent,
            manifest.Source);
    }

    private async Task RestoreActivationAsync(
        VersionSwitchPreview preview,
        CancellationToken cancellationToken)
    {
        if (preview.Active is null)
        {
            await activationLink.DeleteAsync(
                preview.ManagedEntryPath,
                cancellationToken);
            await activationLink.DeleteAsync(
                preview.ActivationPath,
                cancellationToken);
            return;
        }

        await activationLink.SetTargetAsync(
            preview.ActivationPath,
            preview.Active.Location,
            cancellationToken);
        await EnsureManagedEntryAsync(preview, cancellationToken);
    }

    private async Task EnsureManagedEntryAsync(
        VersionSwitchPreview preview,
        CancellationToken cancellationToken)
    {
        await EnsureManagedEntryAsync(
            preview.ManagedEntryPath,
            preview.ActivationPath,
            cancellationToken);
    }

    private async Task EnsureManagedEntryAsync(
        string managedEntryPath,
        string activationPath,
        CancellationToken cancellationToken)
    {
        var currentEntryTarget = await activationLink.GetTargetAsync(
            managedEntryPath,
            cancellationToken);
        if (currentEntryTarget is not null
            && PathsEqual(currentEntryTarget, activationPath))
        {
            return;
        }

        await activationLink.SetTargetAsync(
            managedEntryPath,
            activationPath,
            cancellationToken);
    }

    private async Task<VersionSwitchPreview> EnsurePreviewIsCurrentAsync(
        VersionSwitchPreview preview,
        CancellationToken cancellationToken)
    {
        var current = await manifestStore.FindByFingerprintAsync(
            preview.TargetFingerprint,
            cancellationToken);
        var canonicalTarget = current is null
            ? null
            : NormalizeManifest(current);
        var canonicalManagedEntryPath = current is null
            ? null
            : GetManagedEntryPath(current);
        if (current is null
            || canonicalTarget != NormalizeManifest(preview.Target)
            || !PathsEqual(
                preview.ActivationPath,
                current.StableActivationPath)
            || !PathsEqual(
                preview.ManagedEntryPath,
                canonicalManagedEntryPath!))
        {
            throw new InvalidOperationException("切换计划已过期，请重新预览。");
        }

        var manifests = await manifestStore.ReadAllAsync(cancellationToken);
        var currentTarget = await activationLink.GetTargetAsync(
            preview.ActivationPath,
            cancellationToken);
        var currentActive = currentTarget is null
            ? null
            : manifests.FirstOrDefault(manifest =>
                PathsEqual(
                    activationLink.GetActivationTarget(manifest.Location),
                    currentTarget));
        if (!SameManifest(currentActive, preview.Active))
        {
            throw new InvalidOperationException("切换计划已过期，请重新预览。");
        }

        return preview with
        {
            TargetFingerprint = current.Fingerprint,
            Target = canonicalTarget!,
            Active = currentActive,
            ActivationPath = current.StableActivationPath,
            ManagedEntryPath = canonicalManagedEntryPath!,
            IsAlreadyActive = currentTarget is not null
                && PathsEqual(
                    currentTarget,
                    activationLink.GetActivationTarget(current.Location))
        };
    }

    private static bool SameManifest(
        EnvironmentManifest? left,
        EnvironmentManifest? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        return left.Identity == right.Identity
            && left.Fingerprint == right.Fingerprint
            && PathsEqual(left.Location, right.Location);
    }

    private string GetManagedEntryPath(EnvironmentManifest manifest)
    {
        if (!string.IsNullOrWhiteSpace(manifest.ManagedEntryPath))
        {
            return manifest.ManagedEntryPath;
        }

        var activationKey = string.IsNullOrWhiteSpace(
            manifest.ActivationIdentity)
            ? RuntimeActivationKey.Create(ToAsset(manifest))
            : new ActivationKey(
                new EnvironmentIdentity(manifest.ActivationIdentity));
        return activationPathFactory.CreateManagedEntry(activationKey);
    }

    private EnvironmentManifest NormalizeManifest(EnvironmentManifest manifest)
    {
        var activationKey = string.IsNullOrWhiteSpace(
            manifest.ActivationIdentity)
            ? RuntimeActivationKey.Create(ToAsset(manifest))
            : new ActivationKey(
                new EnvironmentIdentity(manifest.ActivationIdentity));
        var managedEntryPath = GetManagedEntryPath(manifest);
        return manifest with
        {
            ActivationIdentity = activationKey.Value,
            ManagedEntryPath = managedEntryPath
        };
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(
            NormalizePath(left),
            NormalizePath(right),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string path)
    {
        const string localAppDataToken = "%LOCALAPPDATA%";
        var expandedPath = path.StartsWith(
            localAppDataToken,
            StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                path[localAppDataToken.Length..].TrimStart('\\', '/'))
            : path;

        return Path.GetFullPath(expandedPath).TrimEnd('\\', '/');
    }
}
