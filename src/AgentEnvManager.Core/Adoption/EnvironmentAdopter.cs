using AgentEnvManager.Core.Activation;
using AgentEnvManager.Core.Inspection;
using AgentEnvManager.Core.Operations;

namespace AgentEnvManager.Core.Adoption;

internal sealed class EnvironmentAdopter(
    EnvironmentInspector inspector,
    IEnvironmentManifestStore manifestStore,
    IEnvironmentIndex index,
    IEnvironmentAssetHasher assetHasher,
    IEnvironmentRecoveryPointStore recoveryPointStore,
    IOperationJournal operationJournal,
    IStableActivationPathFactory activationPathFactory,
    TimeProvider timeProvider)
{
    public async Task<AdoptionPreview> PreviewAsync(
        EnvironmentFingerprint fingerprint,
        CancellationToken cancellationToken = default)
    {
        var inventory = await inspector.InspectAsync(cancellationToken);
        var observed = inventory.Environments.SingleOrDefault(environment =>
            environment.Fingerprint == fingerprint)
            ?? throw new KeyNotFoundException("未找到要纳管的仅观测环境。");
        var existing = await manifestStore.FindByFingerprintAsync(
            fingerprint,
            cancellationToken);
        var assetHash = await assetHasher.ComputeHashAsync(
            observed.Asset,
            cancellationToken);
        var proposedIdentity = existing?.Identity
            ?? new EnvironmentIdentity(fingerprint.Value);
        var activationKey = string.IsNullOrWhiteSpace(existing?.ActivationIdentity)
            ? RuntimeActivationKey.Create(observed.Asset)
            : new ActivationKey(
                new EnvironmentIdentity(existing.ActivationIdentity));
        var managedEntryPath = string.IsNullOrWhiteSpace(
            existing?.ManagedEntryPath)
            ? activationPathFactory.CreateManagedEntry(activationKey)
            : existing.ManagedEntryPath;

        var preview = new AdoptionPreview(
            fingerprint,
            proposedIdentity,
            observed.Asset,
            assetHash,
            existing?.StableActivationPath
            ?? activationPathFactory.Create(activationKey),
            "纳管会创建可移植 manifest 并重建本地索引，不会移动原始文件。",
            existing is not null,
            existing?.Identity,
            managedEntryPath);
        if (existing is not null && existing.AssetHash == assetHash)
        {
            return preview;
        }

        var operation = OperationStateMachine.Create(
            OperationType.Adopt,
            $"纳管 {observed.Asset.Name}",
            timeProvider,
            target: observed.Asset.Location,
            impact: preview.Impact);
        await operationJournal.SaveAsync(operation, cancellationToken);
        ValidateAdoption(preview);
        operation = await SaveTransitionAsync(
            OperationStateMachine.MarkValidated(operation, timeProvider),
            cancellationToken);
        var recoveryPoint = await recoveryPointStore.CreateAsync(
            preview,
            operation.Id,
            existing,
            cancellationToken);
        operation = await SaveTransitionAsync(
            OperationStateMachine.MarkRecoveryReady(
                operation,
                recoveryPoint.Id,
                timeProvider),
            cancellationToken);

        return preview with
        {
            OperationId = operation.Id,
            RecoveryPointId = recoveryPoint.Id
        };
    }

    public async Task<ManagedEnvironment> AdoptAsync(
        EnvironmentFingerprint fingerprint,
        CancellationToken cancellationToken = default)
    {
        var preview = await PreviewAsync(fingerprint, cancellationToken);
        return await AdoptAsync(preview, cancellationToken);
    }

    public async Task<ManagedEnvironment> AdoptAsync(
        AdoptionPreview preview,
        CancellationToken cancellationToken = default)
    {
        var existing = await manifestStore.FindByFingerprintAsync(
            preview.Fingerprint,
            cancellationToken);
        if (existing is not null && existing.AssetHash == preview.AssetHash)
        {
            return new ManagedEnvironment(existing.Identity, existing);
        }

        var operation = string.IsNullOrWhiteSpace(preview.OperationId)
            ? OperationStateMachine.Create(
                OperationType.Adopt,
                $"纳管 {preview.Asset.Name}",
                timeProvider,
                target: preview.Asset.Location,
                impact: preview.Impact)
            : await operationJournal.GetAsync(
                preview.OperationId,
                cancellationToken)
                ?? throw new InvalidOperationException("纳管计划不存在。");
        if (string.IsNullOrWhiteSpace(preview.OperationId))
        {
            await operationJournal.SaveAsync(operation, cancellationToken);
        }

        EnvironmentManifest? savedManifest = null;
        try
        {
            var prepared = await PrepareAdoptionPlanAsync(
                preview,
                existing,
                operation,
                cancellationToken);
            operation = prepared.Operation;
            var recoveryPoint = prepared.RecoveryPoint;

            operation = await SaveTransitionAsync(
                OperationStateMachine.BeginExecution(operation, timeProvider),
                cancellationToken);

            var activationKey = string.IsNullOrWhiteSpace(
                existing?.ActivationIdentity)
                ? RuntimeActivationKey.Create(preview.Asset)
                : new ActivationKey(
                    new EnvironmentIdentity(existing.ActivationIdentity));
            var managedEntryPath = !string.IsNullOrWhiteSpace(
                preview.ManagedEntryPath)
                ? preview.ManagedEntryPath
                : string.IsNullOrWhiteSpace(existing?.ManagedEntryPath)
                    ? activationPathFactory.CreateManagedEntry(activationKey)
                    : existing.ManagedEntryPath;
            savedManifest = await manifestStore.SaveAsync(
                new EnvironmentManifest(
                    preview.ProposedIdentity,
                    preview.Fingerprint,
                    preview.Asset.Kind,
                    preview.Asset.Name,
                    preview.Asset.Version,
                    preview.Asset.Source,
                    preview.Asset.Location,
                    preview.StableActivationPath,
                    preview.AssetHash,
                    recoveryPoint.Id,
                    operation.Id,
                    preview.Asset.IsSystemComponent,
                    timeProvider.GetUtcNow(),
                    activationKey.Value,
                    managedEntryPath),
                cancellationToken);

            await RebuildIndexAsync(cancellationToken);
            operation = await SaveTransitionAsync(
                OperationStateMachine.BeginVerification(
                    operation,
                    timeProvider),
                cancellationToken);

            var verified = await manifestStore.FindByFingerprintAsync(
                preview.Fingerprint,
                cancellationToken);
            if (verified is null
                || verified.AssetHash != preview.AssetHash)
            {
                throw new InvalidOperationException("纳管验证失败。");
            }

            operation = await SaveTransitionAsync(
                OperationStateMachine.Complete(operation, timeProvider),
                cancellationToken);
            return new ManagedEnvironment(verified.Identity, verified);
        }
        catch (Exception exception)
        {
            operation = OperationStateMachine.Fail(
                operation,
                exception.Message,
                timeProvider);
            await operationJournal.SaveAsync(
                operation,
                CancellationToken.None);
            await RestoreManifestAsync(
                existing,
                savedManifest,
                CancellationToken.None);
            operation = OperationStateMachine.Rollback(
                operation,
                timeProvider);
            await operationJournal.SaveAsync(
                operation,
                CancellationToken.None);
            throw;
        }
    }

    public async Task<int> RebuildIndexAsync(
        CancellationToken cancellationToken = default)
    {
        var manifests = await manifestStore.ReadAllAsync(cancellationToken);
        await index.RebuildAsync(manifests, cancellationToken);
        return manifests.Count;
    }

    public async Task<OperationRecord> RollbackOperationAsync(
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

        if (operation.RecoveryPointId is not null)
        {
            var recoveryPoint = await recoveryPointStore.GetAsync(
                operation.RecoveryPointId,
                cancellationToken);
            if (recoveryPoint is null)
            {
                throw new InvalidOperationException("恢复点不存在，无法回滚。");
            }

            var current = await manifestStore.FindByFingerprintAsync(
                recoveryPoint.Fingerprint,
                cancellationToken);
            if (current is not null
                && current.OperationId != recoveryPoint.OperationId)
            {
                throw new InvalidOperationException(
                    "目标 manifest 已被后续操作更新，拒绝覆盖。");
            }

            if (current is not null)
            {
                await manifestStore.DeleteAsync(
                    current.Identity,
                    cancellationToken);
            }

            if (recoveryPoint.PreviousManifest is not null)
            {
                await manifestStore.SaveAsync(
                    recoveryPoint.PreviousManifest,
                    cancellationToken);
            }

            await RebuildIndexAsync(cancellationToken);
        }

        if (operation.State != OperationState.Failed)
        {
            operation = OperationStateMachine.Fail(
                operation,
                "用户请求从恢复点回滚。",
                timeProvider);
            await operationJournal.SaveAsync(operation, cancellationToken);
        }

        operation = OperationStateMachine.Rollback(
            operation,
            timeProvider);
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

    private async Task<(
        OperationRecord Operation,
        AdoptionRecoveryPoint RecoveryPoint)> PrepareAdoptionPlanAsync(
            AdoptionPreview preview,
            EnvironmentManifest? existing,
            OperationRecord operation,
            CancellationToken cancellationToken)
    {
        if (operation.State == OperationState.RecoveryReady
            && !string.IsNullOrWhiteSpace(preview.RecoveryPointId))
        {
            var existingPoint = await recoveryPointStore.GetAsync(
                preview.RecoveryPointId,
                cancellationToken)
                ?? throw new InvalidOperationException("纳管恢复点不存在。");
            return (operation, existingPoint);
        }

        if (operation.State != OperationState.Draft)
        {
            throw new InvalidOperationException("纳管计划状态无效。");
        }

        ValidateAdoption(preview);
        operation = await SaveTransitionAsync(
            OperationStateMachine.MarkValidated(operation, timeProvider),
            cancellationToken);
        var recoveryPoint = await recoveryPointStore.CreateAsync(
            preview,
            operation.Id,
            existing,
            cancellationToken);
        operation = await SaveTransitionAsync(
            OperationStateMachine.MarkRecoveryReady(
                operation,
                recoveryPoint.Id,
                timeProvider),
            cancellationToken);
        return (operation, recoveryPoint);
    }

    private async Task RestoreManifestAsync(
        EnvironmentManifest? existing,
        EnvironmentManifest? savedManifest,
        CancellationToken cancellationToken)
    {
        if (existing is null)
        {
            if (savedManifest is not null)
            {
                await manifestStore.DeleteAsync(
                    savedManifest.Identity,
                    cancellationToken);
            }
        }
        else
        {
            await manifestStore.SaveAsync(existing, cancellationToken);
        }

        await RebuildIndexAsync(cancellationToken);
    }

    private static void ValidateAdoption(AdoptionPreview preview)
    {
        if (string.IsNullOrWhiteSpace(preview.Fingerprint.Value))
        {
            throw new InvalidOperationException("环境指纹不能为空。");
        }

        if (string.IsNullOrWhiteSpace(preview.ProposedIdentity.Value))
        {
            throw new InvalidOperationException("环境身份不能为空。");
        }

        if (string.IsNullOrWhiteSpace(preview.Asset.Location))
        {
            throw new InvalidOperationException("环境物理路径不能为空。");
        }

        if (string.IsNullOrWhiteSpace(preview.AssetHash))
        {
            throw new InvalidOperationException("资产哈希不能为空。");
        }

        if (string.IsNullOrWhiteSpace(preview.StableActivationPath))
        {
            throw new InvalidOperationException("稳定激活路径不能为空。");
        }

        if (string.IsNullOrWhiteSpace(preview.ManagedEntryPath))
        {
            throw new InvalidOperationException("受管入口路径不能为空。");
        }
    }
}
