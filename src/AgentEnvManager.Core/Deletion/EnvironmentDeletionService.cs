using AgentEnvManager.Core.Activation;
using AgentEnvManager.Core.Adoption;
using AgentEnvManager.Core.Inspection;
using AgentEnvManager.Core.Migrations;
using AgentEnvManager.Core.Operations;

namespace AgentEnvManager.Core.Deletion;

internal sealed class EnvironmentDeletionService(
    IEnvironmentManifestStore manifestStore,
    IEnvironmentRecoveryPointStore recoveryPointStore,
    IOperationJournal operationJournal,
    IEnvironmentActivationLink activationLink,
    IEnvironmentAssetHasher assetHasher,
    IEnvironmentPathMover pathMover,
    IEnvironmentQuarantineStore quarantineStore,
    IRuntimeStateCatalog runtimeStateCatalog,
    TimeProvider timeProvider)
{
    public async Task<EnvironmentDeletionPreview> PreviewAsync(
        EnvironmentFingerprint fingerprint,
        IReadOnlyList<string>? associatedState,
        CancellationToken cancellationToken = default)
    {
        var target = await manifestStore.FindByFingerprintAsync(
            fingerprint,
            cancellationToken)
            ?? throw new KeyNotFoundException("未找到要删除的已纳管环境。");
        if (target.IsSystemComponent)
        {
            throw new InvalidOperationException("系统组件不能删除。");
        }

        var originalPath = activationLink.GetActivationTarget(
            target.Location);
        var quarantinePath = quarantineStore.CreatePath(
            target.Identity.Value);
        var detectedState = await runtimeStateCatalog.DescribeAsync(
            target,
            cancellationToken);
        var impactItems = MergeAssociatedState(
            detectedState,
            associatedState);
        var previousActivationTarget = await activationLink.GetTargetAsync(
            target.StableActivationPath,
            cancellationToken)
            ?? originalPath;
        var impact =
            $"将 {originalPath} 移入隔离区 {quarantinePath}；关联内容: {string.Join("、", impactItems)}。";
        var operation = OperationStateMachine.Create(
            OperationType.Delete,
            $"隔离删除 {target.Name} {target.Version}",
            timeProvider,
            target: quarantinePath,
            impact: impact,
            previousTarget: previousActivationTarget,
            expectedResult: "原路径释放，隔离记录和恢复入口可用。",
            targetIdentity: target.Fingerprint.Value,
            sourceTarget: originalPath,
            stableActivationPath: target.StableActivationPath);
        await operationJournal.SaveAsync(operation, cancellationToken);
        operation = await SaveTransitionAsync(
            OperationStateMachine.MarkValidated(
                operation,
                timeProvider),
            cancellationToken);
        var recoveryPoint = await recoveryPointStore.CreateAsync(
            CreateAdoptionPreview(target),
            operation.Id,
            target,
            cancellationToken);
        operation = await SaveTransitionAsync(
            OperationStateMachine.MarkRecoveryReady(
                operation,
                recoveryPoint.Id,
                timeProvider),
            cancellationToken);

        return new EnvironmentDeletionPreview(
            target.Fingerprint,
            target,
            originalPath,
            quarantinePath,
            previousActivationTarget,
            impact,
            impactItems,
            operation.Id,
            recoveryPoint.Id);
    }

    public async Task<OperationRecord> QuarantineAsync(
        EnvironmentDeletionPreview preview,
        CancellationToken cancellationToken = default)
    {
        var operation = await LoadDeleteOperationAsync(
            preview.OperationId,
            cancellationToken);
        preview = await EnsurePreviewCurrentAsync(
            preview,
            operation,
            cancellationToken);
        var recoveryPoint = await recoveryPointStore.GetAsync(
            operation.RecoveryPointId!,
            cancellationToken)
            ?? throw new InvalidOperationException("删除恢复点不存在。");
        var strategy = pathMover.GetStrategy(
            preview.OriginalPath,
            preview.QuarantinePath);
        var statistics = await pathMover.InspectAsync(
            preview.OriginalPath,
            preview.QuarantinePath,
            cancellationToken);
        if (strategy == MigrationStrategy.CopyAndVerify
            && statistics.TotalBytes > statistics.AvailableBytes)
        {
            throw new InvalidOperationException("隔离区空间不足。");
        }

        var moved = false;
        var copied = false;
        var quarantineSaved = false;
        try
        {
            operation = await SaveTransitionAsync(
                OperationStateMachine.BeginExecution(
                    operation,
                    timeProvider),
                cancellationToken);
            if (strategy == MigrationStrategy.AtomicRename)
            {
                await pathMover.MoveAsync(
                    preview.OriginalPath,
                    preview.QuarantinePath,
                    cancellationToken);
                moved = true;
            }
            else
            {
                copied = true;
                await pathMover.CopyAsync(
                    preview.OriginalPath,
                    preview.QuarantinePath,
                    cancellationToken);
                await VerifyCopyAsync(
                    preview.Target,
                    preview.OriginalPath,
                    preview.QuarantinePath,
                    cancellationToken);
                await pathMover.DeleteAsync(
                    preview.OriginalPath,
                    cancellationToken);
                copied = false;
                moved = true;
            }

            await activationLink.DeleteAsync(
                preview.Target.StableActivationPath,
                cancellationToken);
            await quarantineStore.SaveAsync(
                new QuarantinedEnvironment(
                    operation.Id,
                    preview.Target,
                    preview.QuarantinePath,
                    timeProvider.GetUtcNow(),
                    preview.AssociatedState,
                    preview.PreviousActivationTarget),
                cancellationToken);
            quarantineSaved = true;
            await manifestStore.DeleteAsync(
                preview.Target.Identity,
                cancellationToken);
            operation = await SaveTransitionAsync(
                OperationStateMachine.BeginVerification(
                    operation,
                    timeProvider),
                cancellationToken);
            operation = OperationStateMachine.Complete(
                operation,
                timeProvider);
            await operationJournal.SaveAsync(
                operation,
                cancellationToken);
            return operation;
        }
        catch (Exception exception)
        {
            var rollbackSucceeded = true;
            try
            {
                if (moved)
                {
                    await RestorePathAsync(
                        preview.QuarantinePath,
                        preview.OriginalPath,
                        preview.Target,
                        CancellationToken.None);
                }
                else if (copied)
                {
                    await pathMover.DeleteAsync(
                        preview.QuarantinePath,
                        CancellationToken.None);
                }

                await activationLink.SetTargetAsync(
                    preview.Target.StableActivationPath,
                    preview.PreviousActivationTarget,
                    CancellationToken.None);
                await manifestStore.SaveAsync(
                    preview.Target,
                    CancellationToken.None);
                if (quarantineSaved)
                {
                    await quarantineStore.DeleteAsync(
                        operation.Id,
                        CancellationToken.None);
                }
            }
            catch
            {
                rollbackSucceeded = false;
            }

            operation = OperationStateMachine.Fail(
                operation,
                rollbackSucceeded
                    ? $"{exception.Message}；已恢复原环境。"
                    : $"{exception.Message}；恢复原环境失败。",
                timeProvider);
            await operationJournal.SaveAsync(
                operation,
                CancellationToken.None);
            if (rollbackSucceeded)
            {
                operation = OperationStateMachine.Rollback(
                    operation,
                    timeProvider);
                await operationJournal.SaveAsync(
                    operation,
                    CancellationToken.None);
            }

            throw new InvalidOperationException(
                operation.FailureReason!,
                exception);
        }
    }

    public async Task<EnvironmentRestorePreview> PreviewRestoreAsync(
        string quarantineId,
        CancellationToken cancellationToken = default)
    {
        var entry = await quarantineStore.GetAsync(
            quarantineId,
            cancellationToken)
            ?? throw new KeyNotFoundException("未找到隔离记录。");
        if (Directory.Exists(entry.OriginalManifest.Location)
            || File.Exists(entry.OriginalManifest.Location))
        {
            throw new InvalidOperationException("原环境路径已被占用。");
        }

        var impact =
            $"从隔离区 {entry.QuarantinePath} 恢复到 {entry.OriginalManifest.Location}，恢复原激活目标 {entry.PreviousActivationTarget}。";
        var operation = OperationStateMachine.Create(
            OperationType.Delete,
            $"恢复隔离环境 {entry.OriginalManifest.Name}",
            timeProvider,
            target: entry.OriginalManifest.Location,
            impact: impact,
            previousTarget: entry.PreviousActivationTarget,
            expectedResult: "环境路径、清单和激活关系恢复完成。",
            targetIdentity: entry.OriginalManifest.Fingerprint.Value,
            sourceTarget: entry.QuarantinePath,
            stableActivationPath: entry.OriginalManifest.StableActivationPath);
        await operationJournal.SaveAsync(operation, cancellationToken);
        operation = OperationStateMachine.MarkValidated(
            operation,
            timeProvider);
        await operationJournal.SaveAsync(operation, cancellationToken);
        var recoveryPoint = await recoveryPointStore.CreateAsync(
            CreateAdoptionPreview(entry.OriginalManifest),
            operation.Id,
            entry.OriginalManifest,
            cancellationToken);
        operation = OperationStateMachine.MarkRecoveryReady(
            operation,
            recoveryPoint.Id,
            timeProvider);
        await operationJournal.SaveAsync(operation, cancellationToken);
        return new EnvironmentRestorePreview(
            quarantineId,
            entry.OriginalManifest,
            entry.OriginalManifest.Location,
            entry.QuarantinePath,
            impact,
            entry.AssociatedState,
            operation.Id,
            recoveryPoint.Id);
    }

    public async Task<OperationRecord> RestoreAsync(
        EnvironmentRestorePreview preview,
        CancellationToken cancellationToken = default)
    {
        var operation = await operationJournal.GetAsync(
            preview.OperationId ?? "",
            cancellationToken)
            ?? throw new InvalidOperationException("恢复计划不存在。");
        if (operation.Type != OperationType.Delete
            || operation.State != OperationState.RecoveryReady
            || !PathsEqual(operation.Target ?? "", preview.OriginalPath)
            || !PathsEqual(operation.SourceTarget ?? "", preview.QuarantinePath))
        {
            throw new InvalidOperationException("恢复计划已过期。");
        }

        var entry = await quarantineStore.GetAsync(
            preview.QuarantineId,
            cancellationToken)
            ?? throw new KeyNotFoundException("未找到隔离记录。");
        var moved = false;
        try
        {
            operation = await SaveTransitionAsync(
                OperationStateMachine.BeginExecution(
                    operation,
                    timeProvider),
                cancellationToken);
            var strategy = pathMover.GetStrategy(
                entry.QuarantinePath,
                entry.OriginalManifest.Location);
            if (strategy == MigrationStrategy.AtomicRename)
            {
                await pathMover.MoveAsync(
                    entry.QuarantinePath,
                    entry.OriginalManifest.Location,
                    cancellationToken);
                moved = true;
            }
            else
            {
                await pathMover.CopyAsync(
                    entry.QuarantinePath,
                    entry.OriginalManifest.Location,
                    cancellationToken);
                await VerifyCopyAsync(
                    entry.OriginalManifest,
                    entry.QuarantinePath,
                    entry.OriginalManifest.Location,
                    cancellationToken);
            }

            await manifestStore.SaveAsync(
                entry.OriginalManifest,
                cancellationToken);
            await activationLink.SetTargetAsync(
                entry.OriginalManifest.StableActivationPath,
                entry.PreviousActivationTarget,
                cancellationToken);
            await quarantineStore.DeleteAsync(
                entry.Id,
                cancellationToken);
            operation = await SaveTransitionAsync(
                OperationStateMachine.BeginVerification(
                    operation,
                    timeProvider),
                cancellationToken);
            operation = OperationStateMachine.Complete(
                operation,
                timeProvider);
            await operationJournal.SaveAsync(operation, cancellationToken);
            return operation;
        }
        catch
        {
            if (moved)
            {
                await pathMover.MoveBackAsync(
                    entry.OriginalManifest.Location,
                    entry.QuarantinePath,
                    CancellationToken.None);
            }

            operation = OperationStateMachine.Fail(
                operation,
                "恢复隔离环境失败，已保留隔离记录。",
                timeProvider);
            await operationJournal.SaveAsync(
                operation,
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

    public async Task<OperationRecord> RestoreAsync(
        string quarantineId,
        CancellationToken cancellationToken = default)
    {
        var preview = await PreviewRestoreAsync(
            quarantineId,
            cancellationToken);
        return await RestoreAsync(preview, cancellationToken);
    }

    public Task<IReadOnlyList<QuarantinedEnvironment>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        return quarantineStore.ReadAllAsync(cancellationToken);
    }

    public async Task<OperationRecord> RollbackDeleteAsync(
        string operationId,
        CancellationToken cancellationToken = default)
    {
        var operation = await operationJournal.GetAsync(
            operationId,
            cancellationToken)
            ?? throw new KeyNotFoundException("未找到操作记录。");
        if (operation.Type == OperationType.Purge)
        {
            var quarantined = await quarantineStore.GetAsync(
                operation.TargetIdentity ?? "",
                cancellationToken);
            if (quarantined is null
                || !PathsEqual(quarantined.QuarantinePath, operation.Target ?? ""))
            {
                throw new InvalidOperationException(
                    "永久清除已完成，不能回滚。");
            }
        }
        else
        {
            var quarantined = await quarantineStore.GetAsync(
                operation.Id,
                cancellationToken);
            if (quarantined is not null)
            {
                var preview = await PreviewRestoreAsync(
                    operation.Id,
                    cancellationToken);
                await RestoreAsync(preview, cancellationToken);
            }
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

    public async Task<PermanentDeletePreview> PreviewPermanentDeleteAsync(
        string quarantineId,
        CancellationToken cancellationToken = default)
    {
        var entry = await quarantineStore.GetAsync(
            quarantineId,
            cancellationToken)
            ?? throw new KeyNotFoundException("未找到隔离记录。");
        var impact =
            $"永久清除 {entry.QuarantinePath}；关联内容: {string.Join("、", entry.AssociatedState)}。此操作不可恢复。";
        var operation = OperationStateMachine.Create(
            OperationType.Purge,
            $"永久清除 {entry.OriginalManifest.Name}",
            timeProvider,
            target: entry.QuarantinePath,
            impact: impact,
            expectedResult: "隔离资产已永久删除。",
            targetIdentity: entry.Id,
            sourceTarget: entry.OriginalManifest.Fingerprint.Value);
        await operationJournal.SaveAsync(operation, cancellationToken);
        operation = await SaveTransitionAsync(
            OperationStateMachine.MarkValidated(
                operation,
                timeProvider),
            cancellationToken);
        var recoveryPoint = await recoveryPointStore.CreateAsync(
            CreateAdoptionPreview(entry.OriginalManifest),
            operation.Id,
            entry.OriginalManifest,
            cancellationToken);
        operation = await SaveTransitionAsync(
            OperationStateMachine.MarkRecoveryReady(
                operation,
                recoveryPoint.Id,
                timeProvider),
            cancellationToken);
        return new PermanentDeletePreview(
            entry.Id,
            entry.OriginalManifest.Name,
            entry.QuarantinePath,
            impact,
            entry.AssociatedState,
            operation.Id);
    }

    public async Task PermanentDeleteAsync(
        PermanentDeletePreview preview,
        bool confirmed,
        CancellationToken cancellationToken = default)
    {
        if (!confirmed)
        {
            throw new InvalidOperationException("永久清除确认无效，请重新预览。");
        }

        var operation = await operationJournal.GetAsync(
            preview.OperationId,
            cancellationToken);
        if (operation is null
            || operation.Type != OperationType.Purge
            || operation.State != OperationState.RecoveryReady
            || string.IsNullOrWhiteSpace(operation.Target)
            || !PathsEqual(operation.Target, preview.QuarantinePath))
        {
            throw new InvalidOperationException("永久清除预览已过期。");
        }

        var entry = await quarantineStore.GetAsync(
            preview.QuarantineId,
            cancellationToken)
            ?? throw new KeyNotFoundException("未找到隔离记录。");
        if (!PathsEqual(entry.QuarantinePath, preview.QuarantinePath))
        {
            throw new InvalidOperationException("永久清除预览已过期。");
        }

        if (!string.Equals(
                operation.Impact,
                preview.Impact,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException("永久清除预览已过期。");
        }

        operation = await SaveTransitionAsync(
            OperationStateMachine.BeginExecution(
                operation,
                timeProvider),
            cancellationToken);
        await pathMover.DeleteAsync(
            entry.QuarantinePath,
            cancellationToken);
        await quarantineStore.DeleteAsync(
            entry.Id,
            cancellationToken);
        operation = OperationStateMachine.BeginVerification(
            operation,
            timeProvider);
        operation = OperationStateMachine.Complete(
            operation,
            timeProvider);
        await operationJournal.SaveAsync(operation, cancellationToken);
    }

    private async Task<EnvironmentDeletionPreview> EnsurePreviewCurrentAsync(
        EnvironmentDeletionPreview preview,
        OperationRecord operation,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(
                preview.OperationId,
                operation.Id,
                StringComparison.Ordinal)
            || !string.Equals(
                preview.RecoveryPointId,
                operation.RecoveryPointId,
                StringComparison.Ordinal)
            || !string.Equals(
                preview.Fingerprint.Value,
                operation.TargetIdentity,
                StringComparison.Ordinal)
            || !PathsEqual(preview.OriginalPath, operation.SourceTarget ?? "")
            || !PathsEqual(
                preview.QuarantinePath,
                operation.Target ?? "")
            || operation.Type != OperationType.Delete
            || operation.State != OperationState.RecoveryReady)
        {
            throw new InvalidOperationException("删除计划已过期，请重新预览。");
        }

        var current = await manifestStore.FindByFingerprintAsync(
            preview.Fingerprint,
            cancellationToken);
        if (current != preview.Target)
        {
            throw new InvalidOperationException("删除计划已过期，请重新预览。");
        }

        return preview;
    }

    private async Task<OperationRecord> LoadDeleteOperationAsync(
        string? operationId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(operationId))
        {
            throw new InvalidOperationException("删除计划尚未创建恢复点。");
        }

        return await operationJournal.GetAsync(
            operationId,
            cancellationToken)
            ?? throw new InvalidOperationException("删除计划不存在。");
    }

    private async Task VerifyCopyAsync(
        EnvironmentManifest manifest,
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        var sourceHash = await assetHasher.ComputeHashAsync(
            ToAsset(manifest, sourcePath),
            cancellationToken);
        var destinationHash = await assetHasher.ComputeHashAsync(
            ToAsset(manifest, destinationPath),
            cancellationToken);
        if (!string.Equals(
                sourceHash,
                manifest.AssetHash,
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(
                sourceHash,
                destinationHash,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("隔离复制后的哈希校验失败。");
        }
    }

    private async Task RestorePathAsync(
        string currentPath,
        string originalPath,
        EnvironmentManifest manifest,
        CancellationToken cancellationToken)
    {
        var strategy = pathMover.GetStrategy(currentPath, originalPath);
        if (strategy == MigrationStrategy.AtomicRename)
        {
            await pathMover.MoveAsync(
                currentPath,
                originalPath,
                cancellationToken);
            return;
        }

        await pathMover.CopyAsync(
            currentPath,
            originalPath,
            cancellationToken);
        await VerifyCopyAsync(
            manifest,
            currentPath,
            originalPath,
            cancellationToken);
        await pathMover.DeleteAsync(
            currentPath,
            cancellationToken);
    }

    private async Task<OperationRecord> SaveTransitionAsync(
        OperationRecord operation,
        CancellationToken cancellationToken)
    {
        await operationJournal.SaveAsync(operation, cancellationToken);
        return operation;
    }

    private static IReadOnlyList<string> MergeAssociatedState(
        IReadOnlyList<string> detectedState,
        IReadOnlyList<string>? associatedState)
    {
        return detectedState
            .Concat(associatedState ?? [])
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .DefaultIfEmpty("未检测到额外运行时状态")
            .ToArray();
    }

    private static AdoptionPreview CreateAdoptionPreview(
        EnvironmentManifest manifest)
    {
        return new AdoptionPreview(
            manifest.Fingerprint,
            manifest.Identity,
            ToAsset(manifest, manifest.Location),
            manifest.AssetHash,
            manifest.StableActivationPath,
            "隔离删除前记录原环境。",
            IsAlreadyManaged: true,
            ExistingIdentity: manifest.Identity,
            ManagedEntryPath: manifest.ManagedEntryPath);
    }

    private static EnvironmentAsset ToAsset(
        EnvironmentManifest manifest,
        string location)
    {
        return new EnvironmentAsset(
            manifest.Kind,
            manifest.Name,
            manifest.Version,
            location,
            manifest.IsSystemComponent,
            manifest.Source);
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            StringComparison.OrdinalIgnoreCase);
    }
}
