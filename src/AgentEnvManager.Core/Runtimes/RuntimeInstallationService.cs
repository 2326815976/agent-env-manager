using AgentEnvManager.Core.Activation;
using AgentEnvManager.Core.Adoption;
using AgentEnvManager.Core.Inspection;
using AgentEnvManager.Core.Operations;

namespace AgentEnvManager.Core.Runtimes;

internal sealed class RuntimeInstallationService(
    IReadOnlyDictionary<string, IRuntimeProvider> providers,
    IRuntimeCommandRunner commandRunner,
    IEnvironmentManifestStore manifestStore,
    IEnvironmentRecoveryPointStore recoveryPointStore,
    IOperationJournal operationJournal,
    IStableActivationPathFactory activationPathFactory,
    IEnvironmentActivationLink activationLink,
    IEnvironmentIndex environmentIndex,
    VersionSwitcher switcher,
    string runtimeRoot,
    string runtimeStateRoot,
    TimeProvider timeProvider)
{
    public async Task<RuntimeInstallPreview> PreviewAsync(
        string providerId,
        string version,
        CancellationToken cancellationToken = default)
    {
        var provider = GetProvider(providerId);
        var artifact = provider.Descriptor.Artifacts.SingleOrDefault(
            candidate => string.Equals(
                candidate.Version,
                version,
                StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException(
                $"运行时提供者 {providerId} 不支持版本 {version}。");
        ValidateArtifact(provider.Descriptor, artifact);

        var fingerprint = CreateFingerprint(
            provider.Descriptor.Id,
            artifact.Version);
        var identity = new EnvironmentIdentity(fingerprint.Value);
        var installRoot = Path.Combine(
            runtimeRoot,
            provider.Descriptor.Id,
            artifact.Version);
        var executableRelativePath = provider.GetExecutableRelativePath(
            artifact);
        var location = Path.Combine(
            installRoot,
            Path.GetDirectoryName(executableRelativePath)
                ?? throw new InvalidOperationException(
                    "运行时可执行文件相对路径无效。"));
        var executablePath = Path.Combine(
            installRoot,
            executableRelativePath);
        var asset = new EnvironmentAsset(
            provider.Descriptor.Kind,
            provider.Descriptor.Name,
            artifact.Version,
            location,
            IsSystemComponent: false,
            provider.Descriptor.Source);
        var activationKey = RuntimeActivationKey.Create(asset);
        var stableActivationPath = activationPathFactory.Create(
            activationKey);
        var managedEntryPath = activationPathFactory.CreateManagedEntry(
            activationKey);
        var existing = await manifestStore.FindByFingerprintAsync(
            fingerprint,
            cancellationToken);
        if (existing is not null)
        {
            if (existing.AssetHash != artifact.Sha256
                || !Directory.Exists(existing.Location))
            {
                throw new InvalidOperationException(
                    "该版本已有不同的纳管记录，请先处理冲突。");
            }

            return CreatePreview(
                provider.Descriptor,
                artifact,
                existing.Identity,
                existing.Fingerprint,
                installRoot,
                existing.Location,
                Path.Combine(
                    existing.Location,
                    Path.GetFileName(executableRelativePath)),
                existing.ActivationIdentity,
                existing.StableActivationPath,
                existing.ManagedEntryPath,
                "该版本已安装并已纳管。",
                isAlreadyInstalled: true);
        }

        if (Directory.Exists(installRoot)
            || File.Exists(executablePath))
        {
            throw new InvalidOperationException(
                "目标安装目录已存在但未纳管，拒绝覆盖。");
        }

        var active = await FindManagedActiveAsync(
            stableActivationPath,
            cancellationToken);
        var impact =
            $"安装 {provider.Descriptor.Name} {artifact.Version} 到 {installRoot}；" +
            "安装或健康检查失败不会改变当前激活版本。";
        var adoptionPreview = new AdoptionPreview(
            fingerprint,
            identity,
            asset,
            artifact.Sha256,
            stableActivationPath,
            impact,
            IsAlreadyManaged: false,
            ExistingIdentity: null,
            ManagedEntryPath: managedEntryPath);
        var operation = OperationStateMachine.Create(
            OperationType.Install,
            $"安装 {provider.Descriptor.Name} {artifact.Version}",
            timeProvider,
            target: installRoot,
            impact: impact,
            expectedResult:
                $"{provider.Descriptor.Name} {artifact.Version} 可通过受管入口调用。");
        await operationJournal.SaveAsync(operation, cancellationToken);
        operation = await SaveTransitionAsync(
            OperationStateMachine.MarkValidated(operation, timeProvider),
            cancellationToken);
        var recoveryPoint = await recoveryPointStore.CreateAsync(
            adoptionPreview,
            operation.Id,
            active,
            cancellationToken);
        operation = await SaveTransitionAsync(
            OperationStateMachine.MarkRecoveryReady(
                operation,
                recoveryPoint.Id,
                timeProvider),
            cancellationToken);

        return CreatePreview(
            provider.Descriptor,
            artifact,
            identity,
            fingerprint,
            installRoot,
            location,
            executablePath,
            activationKey.Value,
            stableActivationPath,
            managedEntryPath,
            impact,
            isAlreadyInstalled: false,
            operation.Id,
            recoveryPoint.Id);
    }

    public async Task<InstalledRuntime> InstallAsync(
        RuntimeInstallPreview preview,
        CancellationToken cancellationToken = default)
    {
        var provider = GetProvider(preview.Provider.Id);
        var artifact = GetArtifact(provider, preview.Artifact.Version);
        await ValidatePreviewAsync(
            provider,
            artifact,
            preview,
            cancellationToken);

        if (preview.IsAlreadyInstalled)
        {
            return await ActivateExistingAsync(
                preview,
                provider,
                artifact,
                cancellationToken);
        }

        var operation = await GetInstallOperationAsync(
            preview,
            cancellationToken);
        var installRootCreated = false;
        EnvironmentManifest? savedManifest = null;
        try
        {
            operation = await SaveTransitionAsync(
                OperationStateMachine.BeginExecution(
                    operation,
                    timeProvider),
                cancellationToken);
            if (Directory.Exists(preview.InstallRoot)
                || File.Exists(preview.ExecutablePath))
            {
                throw new InvalidOperationException(
                    "安装目标已存在，拒绝覆盖。");
            }

            Directory.CreateDirectory(preview.InstallRoot);
            installRootCreated = true;
            var command = provider.CreateInstallCommand(
                new RuntimeInstallContext(
                    artifact,
                    preview.InstallRoot,
                    Path.Combine(
                        runtimeStateRoot,
                        preview.Identity.Value)));
            var commandResult = await commandRunner.RunAsync(
                command.Executable,
                command.Arguments,
                command.WorkingDirectory,
                new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase),
                cancellationToken);
            if (commandResult.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    CreateCommandFailure(
                        $"安装 {provider.Descriptor.Name} {artifact.Version}",
                        commandResult));
            }

            if (!File.Exists(preview.ExecutablePath))
            {
                throw new InvalidOperationException(
                    $"安装后未找到可执行文件: {preview.ExecutablePath}");
            }

            RemoveTopLevelReparsePoints(preview.InstallRoot);
            savedManifest = await manifestStore.SaveAsync(
                CreateManifest(preview, operation),
                cancellationToken);
            await RebuildIndexAsync(cancellationToken);
            operation = await SaveTransitionAsync(
                OperationStateMachine.BeginVerification(
                    operation,
                    timeProvider),
                cancellationToken);

            var switchPreview = await switcher.PreviewAsync(
                preview.Fingerprint,
                cancellationToken);
            var switchOperation = await switcher.SwitchAsync(
                switchPreview,
                cancellationToken);
            if (switchOperation.State != OperationState.Succeeded)
            {
                throw new InvalidOperationException(
                    "运行时切换未完成。");
            }

            operation = OperationStateMachine.Complete(
                operation,
                timeProvider);
            await operationJournal.SaveAsync(
                operation,
                cancellationToken);
            return new InstalledRuntime(
                provider.Descriptor,
                artifact,
                savedManifest,
                preview.ExecutablePath,
                preview.ManagedEntryPath);
        }
        catch (Exception exception)
        {
            var failureReason = exception.Message;
            var rollbackSucceeded = false;
            try
            {
                if (await IsActivationRestoredAsync(
                    preview,
                    operation,
                    CancellationToken.None))
                {
                    await CleanupFailedInstallAsync(
                        savedManifest,
                        installRootCreated ? preview.InstallRoot : null,
                        CancellationToken.None);
                    rollbackSucceeded = true;
                }
                else
                {
                    failureReason =
                        $"{failureReason}；未确认原激活版本已恢复，" +
                        "保留新安装目录和纳管记录。";
                }
            }
            catch (Exception cleanupException)
            {
                rollbackSucceeded = false;
                failureReason =
                    $"{failureReason}；清理失败安装失败: " +
                    cleanupException.Message;
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
        if (operation.Type != OperationType.Install)
        {
            throw new InvalidOperationException(
                "该操作不是运行时安装操作。");
        }

        if (operation.State is OperationState.Succeeded
            or OperationState.RolledBack)
        {
            operation = OperationStateMachine.Reject(
                operation,
                OperationState.RolledBack,
                "该操作当前状态不能回滚。",
                timeProvider);
            await operationJournal.SaveAsync(
                operation,
                CancellationToken.None);
            throw new InvalidOperationException(
                "该操作当前状态不能回滚。");
        }

        if (string.IsNullOrWhiteSpace(operation.RecoveryPointId))
        {
            throw new InvalidOperationException(
                "安装操作没有可用的恢复点。");
        }

        var recoveryPoint = await recoveryPointStore.GetAsync(
            operation.RecoveryPointId,
            cancellationToken)
            ?? throw new InvalidOperationException(
                "恢复点不存在，无法回滚。");
        var current = await manifestStore.FindByFingerprintAsync(
            recoveryPoint.Fingerprint,
            cancellationToken);
        if (current is not null
            && current.OperationId != operation.Id)
        {
            throw new InvalidOperationException(
                "目标 manifest 已被后续操作更新，拒绝覆盖。");
        }

        if (current is not null)
        {
            if (recoveryPoint.PreviousManifest is null)
            {
                await activationLink.DeleteAsync(
                    current.ManagedEntryPath,
                    cancellationToken);
                await activationLink.DeleteAsync(
                    current.StableActivationPath,
                    cancellationToken);
            }
            else
            {
                await activationLink.SetTargetAsync(
                    current.StableActivationPath,
                    recoveryPoint.PreviousManifest.Location,
                    cancellationToken);
                await EnsureManagedEntryAsync(
                    current.ManagedEntryPath,
                    current.StableActivationPath,
                    cancellationToken);
                var expectedTarget =
                    activationLink.GetActivationTarget(
                        recoveryPoint.PreviousManifest.Location);
                var actualTarget = await activationLink.GetTargetAsync(
                    current.StableActivationPath,
                    cancellationToken);
                if (actualTarget is null
                    || !PathsEqual(expectedTarget, actualTarget))
                {
                    throw new InvalidOperationException(
                        "恢复安装前激活版本失败。");
                }
            }

            await manifestStore.DeleteAsync(
                current.Identity,
                cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(operation.Target)
            && Directory.Exists(operation.Target))
        {
            DeleteInstallRoot(operation.Target);
        }

        await RebuildIndexAsync(cancellationToken);
        if (operation.State != OperationState.Failed)
        {
            operation = OperationStateMachine.Fail(
                operation,
                "用户请求从恢复点回滚。",
                timeProvider);
            await operationJournal.SaveAsync(
                operation,
                cancellationToken);
        }

        operation = OperationStateMachine.Rollback(
            operation,
            timeProvider);
        await operationJournal.SaveAsync(
            operation,
            cancellationToken);
        return operation;
    }

    private async Task<InstalledRuntime> ActivateExistingAsync(
        RuntimeInstallPreview preview,
        IRuntimeProvider provider,
        RuntimeArtifactDescriptor artifact,
        CancellationToken cancellationToken)
    {
        var manifest = await manifestStore.FindByFingerprintAsync(
            preview.Fingerprint,
            cancellationToken)
            ?? throw new InvalidOperationException(
                "已安装运行时缺少纳管记录。");
        await FindManagedActiveAsync(
            manifest.StableActivationPath,
            cancellationToken);
        var currentTarget = await activationLink.GetTargetAsync(
            manifest.StableActivationPath,
            cancellationToken);
        if (currentTarget is null
            || !PathsEqual(
                activationLink.GetActivationTarget(manifest.Location),
                currentTarget))
        {
            var switchPreview = await switcher.PreviewAsync(
                manifest.Fingerprint,
                cancellationToken);
            await switcher.SwitchAsync(
                switchPreview,
                cancellationToken);
        }

        return new InstalledRuntime(
            provider.Descriptor,
            artifact,
            manifest,
            preview.ExecutablePath,
            manifest.ManagedEntryPath);
    }

    private async Task EnsureManagedEntryAsync(
        string managedEntryPath,
        string activationPath,
        CancellationToken cancellationToken)
    {
        var currentTarget = await activationLink.GetTargetAsync(
            managedEntryPath,
            cancellationToken);
        if (currentTarget is not null
            && PathsEqual(currentTarget, activationPath))
        {
            return;
        }

        await activationLink.SetTargetAsync(
            managedEntryPath,
            activationPath,
            cancellationToken);
    }

    private async Task<OperationRecord> GetInstallOperationAsync(
        RuntimeInstallPreview preview,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(preview.OperationId)
            || string.IsNullOrWhiteSpace(preview.RecoveryPointId))
        {
            throw new InvalidOperationException(
                "安装计划缺少操作或恢复点。");
        }

        var operation = await operationJournal.GetAsync(
            preview.OperationId,
            cancellationToken)
            ?? throw new InvalidOperationException("安装计划不存在。");
        if (operation.Type != OperationType.Install
            || operation.State != OperationState.RecoveryReady
            || operation.RecoveryPointId != preview.RecoveryPointId
            || !PathsEqual(
                operation.Target
                    ?? throw new InvalidOperationException(
                        "安装计划缺少目标。"),
                preview.InstallRoot))
        {
            throw new InvalidOperationException(
                "安装计划状态无效或已过期。");
        }

        return operation;
    }

    private EnvironmentManifest CreateManifest(
        RuntimeInstallPreview preview,
        OperationRecord operation)
    {
        return new EnvironmentManifest(
            preview.Identity,
            preview.Fingerprint,
            preview.Provider.Kind,
            preview.Provider.Name,
            preview.Artifact.Version,
            preview.Provider.Source,
            preview.Location,
            preview.StableActivationPath,
            preview.Artifact.Sha256,
            preview.RecoveryPointId
                ?? throw new InvalidOperationException(
                    "安装计划缺少恢复点。"),
            operation.Id,
            IsSystemComponent: false,
            timeProvider.GetUtcNow(),
            preview.ActivationIdentity,
            preview.ManagedEntryPath);
    }

    private async Task CleanupFailedInstallAsync(
        EnvironmentManifest? savedManifest,
        string? installRoot,
        CancellationToken cancellationToken)
    {
        if (savedManifest is not null)
        {
            await manifestStore.DeleteAsync(
                savedManifest.Identity,
                cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(installRoot)
            && Directory.Exists(installRoot))
        {
            DeleteInstallRoot(installRoot);
        }

        await RebuildIndexAsync(cancellationToken);
    }

    private void DeleteInstallRoot(string installRoot)
    {
        var fullRuntimeRoot = Path.GetFullPath(runtimeRoot);
        var fullInstallRoot = Path.GetFullPath(installRoot);
        var allowedPrefix = fullRuntimeRoot.EndsWith(
            Path.DirectorySeparatorChar)
            ? fullRuntimeRoot
            : fullRuntimeRoot + Path.DirectorySeparatorChar;
        if (!fullInstallRoot.StartsWith(
            allowedPrefix,
            StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "拒绝删除运行时根目录之外的路径。");
        }

        DeleteDirectoryTree(fullInstallRoot);
    }

    private static void DeleteDirectoryTree(string path)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(path))
        {
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    Directory.Delete(entry, recursive: false);
                }
                else
                {
                    File.Delete(entry);
                }

                continue;
            }

            if ((attributes & FileAttributes.Directory) != 0)
            {
                DeleteDirectoryTree(entry);
            }
            else
            {
                File.Delete(entry);
            }
        }

        Directory.Delete(path, recursive: false);
    }

    private static void RemoveTopLevelReparsePoints(string installRoot)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(
            installRoot))
        {
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) == 0)
            {
                continue;
            }

            if ((attributes & FileAttributes.Directory) != 0)
            {
                Directory.Delete(entry, recursive: false);
            }
            else
            {
                File.Delete(entry);
            }
        }
    }

    private async Task RebuildIndexAsync(
        CancellationToken cancellationToken)
    {
        var manifests = await manifestStore.ReadAllAsync(
            cancellationToken);
        await environmentIndex.RebuildAsync(
            manifests,
            cancellationToken);
    }

    private async Task<EnvironmentManifest?> FindManagedActiveAsync(
        string stableActivationPath,
        CancellationToken cancellationToken)
    {
        var currentTarget = await activationLink.GetTargetAsync(
            stableActivationPath,
            cancellationToken);
        if (currentTarget is null)
        {
            return null;
        }

        var manifests = await manifestStore.ReadAllAsync(cancellationToken);
        var active = manifests.FirstOrDefault(manifest =>
            PathsEqual(
                activationLink.GetActivationTarget(manifest.Location),
                currentTarget));
        return active
            ?? throw new InvalidOperationException(
                "当前激活点不由管理器纳管，拒绝自动覆盖。");
    }

    private async Task<bool> IsActivationRestoredAsync(
        RuntimeInstallPreview preview,
        OperationRecord operation,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(operation.RecoveryPointId))
        {
            return false;
        }

        var recoveryPoint = await recoveryPointStore.GetAsync(
            operation.RecoveryPointId,
            cancellationToken);
        if (recoveryPoint is null)
        {
            return false;
        }

        var actualTarget = await activationLink.GetTargetAsync(
            preview.StableActivationPath,
            cancellationToken);
        if (recoveryPoint.PreviousManifest is null)
        {
            return actualTarget is null;
        }

        var expectedTarget = activationLink.GetActivationTarget(
            recoveryPoint.PreviousManifest.Location);
        return actualTarget is not null
            && PathsEqual(actualTarget, expectedTarget);
    }

    private async Task<OperationRecord> SaveTransitionAsync(
        OperationRecord operation,
        CancellationToken cancellationToken)
    {
        await operationJournal.SaveAsync(operation, cancellationToken);
        return operation;
    }

    private IRuntimeProvider GetProvider(string providerId)
    {
        return providers.TryGetValue(providerId, out var provider)
            ? provider
            : throw new KeyNotFoundException(
                $"未注册运行时提供者: {providerId}");
    }

    private static RuntimeArtifactDescriptor GetArtifact(
        IRuntimeProvider provider,
        string version)
    {
        return provider.Descriptor.Artifacts.SingleOrDefault(
            artifact => string.Equals(
                artifact.Version,
                version,
                StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException(
                $"运行时提供者 {provider.Descriptor.Id} " +
                $"不支持版本 {version}。");
    }

    private async Task ValidatePreviewAsync(
        IRuntimeProvider provider,
        RuntimeArtifactDescriptor artifact,
        RuntimeInstallPreview preview,
        CancellationToken cancellationToken)
    {
        ValidateArtifact(provider.Descriptor, artifact);
        if (preview.IsAlreadyInstalled)
        {
            await ValidateExistingPreviewAsync(
                provider,
                artifact,
                preview,
                cancellationToken);
            return;
        }

        var expectedFingerprint = CreateFingerprint(
            provider.Descriptor.Id,
            artifact.Version);
        var expectedInstallRoot = Path.Combine(
            runtimeRoot,
            provider.Descriptor.Id,
            artifact.Version);
        var executableRelativePath = provider.GetExecutableRelativePath(
            artifact);
        var expectedLocation = Path.Combine(
            expectedInstallRoot,
            Path.GetDirectoryName(executableRelativePath)
                ?? throw new InvalidOperationException(
                    "运行时可执行文件相对路径无效。"));
        var expectedExecutablePath = Path.Combine(
            expectedInstallRoot,
            executableRelativePath);
        var expectedAsset = new EnvironmentAsset(
            provider.Descriptor.Kind,
            provider.Descriptor.Name,
            artifact.Version,
            expectedLocation,
            IsSystemComponent: false,
            provider.Descriptor.Source);
        var expectedActivationKey = RuntimeActivationKey.Create(
            expectedAsset);
        var expectedStableActivationPath = activationPathFactory.Create(
            expectedActivationKey);
        var expectedManagedEntryPath =
            activationPathFactory.CreateManagedEntry(
                expectedActivationKey);

        if (preview.Fingerprint != expectedFingerprint
            || !PathsEqual(preview.InstallRoot, expectedInstallRoot)
            || !PathsEqual(preview.Location, expectedLocation)
            || !PathsEqual(preview.ExecutablePath, expectedExecutablePath)
            || !string.Equals(
                preview.ActivationIdentity,
                expectedActivationKey.Value,
                StringComparison.Ordinal)
            || !PathsEqual(
                preview.StableActivationPath,
                expectedStableActivationPath)
            || !PathsEqual(
                preview.ManagedEntryPath,
                expectedManagedEntryPath))
        {
            throw new InvalidOperationException(
                "安装计划已过期，请重新预览。");
        }
    }

    private async Task ValidateExistingPreviewAsync(
        IRuntimeProvider provider,
        RuntimeArtifactDescriptor artifact,
        RuntimeInstallPreview preview,
        CancellationToken cancellationToken)
    {
        var manifest = await manifestStore.FindByFingerprintAsync(
            preview.Fingerprint,
            cancellationToken)
            ?? throw new InvalidOperationException(
                "安装计划已过期，请重新预览。");
        var executableName = Path.GetFileName(
            provider.GetExecutableRelativePath(artifact));
        if (manifest.Identity != preview.Identity
            || manifest.Fingerprint != preview.Fingerprint
            || manifest.Kind != provider.Descriptor.Kind
            || !string.Equals(
                manifest.Name,
                provider.Descriptor.Name,
                StringComparison.Ordinal)
            || !string.Equals(
                manifest.Version,
                artifact.Version,
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(
                manifest.AssetHash,
                artifact.Sha256,
                StringComparison.Ordinal)
            || !PathsEqual(manifest.Location, preview.Location)
            || !PathsEqual(
                Path.Combine(manifest.Location, executableName),
                preview.ExecutablePath)
            || !string.Equals(
                manifest.ActivationIdentity,
                preview.ActivationIdentity,
                StringComparison.Ordinal)
            || !PathsEqual(
                manifest.StableActivationPath,
                preview.StableActivationPath)
            || !PathsEqual(
                manifest.ManagedEntryPath,
                preview.ManagedEntryPath)
            || !Directory.Exists(manifest.Location))
        {
            throw new InvalidOperationException(
                "安装计划已过期，请重新预览。");
        }
    }

    private static string CreateCommandFailure(
        string action,
        RuntimeCommandResult result)
    {
        var detail = string.IsNullOrWhiteSpace(result.StandardError)
            ? result.StandardOutput.Trim()
            : result.StandardError.Trim();
        return string.IsNullOrWhiteSpace(detail)
            ? $"{action}失败，退出码 {result.ExitCode}。"
            : $"{action}失败: {detail}";
    }

    private static void ValidateArtifact(
        RuntimeProviderDescriptor provider,
        RuntimeArtifactDescriptor artifact)
    {
        if (provider.Mode != RuntimeProviderMode.Installable
            || artifact.InstallStrategy
                == RuntimeInstallStrategy.ObservedRebuild)
        {
            throw new InvalidOperationException(
                $"{provider.Name} 不是可安装运行时。");
        }

        if (string.IsNullOrWhiteSpace(artifact.OfficialSource)
            || string.IsNullOrWhiteSpace(artifact.License)
            || string.IsNullOrWhiteSpace(artifact.Sha256)
            || string.IsNullOrWhiteSpace(artifact.DownloadUrl))
        {
            throw new InvalidOperationException(
                $"{provider.Name} 缺少来源、许可证、哈希或下载地址。");
        }

        if (!artifact.SupportedArchitectures.Contains(
            "win-x64",
            StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"{provider.Name} 不支持当前 Windows x64 架构。");
        }
    }

    private static EnvironmentFingerprint CreateFingerprint(
        string providerId,
        string version)
    {
        return new EnvironmentFingerprint(
            $"runtime-{providerId}-{version}-win-x64");
    }

    private static RuntimeInstallPreview CreatePreview(
        RuntimeProviderDescriptor provider,
        RuntimeArtifactDescriptor artifact,
        EnvironmentIdentity identity,
        EnvironmentFingerprint fingerprint,
        string installRoot,
        string location,
        string executablePath,
        string activationIdentity,
        string stableActivationPath,
        string managedEntryPath,
        string impact,
        bool isAlreadyInstalled,
        string? operationId = null,
        string? recoveryPointId = null)
    {
        return new RuntimeInstallPreview(
            provider,
            artifact,
            identity,
            fingerprint,
            installRoot,
            location,
            executablePath,
            activationIdentity,
            stableActivationPath,
            managedEntryPath,
            impact,
            isAlreadyInstalled,
            operationId,
            recoveryPointId);
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(
            Inspection.WindowsPathNormalizer.NormalizeForComparison(
                Path.GetFullPath(left)),
            Inspection.WindowsPathNormalizer.NormalizeForComparison(
                Path.GetFullPath(right)),
            StringComparison.OrdinalIgnoreCase);
    }
}
