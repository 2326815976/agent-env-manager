using AgentEnvManager.Core.Activation;
using AgentEnvManager.Core.Adoption;
using AgentEnvManager.Core.Inspection;
using AgentEnvManager.Core.Operations;

namespace AgentEnvManager.Core.Migrations;

internal sealed class EnvironmentMigrator(
    IEnvironmentManifestStore manifestStore,
    IEnvironmentRecoveryPointStore recoveryPointStore,
    IOperationJournal operationJournal,
    IEnvironmentAssetHasher assetHasher,
    IStableActivationPathFactory activationPathFactory,
    IEnvironmentActivationLink activationLink,
    IRuntimeHealthCheck healthCheck,
    IMigrationOccupancyProbe occupancyProbe,
    IEnvironmentPathMover pathMover,
    TimeProvider timeProvider)
{
    public async Task<MigrationPreview> PreviewAsync(
        EnvironmentFingerprint fingerprint,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        var target = await manifestStore.FindByFingerprintAsync(
            fingerprint,
            cancellationToken)
            ?? throw new KeyNotFoundException("未找到要迁移的已纳管环境。");
        var sourcePath = GetSourcePath(target);
        var destination = Path.GetFullPath(destinationPath);
        await ValidateMigrationAsync(
            target,
            sourcePath,
            destination,
            cancellationToken);
        var previousActivationTarget = await activationLink.GetTargetAsync(
            target.StableActivationPath,
            cancellationToken)
            ?? throw new InvalidOperationException(
                "稳定激活路径尚未绑定，无法安全迁移。");
        var wasActive = PathsEqual(
            previousActivationTarget,
            sourcePath);
        var strategy = pathMover.GetStrategy(
            sourcePath,
            destination);
        var statistics = await pathMover.InspectAsync(
            sourcePath,
            destination,
            cancellationToken);
        ValidateMigrationResources(
            strategy,
            statistics);

        var operationText = strategy == MigrationStrategy.AtomicRename
            ? "原子重命名"
            : "复制、哈希校验并保留原目录";
        var impact = wasActive
            ? $"将 {sourcePath} {operationText}到 {destination}，稳定激活路径保持 {target.StableActivationPath}。"
            : $"将非激活版本 {sourcePath} {operationText}到 {destination}；健康检查期间临时激活，完成后恢复原激活目标 {previousActivationTarget}。";
        var expectedResult = wasActive
            ? $"{sourcePath} 迁移到 {destination}，稳定激活路径可用且健康检查通过。"
            : $"{sourcePath} 迁移到 {destination}，健康检查通过并恢复原激活目标 {previousActivationTarget}。";
        var operation = OperationStateMachine.Create(
            OperationType.Migrate,
            $"迁移 {target.Name} {target.Version}",
            timeProvider,
            target: destination,
            impact: impact,
            previousTarget: previousActivationTarget,
            expectedResult: expectedResult,
            targetIdentity: target.Fingerprint.Value,
            sourceTarget: sourcePath,
            stableActivationPath: target.StableActivationPath,
            migrationStrategy: strategy.ToString());
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

        return new MigrationPreview(
            target.Fingerprint,
            target,
            sourcePath,
            destination,
            target.StableActivationPath,
            GetManagedEntryPath(target),
            previousActivationTarget,
            wasActive,
            strategy,
            statistics,
            impact,
            expectedResult,
            operation.Id,
            recoveryPoint.Id);
    }

    public async Task<OperationRecord> MigrateAsync(
        MigrationPreview preview,
        CancellationToken cancellationToken = default)
    {
        var operation = string.IsNullOrWhiteSpace(preview.OperationId)
            ? throw new InvalidOperationException("迁移计划尚未创建恢复点。")
            : await operationJournal.GetAsync(
                preview.OperationId,
                cancellationToken)
                ?? throw new InvalidOperationException("迁移计划不存在。");
        preview = await EnsurePreviewIsCurrentAsync(
            preview,
            operation,
            cancellationToken);
        if (operation.Type != OperationType.Migrate
            || operation.State != OperationState.RecoveryReady
            || string.IsNullOrWhiteSpace(operation.RecoveryPointId))
        {
            throw new InvalidOperationException("迁移计划状态无效。");
        }

        await ValidateMigrationAsync(
            preview.Target,
            preview.SourcePath,
            preview.DestinationPath,
            cancellationToken);
        var recoveryPoint = await recoveryPointStore.GetAsync(
            operation.RecoveryPointId,
            cancellationToken)
            ?? throw new InvalidOperationException("迁移恢复点不存在。");
        if (recoveryPoint.PreviousManifest is null)
        {
            throw new InvalidOperationException("迁移恢复点缺少原环境记录。");
        }

        var statistics = await pathMover.InspectAsync(
            preview.SourcePath,
            preview.DestinationPath,
            cancellationToken);
        ValidateMigrationResources(
            preview.Strategy,
            statistics);
        var moved = false;
        var copied = false;
        try
        {
            operation = await SaveTransitionAsync(
                OperationStateMachine.BeginExecution(
                    operation,
                    timeProvider),
                cancellationToken);
            if (preview.Strategy == MigrationStrategy.AtomicRename)
            {
                await pathMover.MoveAsync(
                    preview.SourcePath,
                    preview.DestinationPath,
                    cancellationToken);
                moved = true;
            }
            else
            {
                copied = true;
                await pathMover.CopyAsync(
                    preview.SourcePath,
                    preview.DestinationPath,
                    cancellationToken);
                var copiedManifest = MoveManifest(
                    preview.Target,
                    preview.SourcePath,
                    preview.DestinationPath);
                var sourceHash = await assetHasher.ComputeHashAsync(
                    ToAsset(preview.Target, preview.SourcePath),
                    cancellationToken);
                var destinationHash = await assetHasher.ComputeHashAsync(
                    ToAsset(copiedManifest, copiedManifest.Location),
                    cancellationToken);
                if (!string.Equals(
                        sourceHash,
                        preview.Target.AssetHash,
                        StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(
                        sourceHash,
                        destinationHash,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "跨卷复制后的哈希校验失败。");
                }
            }

            var movedManifest = MoveManifest(
                preview.Target,
                preview.SourcePath,
                preview.DestinationPath);
            await activationLink.SetTargetAsync(
                movedManifest.StableActivationPath,
                movedManifest.Location,
                cancellationToken);
            await manifestStore.SaveAsync(
                movedManifest,
                cancellationToken);

            operation = await SaveTransitionAsync(
                OperationStateMachine.BeginVerification(
                    operation,
                    timeProvider),
                cancellationToken);
            var health = await healthCheck.CheckAsync(
                movedManifest,
                movedManifest.ManagedEntryPath,
                cancellationToken);
            if (!health.IsHealthy)
            {
                throw new InvalidOperationException(
                    $"健康检查失败: {health.Message}");
            }

            if (!preview.WasActive)
            {
                await activationLink.SetTargetAsync(
                    preview.StableActivationPath,
                    preview.PreviousActivationTarget,
                    cancellationToken);
            }

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
            var failureReason = exception.Message;
            var rollbackSucceeded = true;
            try
            {
                await RestoreAsync(
                    preview,
                    recoveryPoint,
                    moved,
                    copied,
                    CancellationToken.None);
                failureReason = $"{failureReason}；已恢复原激活路径。";
            }
            catch (Exception rollbackException)
            {
                rollbackSucceeded = false;
                failureReason =
                    $"{failureReason}；恢复原激活路径失败: {rollbackException.Message}";
            }

            operation = OperationStateMachine.Fail(
                operation,
                failureReason,
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
                failureReason,
                exception);
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
        if (operation.Type != OperationType.Migrate
            || operation.State is OperationState.Succeeded
                or OperationState.RolledBack)
        {
            throw new InvalidOperationException("该操作当前状态不能回滚。");
        }

        if (string.IsNullOrWhiteSpace(operation.RecoveryPointId)
            || string.IsNullOrWhiteSpace(operation.Target))
        {
            throw new InvalidOperationException("迁移操作缺少恢复点或目标路径。");
        }

        var recoveryPoint = await recoveryPointStore.GetAsync(
            operation.RecoveryPointId,
            cancellationToken)
            ?? throw new InvalidOperationException("恢复点不存在，无法回滚。");
        var original = recoveryPoint.PreviousManifest
            ?? throw new InvalidOperationException("恢复点缺少原环境记录。");
        var sourcePath = GetSourcePath(original);
        var destinationPath = Path.GetFullPath(operation.Target);
        if (!string.Equals(
                operation.TargetIdentity,
                original.Fingerprint.Value,
                StringComparison.Ordinal)
            || !PathsEqual(operation.SourceTarget ?? "", sourcePath)
            || !PathsEqual(
                operation.StableActivationPath ?? "",
                original.StableActivationPath))
        {
            throw new InvalidOperationException(
                "迁移操作与原环境身份不一致，拒绝回滚。");
        }

        var destinationExists = Directory.Exists(destinationPath)
            || File.Exists(destinationPath);
        var sourceExists = Directory.Exists(sourcePath);
        if (!Enum.TryParse<MigrationStrategy>(
                operation.MigrationStrategy,
                ignoreCase: true,
                out var strategy))
        {
            throw new InvalidOperationException(
                "迁移操作缺少有效的迁移策略。");
        }

        var moved = false;
        var copied = false;
        if (strategy == MigrationStrategy.CopyAndVerify)
        {
            if (!sourceExists)
            {
                throw new DirectoryNotFoundException(
                    "跨卷迁移必须保留原目录，但原目录不存在。");
            }

            copied = destinationExists;
        }
        else
        {
            if (destinationExists && sourceExists)
            {
                throw new InvalidOperationException(
                    "迁移源和迁移目标同时存在，无法确定回滚来源。");
            }

            if (!destinationExists && !sourceExists)
            {
                throw new DirectoryNotFoundException(
                    "迁移源和目标均不存在，无法回滚。");
            }

            moved = destinationExists;
        }

        if (destinationExists)
        {
            var destinationHash = await assetHasher.ComputeHashAsync(
                ToAsset(original, destinationPath),
                cancellationToken);
            if (!string.Equals(
                    destinationHash,
                    original.AssetHash,
                    StringComparison.OrdinalIgnoreCase)
                && operation.State != OperationState.Executing)
            {
                throw new InvalidOperationException(
                    "迁移目标内容校验失败，拒绝回滚。");
            }
        }

        var preview = new MigrationPreview(
            original.Fingerprint,
            original,
            sourcePath,
            destinationPath,
            original.StableActivationPath,
            GetManagedEntryPath(original),
            operation.PreviousTarget ?? sourcePath,
            PathsEqual(
                operation.PreviousTarget ?? sourcePath,
                sourcePath),
            strategy,
            new MigrationPathStatistics(0, 0, 0),
            operation.Impact ?? "恢复迁移前状态。",
            operation.ExpectedResult ?? "恢复迁移前状态。",
            operation.Id,
            recoveryPoint.Id);
        await RestoreAsync(
            preview,
            recoveryPoint,
            moved,
            copied,
            cancellationToken);

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

    private async Task<MigrationPreview> EnsurePreviewIsCurrentAsync(
        MigrationPreview preview,
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
            || string.IsNullOrWhiteSpace(operation.Target)
            || !PathsEqual(
                preview.DestinationPath,
                operation.Target)
            || string.IsNullOrWhiteSpace(operation.PreviousTarget)
            || !PathsEqual(
                preview.PreviousActivationTarget,
                operation.PreviousTarget)
            || !string.Equals(
                preview.ExpectedResult,
                operation.ExpectedResult,
                StringComparison.Ordinal)
            || !string.Equals(
                preview.TargetFingerprint.Value,
                operation.TargetIdentity,
                StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(operation.SourceTarget)
            || !PathsEqual(preview.SourcePath, operation.SourceTarget)
            || string.IsNullOrWhiteSpace(operation.StableActivationPath)
            || !PathsEqual(
                preview.StableActivationPath,
                operation.StableActivationPath)
            || !string.Equals(
                preview.Strategy.ToString(),
                operation.MigrationStrategy,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "迁移计划已过期，请重新预览。");
        }

        var currentActivationTarget = await activationLink.GetTargetAsync(
            preview.StableActivationPath,
            cancellationToken);
        if (currentActivationTarget is null
            || !PathsEqual(
                currentActivationTarget,
                preview.PreviousActivationTarget))
        {
            throw new InvalidOperationException(
                "迁移计划已过期，请重新预览。");
        }

        var currentStatistics = await pathMover.InspectAsync(
            preview.SourcePath,
            preview.DestinationPath,
            cancellationToken);
        if (currentStatistics.FileCount != preview.Statistics.FileCount
            || currentStatistics.TotalBytes != preview.Statistics.TotalBytes)
        {
            throw new InvalidOperationException(
                "迁移计划已过期，请重新预览。");
        }

        ValidateMigrationResources(
            preview.Strategy,
            currentStatistics);
        var current = await manifestStore.FindByFingerprintAsync(
            preview.TargetFingerprint,
            cancellationToken);
        if (current is null
            || current != NormalizeManifest(preview.Target)
            || !PathsEqual(preview.SourcePath, GetSourcePath(current))
            || !PathsEqual(
                preview.StableActivationPath,
                current.StableActivationPath)
            || !PathsEqual(
                preview.ManagedEntryPath,
                GetManagedEntryPath(current)))
        {
            throw new InvalidOperationException(
                "迁移计划已过期，请重新预览。");
        }

        return preview with
        {
            Target = NormalizeManifest(current),
            SourcePath = GetSourcePath(current),
            ManagedEntryPath = GetManagedEntryPath(current)
        };
    }

    private async Task ValidateMigrationAsync(
        EnvironmentManifest manifest,
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        if (manifest.IsSystemComponent)
        {
            throw new InvalidOperationException("系统组件不能迁移。");
        }

        if (!Directory.Exists(sourcePath))
        {
            throw new DirectoryNotFoundException(
                $"待迁移环境目录不存在: {sourcePath}");
        }

        if (File.GetAttributes(sourcePath)
            .HasFlag(FileAttributes.ReparsePoint))
        {
            throw new InvalidOperationException(
                "待迁移环境不能是 Junction 或符号链接。");
        }

        if (Directory.Exists(destinationPath)
            || File.Exists(destinationPath))
        {
            throw new InvalidOperationException(
                $"目标路径已存在: {destinationPath}");
        }

        ValidateNoReparsePointAncestors(destinationPath);
        ValidateWritableDestination(destinationPath);
        var strategy = pathMover.GetStrategy(
            sourcePath,
            destinationPath);
        if (strategy == MigrationStrategy.AtomicRename)
        {
            ValidateSourceParentMutation(sourcePath);
        }

        var managedEntryPath = GetManagedEntryPath(manifest);
        var managedEntryTarget = await activationLink.GetTargetAsync(
            managedEntryPath,
            cancellationToken);
        if (managedEntryTarget is null
            || !PathsEqual(
                managedEntryTarget,
                manifest.StableActivationPath))
        {
            throw new InvalidOperationException(
                "受管入口未指向稳定激活路径，无法安全迁移。");
        }

        var blockers = await occupancyProbe.FindBlockersAsync(
            sourcePath,
            destinationPath,
            cancellationToken);
        if (blockers.Count > 0)
        {
            throw new InvalidOperationException(
                $"迁移前检测到占用或文件锁: {string.Join("；", blockers.Select(blocker => blocker.Message))}");
        }
    }

    private static void ValidateMigrationResources(
        MigrationStrategy strategy,
        MigrationPathStatistics statistics)
    {
        if (strategy == MigrationStrategy.CopyAndVerify
            && statistics.TotalBytes > statistics.AvailableBytes)
        {
            throw new InvalidOperationException(
                $"目标卷空间不足，需要 {statistics.TotalBytes} 字节，可用 {statistics.AvailableBytes} 字节。");
        }
    }

    private static void ValidateNoReparsePointAncestors(string destinationPath)
    {
        var current = Path.GetDirectoryName(
            Path.GetFullPath(destinationPath));
        while (!string.IsNullOrWhiteSpace(current))
        {
            if (File.Exists(current) && !Directory.Exists(current))
            {
                throw new InvalidOperationException(
                    $"目标路径的祖先位置被文件占用: {current}");
            }

            if (Directory.Exists(current)
                && File.GetAttributes(current)
                    .HasFlag(FileAttributes.ReparsePoint))
            {
                throw new InvalidOperationException(
                    $"目标路径包含 Junction 或符号链接: {current}");
            }

            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrWhiteSpace(parent)
                || string.Equals(
                    parent,
                    current,
                    StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            current = parent;
        }
    }

    private static void ValidateSourceParentMutation(string sourcePath)
    {
        var parent = Path.GetDirectoryName(
            Path.GetFullPath(sourcePath))
            ?? throw new InvalidOperationException(
                "源路径缺少父目录。");
        var probePath = Path.Combine(
            parent,
            $".agent-env-manager-rename-{Guid.NewGuid():N}.tmp");
        try
        {
            Directory.CreateDirectory(probePath);
            Directory.Delete(probePath);
        }
        catch (Exception exception)
            when (exception is IOException
                or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                $"源父目录不可写或无法删除探测目录: {parent}",
                exception);
        }
    }

    private static void ValidateWritableDestination(string destinationPath)
    {
        var current = Path.GetDirectoryName(
            Path.GetFullPath(destinationPath));
        while (!string.IsNullOrWhiteSpace(current)
            && !Directory.Exists(current))
        {
            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrWhiteSpace(parent)
                || string.Equals(
                    parent,
                    current,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "目标路径缺少可写父目录。");
            }

            current = parent;
        }

        if (string.IsNullOrWhiteSpace(current))
        {
            throw new InvalidOperationException(
                "目标路径缺少可写父目录。");
        }

        var probePath = Path.Combine(
            current,
            $".agent-env-manager-write-{Guid.NewGuid():N}.tmp");
        try
        {
            using var stream = new FileStream(
                probePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 1,
                FileOptions.DeleteOnClose);
        }
        catch (Exception exception)
            when (exception is IOException
                or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                $"目标父目录不可写: {current}",
                exception);
        }
    }

    private async Task RestoreAsync(
        MigrationPreview preview,
        AdoptionRecoveryPoint recoveryPoint,
        bool moved,
        bool copied,
        CancellationToken cancellationToken)
    {
        if (moved)
        {
            await pathMover.MoveBackAsync(
                preview.DestinationPath,
                preview.SourcePath,
                cancellationToken);
        }

        if (copied)
        {
            await pathMover.DeleteAsync(
                preview.DestinationPath,
                cancellationToken);
        }

        await activationLink.SetTargetAsync(
            preview.StableActivationPath,
            preview.PreviousActivationTarget,
            cancellationToken);
        var original = recoveryPoint.PreviousManifest
            ?? preview.Target;
        await manifestStore.SaveAsync(
            original,
            cancellationToken);
    }

    private static EnvironmentManifest MoveManifest(
        EnvironmentManifest manifest,
        string sourcePath,
        string destinationPath)
    {
        var relativePath = Path.GetRelativePath(
            sourcePath,
            manifest.Location);
        return manifest with
        {
            Location = Path.GetFullPath(
                Path.Combine(destinationPath, relativePath))
        };
    }

    private string GetSourcePath(EnvironmentManifest manifest)
    {
        return Path.GetFullPath(
            activationLink.GetActivationTarget(manifest.Location));
    }

    private string GetManagedEntryPath(EnvironmentManifest manifest)
    {
        if (!string.IsNullOrWhiteSpace(manifest.ManagedEntryPath))
        {
            return manifest.ManagedEntryPath;
        }

        var activationKey = string.IsNullOrWhiteSpace(
            manifest.ActivationIdentity)
            ? RuntimeActivationKey.Create(
                new EnvironmentAsset(
                    manifest.Kind,
                    manifest.Name,
                    manifest.Version,
                    manifest.Location,
                    manifest.IsSystemComponent,
                    manifest.Source))
            : new ActivationKey(
                new EnvironmentIdentity(manifest.ActivationIdentity));
        return activationPathFactory.CreateManagedEntry(activationKey);
    }

    private EnvironmentManifest NormalizeManifest(
        EnvironmentManifest manifest)
    {
        return manifest with
        {
            Location = Path.GetFullPath(manifest.Location),
            StableActivationPath = Path.GetFullPath(
                manifest.StableActivationPath),
            ManagedEntryPath = GetManagedEntryPath(manifest)
        };
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
            "迁移前记录原环境路径。",
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

    private async Task<OperationRecord> SaveTransitionAsync(
        OperationRecord operation,
        CancellationToken cancellationToken)
    {
        await operationJournal.SaveAsync(operation, cancellationToken);
        return operation;
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            StringComparison.OrdinalIgnoreCase);
    }
}
