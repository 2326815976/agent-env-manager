using AgentEnvManager.Core.Activation;
using AgentEnvManager.Core.Adoption;
using AgentEnvManager.Core.Agents;
using AgentEnvManager.Core.Deletion;
using AgentEnvManager.Core.Diagnostics;
using AgentEnvManager.Core.EnvironmentVariables;
using AgentEnvManager.Core.Migrations;
using AgentEnvManager.Core.Operations;
using AgentEnvManager.Core.Runtimes;
using AgentEnvManager.Core.Storage;

namespace AgentEnvManager.Core.Inspection;

public sealed class EnvironmentManager
{
    private readonly EnvironmentInspector _inspector;
    private readonly EnvironmentAdopter _adopter;
    private readonly VersionSwitcher _switcher;
    private readonly EnvironmentMigrator _migrator;
    private readonly EnvironmentDeletionService _deletionService;
    private readonly IOperationJournal _operationJournal;
    private readonly IEnvironmentManifestStore _manifestStore;
    private readonly EnvironmentVariableService _environmentVariables;
    private readonly IEnvironmentRecoveryPointStore _recoveryPointStore;
    private readonly IEnvironmentVariableRecoveryPointStore
        _environmentVariableRecoveryPointStore;
    private readonly IReadOnlyDictionary<string, IAgentAdapter>
        _agentAdapters;
    private readonly IReadOnlyDictionary<string, IRuntimeProvider>
        _runtimeProviderRegistry;
    private readonly IReadOnlyList<RuntimeProviderDescriptor>
        _runtimeProviders;
    private readonly RuntimeInstallationService _runtimeInstallation;
    private readonly IRuntimeArtifactCache _artifactCache;
    private readonly string _artifactCacheDirectory;
    private readonly TimeProvider _timeProvider;
    private readonly CondaEnvironmentService _condaEnvironments;
    private readonly IRuntimeStateCatalog _runtimeStateCatalog;
    private readonly IGitConfigurationBackupService
        _gitConfigurationBackup;
    private readonly IDiagnosticPackageService _diagnosticPackage;
    private readonly CcSwitchConfigurationService _ccSwitch;
    private readonly CoordinatedMigrationService _coordinatedMigration;

    public EnvironmentManager(
        IEnvironmentProbe probe,
        TimeProvider? timeProvider = null,
        IEnvironmentManifestStore? manifestStore = null,
        IEnvironmentIndex? index = null,
        IEnvironmentAssetHasher? assetHasher = null,
        IEnvironmentRecoveryPointStore? recoveryPointStore = null,
        IOperationJournal? operationJournal = null,
        IStableActivationPathFactory? activationPathFactory = null,
        IEnvironmentActivationLink? activationLink = null,
        IRuntimeHealthCheck? healthCheck = null,
        IUserEnvironmentVariableStore? userEnvironmentVariableStore = null,
        IMachineEnvironmentVariableReader? machineEnvironmentVariableReader = null,
        IEnvironmentVariableRecoveryPointStore?
            environmentVariableRecoveryPointStore = null,
        ManagerPaths? managerPaths = null,
        IEnumerable<IAgentAdapter>? agentAdapters = null,
        IMigrationOccupancyProbe? migrationOccupancyProbe = null,
        IEnvironmentPathMover? environmentPathMover = null,
        IEnvironmentQuarantineStore? quarantineStore = null,
        IRuntimeStateCatalog? runtimeStateCatalog = null,
        IEnumerable<IRuntimeProvider>? runtimeProviders = null,
        IRuntimeCommandRunner? runtimeCommandRunner = null,
        string? runtimeRoot = null,
        IGitConfigurationBackupService? gitConfigurationBackupService = null,
        IRuntimeArtifactCache? artifactCache = null,
        IDiagnosticPackageService? diagnosticPackageService = null,
        IAgentConfigurationBackupStore? agentConfigurationBackupStore = null,
        Func<string, bool>? isReparsePoint = null,
        CoordinatedMigrationWiring? coordinatedMigration = null)
    {
        var clock = timeProvider ?? TimeProvider.System;
        _timeProvider = clock;
        var store = manifestStore ?? new InMemoryEnvironmentManifestStore();
        var environmentIndex = index ?? new InMemoryEnvironmentIndex();
        var hasher = assetHasher ?? new FileSystemEnvironmentAssetHasher();
        var recoveryStore = recoveryPointStore
            ?? InMemoryEnvironmentRecoveryPointStore.Instance;
        var journal = operationJournal ?? new InMemoryOperationJournal();
        var resolvedManagerPaths = managerPaths ?? ManagerPaths.Resolve();
        var resolvedRuntimeStateRoot =
            resolvedManagerPaths.RuntimeStateDirectory;
        var pathFactory = activationPathFactory
            ?? new DefaultStableActivationPathFactory(
                resolvedManagerPaths.StateRoot);
        var link = activationLink ?? new WindowsJunctionActivationLink();
        var runtimeHealthCheck = healthCheck ?? new ProcessRuntimeHealthCheck();
        var runtimeCommand = runtimeCommandRunner
            ?? new SystemRuntimeCommandRunner();
        _operationJournal = journal;
        _manifestStore = store;
        _agentAdapters = (agentAdapters ?? [])
            .ToDictionary(
                adapter => adapter.Name,
                StringComparer.OrdinalIgnoreCase);
        var resolvedRuntimeProviders = (
            runtimeProviders
                ?? [
                    new PythonRuntimeProvider(),
                    new NodeRuntimeProvider(),
                    new GitRuntimeProvider(),
                    new PowerShellRuntimeProvider(),
                    new UvRuntimeProvider(),
                    new NpmRuntimeProvider(),
                    new PnpmRuntimeProvider(),
                    new RipgrepRuntimeProvider(),
                    new GitHubCliRuntimeProvider()
                ])
            .ToDictionary(
                provider => provider.Descriptor.Id,
                StringComparer.OrdinalIgnoreCase);
        _runtimeProviderRegistry = resolvedRuntimeProviders;
        _runtimeProviders =
        [
            .. resolvedRuntimeProviders.Values.Select(
                provider => provider.Descriptor),
            CondaEnvironmentService.Descriptor
        ];
        var runtimeStateBinder = new ProviderRuntimeStateBinder(
            resolvedRuntimeProviders,
            resolvedRuntimeStateRoot);
        _artifactCache = artifactCache
            ?? new RuntimeArtifactCache(
                resolvedManagerPaths.ArtifactCacheDirectory,
                new HttpArtifactDownloader());
        _artifactCacheDirectory =
            resolvedManagerPaths.ArtifactCacheDirectory;
        _recoveryPointStore = recoveryStore;
        _environmentVariableRecoveryPointStore =
            environmentVariableRecoveryPointStore
                ?? new InMemoryEnvironmentVariableRecoveryPointStore();
        _environmentVariables = new EnvironmentVariableService(
            userEnvironmentVariableStore
                ?? new WindowsUserEnvironmentVariableStore(),
            resolvedManagerPaths.ShimDirectory,
            _environmentVariableRecoveryPointStore,
            store,
            journal,
            machineEnvironmentVariableReader
                ?? new WindowsMachineEnvironmentVariableReader(),
            clock);

        _inspector = new EnvironmentInspector(
            probe,
            store,
            clock);
        _adopter = new EnvironmentAdopter(
            _inspector,
            store,
            environmentIndex,
            hasher,
            recoveryStore,
            journal,
            pathFactory,
            clock);
        _switcher = new VersionSwitcher(
            store,
            recoveryStore,
            journal,
            pathFactory,
            link,
            runtimeHealthCheck,
            runtimeStateBinder,
            clock);
        _runtimeInstallation = new RuntimeInstallationService(
            resolvedRuntimeProviders,
            runtimeCommand,
            store,
            recoveryStore,
            journal,
            pathFactory,
            link,
            environmentIndex,
            _switcher,
            runtimeStateBinder,
            _artifactCache,
            runtimeRoot ?? resolvedManagerPaths.RuntimeDirectory,
            resolvedManagerPaths.DataRoot,
            resolvedRuntimeStateRoot,
            clock);
        _condaEnvironments = new CondaEnvironmentService(runtimeCommand);
        _runtimeStateCatalog = runtimeStateCatalog
            ?? new WindowsRuntimeStateCatalog(resolvedRuntimeStateRoot);
        _gitConfigurationBackup = gitConfigurationBackupService
            ?? new GitConfigurationBackupService(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.UserProfile),
                resolvedManagerPaths.GitConfigurationBackupDirectory,
                journal,
                clock);
        _diagnosticPackage = diagnosticPackageService
            ?? new DiagnosticPackageService(
                store,
                journal,
                recoveryStore,
                _environmentVariableRecoveryPointStore,
                resolvedManagerPaths,
                clock);
        _migrator = new EnvironmentMigrator(
            store,
            recoveryStore,
            journal,
            hasher,
            pathFactory,
            link,
            runtimeHealthCheck,
            migrationOccupancyProbe
                ?? new WindowsMigrationOccupancyProbe(),
            environmentPathMover
                ?? new FileSystemEnvironmentPathMover(),
            clock);
        _deletionService = new EnvironmentDeletionService(
            store,
            recoveryStore,
            journal,
            link,
            hasher,
            environmentPathMover
                ?? new FileSystemEnvironmentPathMover(),
            quarantineStore
                ?? new FileEnvironmentQuarantineStore(
                    resolvedManagerPaths.QuarantineDirectory),
            _runtimeStateCatalog,
            clock);
        _ccSwitch = new CcSwitchConfigurationService(
            agentConfigurationBackupStore
                ?? new FileAgentConfigurationBackupStore(
                    resolvedManagerPaths.AgentBackupDirectory),
            journal,
            clock,
            isReparsePoint: isReparsePoint);
        var migrationWiring = coordinatedMigration
            ?? new CoordinatedMigrationWiring(
                new WindowsProcessControlProbe(),
                new WindowsProcessStartupProbe(),
                new WindowsShortcutEditor(),
                new FileSystemMigrationSourceQuarantine(
                    resolvedManagerPaths.QuarantineDirectory),
                new WindowsCoordinatedTargetsProvider());
        _coordinatedMigration = new CoordinatedMigrationService(
            migrationWiring.ProcessControl,
            userEnvironmentVariableStore
                ?? new WindowsUserEnvironmentVariableStore(),
            activationLink ?? new WindowsJunctionActivationLink(),
            migrationWiring.Startup,
            migrationWiring.Health,
            migrationWiring.ShortcutEditor,
            migrationWiring.SourceQuarantine,
            migrationWiring.TargetsProvider,
            journal,
            clock,
            migrationWiring.EnvironmentFolderPath);
    }

    public Task<CoordinatedMigrationPreview>
        PreviewCoordinatedMigrationAsync(
            CoordinatedMigrationRequest request,
            CancellationToken cancellationToken = default)
    {
        return _coordinatedMigration.PreviewAsync(
            request,
            cancellationToken);
    }

    public Task<CoordinatedMigrationResult> ApplyCoordinatedMigrationAsync(
        CoordinatedMigrationPreview preview,
        CancellationToken cancellationToken = default)
    {
        return _coordinatedMigration.ApplyAsync(preview, cancellationToken);
    }

    public Task<CcSwitchDiscovery> DiscoverCcSwitchAsync(
        CcSwitchDiscoveryRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        return _ccSwitch.DiscoverAsync(request, cancellationToken);
    }

    public Task<CcSwitchBindingPreview> PreviewCcSwitchCodexConfigDirAsync(
        string configRoot,
        string targetCodexConfigDirectory,
        CancellationToken cancellationToken = default)
    {
        return _ccSwitch.PreviewAsync(
            configRoot,
            targetCodexConfigDirectory,
            cancellationToken);
    }

    public Task<CcSwitchBindingResult> ApplyCcSwitchCodexConfigDirAsync(
        CcSwitchBindingPreview preview,
        CancellationToken cancellationToken = default)
    {
        return _ccSwitch.ApplyAsync(preview, cancellationToken);
    }

    public Task<InspectionReport> InspectAsync(
        CancellationToken cancellationToken = default)
    {
        return _inspector.InspectAsync(cancellationToken);
    }

    public IReadOnlyList<RuntimeProviderDescriptor>
        DescribeRuntimeProviders()
    {
        return _runtimeProviders;
    }

    public IReadOnlyList<string> DescribeAgentAdapters()
    {
        return _agentAdapters.Keys
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<IReadOnlyList<ManagedRuntimeStatus>>
        DescribeManagedRuntimesAsync(
            CancellationToken cancellationToken = default)
    {
        var manifests = await _manifestStore.ReadAllAsync(
            cancellationToken);
        return _runtimeProviders
            .Where(provider =>
                provider.Mode == RuntimeProviderMode.Installable)
            .Select(provider =>
            {
                var managed = manifests
                    .Where(manifest => string.Equals(
                        manifest.Name,
                        provider.Name,
                        StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(manifest => manifest.AdoptedAtUtc)
                    .FirstOrDefault();
                return new ManagedRuntimeStatus(
                    provider.Id,
                    provider.Name,
                    managed is not null,
                    managed?.Version,
                    managed?.Location,
                    managed?.ManagedEntryPath,
                    managed?.Identity.Value);
            })
            .ToArray();
    }

    public Task<RuntimeInstallPreview> PreviewRuntimeInstallAsync(
        string providerId,
        string version,
        CancellationToken cancellationToken = default)
    {
        return _runtimeInstallation.PreviewAsync(
            providerId,
            version,
            mirrorUrl: null,
            installRoot: null,
            cancellationToken);
    }

    public Task<RuntimeInstallPreview> PreviewRuntimeInstallAsync(
        string providerId,
        string version,
        string? mirrorUrl,
        CancellationToken cancellationToken = default)
    {
        return _runtimeInstallation.PreviewAsync(
            providerId,
            version,
            mirrorUrl,
            installRoot: null,
            cancellationToken);
    }

    public Task<RuntimeInstallPreview> PreviewRuntimeInstallAsync(
        string providerId,
        string version,
        string? mirrorUrl,
        string? installRoot,
        CancellationToken cancellationToken = default)
    {
        return _runtimeInstallation.PreviewAsync(
            providerId,
            version,
            mirrorUrl,
            installRoot,
            cancellationToken);
    }

    public async Task<RuntimeArtifactCacheEntry> ImportRuntimeArtifactAsync(
        string providerId,
        string version,
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        if (!_runtimeProviderRegistry.TryGetValue(
            providerId,
            out var provider))
        {
            throw new KeyNotFoundException(
                $"未注册运行时提供者: {providerId}");
        }

        var artifact = provider.Descriptor.Artifacts.SingleOrDefault(
            candidate => string.Equals(
                candidate.Version,
                version,
                StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException(
                $"运行时提供者 {providerId} 不支持版本 {version}。");
        var operation = OperationStateMachine.Create(
            OperationType.ArtifactImport,
            $"导入 {provider.Descriptor.Name} {version} 制品",
            _timeProvider,
            target: sourcePath,
            impact: $"校验 {sourcePath} 并写入制品缓存。",
            expectedResult: "制品通过官方 SHA-256 校验并可从缓存复用。");
        await _operationJournal.SaveAsync(operation, cancellationToken);
        operation = await SaveOperationTransitionAsync(
            OperationStateMachine.MarkValidated(operation, _timeProvider),
            cancellationToken);
        var recoveryPointId = $"artifact-import-{operation.Id}";
        operation = await SaveOperationTransitionAsync(
            OperationStateMachine.MarkRecoveryReady(
                operation,
                recoveryPointId,
                _timeProvider),
            cancellationToken);
        try
        {
            operation = await SaveOperationTransitionAsync(
                OperationStateMachine.BeginExecution(
                    operation,
                    _timeProvider),
                cancellationToken);
            var entry = await _artifactCache.ImportAsync(
                artifact,
                sourcePath,
                cancellationToken);
            operation = operation with
            {
                ArtifactSource = entry.Source.ToString(),
                ArtifactCachePath = entry.Path,
                ArtifactSha256 = entry.Sha256,
                VerificationResult = entry.VerificationResult
            };
            operation = await SaveOperationTransitionAsync(
                OperationStateMachine.BeginVerification(
                    operation,
                    _timeProvider),
                cancellationToken);
            if (!File.Exists(entry.Path))
            {
                throw new InvalidOperationException(
                    "导入后的缓存制品不存在。");
            }

            operation = OperationStateMachine.Complete(
                operation,
                _timeProvider);
            await _operationJournal.SaveAsync(
                operation,
                cancellationToken);
            return entry with { OperationId = operation.Id };
        }
        catch (Exception exception)
        {
            operation = OperationStateMachine.Fail(
                operation,
                exception.Message,
                _timeProvider);
            await _operationJournal.SaveAsync(
                operation,
                CancellationToken.None);
            operation = OperationStateMachine.Rollback(
                operation,
                _timeProvider);
            await _operationJournal.SaveAsync(
                operation,
                CancellationToken.None);
            throw;
        }
    }

    private async Task<OperationRecord> SaveOperationTransitionAsync(
        OperationRecord operation,
        CancellationToken cancellationToken)
    {
        await _operationJournal.SaveAsync(operation, cancellationToken);
        return operation;
    }

    public Task<DiagnosticPackagePreview> PreviewDiagnosticPackageAsync(
        CancellationToken cancellationToken = default)
    {
        return _diagnosticPackage.PreviewAsync(cancellationToken);
    }

    public Task<DiagnosticPackageResult> ExportDiagnosticPackageAsync(
        DiagnosticPackagePreview preview,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        return _diagnosticPackage.ExportAsync(
            preview,
            destinationPath,
            cancellationToken);
    }

    public Task<InstalledRuntime> InstallRuntimeAsync(
        RuntimeInstallPreview preview,
        CancellationToken cancellationToken = default)
    {
        return _runtimeInstallation.InstallAsync(
            preview,
            cancellationToken);
    }

    public Task<IReadOnlyList<ObservedCondaEnvironment>>
        InspectCondaEnvironmentsAsync(
            CancellationToken cancellationToken = default)
    {
        return _condaEnvironments.ListAsync(cancellationToken);
    }

    public Task<CondaRebuildPlan> ExportCondaEnvironmentAsync(
        string prefix,
        string definitionPath,
        string? targetPrefix = null,
        CancellationToken cancellationToken = default)
    {
        return _condaEnvironments.ExportAsync(
            prefix,
            definitionPath,
            targetPrefix,
            cancellationToken);
    }

    public async Task<IReadOnlyList<string>> InspectRuntimeStateAsync(
        EnvironmentFingerprint fingerprint,
        CancellationToken cancellationToken = default)
    {
        var manifest = await _manifestStore.FindByFingerprintAsync(
            fingerprint,
            cancellationToken)
            ?? throw new KeyNotFoundException(
                "未找到要查看运行时状态的已纳管环境。");
        return await _runtimeStateCatalog.DescribeAsync(
            manifest,
            cancellationToken);
    }

    public Task<GitConfigurationBackupPreview>
        PreviewGitConfigurationBackupAsync(
            CancellationToken cancellationToken = default)
    {
        return _gitConfigurationBackup.PreviewAsync(cancellationToken);
    }

    public Task<GitConfigurationBackupResult>
        BackupGitConfigurationAsync(
            GitConfigurationBackupPreview preview,
            bool confirmed,
            CancellationToken cancellationToken = default)
    {
        return _gitConfigurationBackup.BackupAsync(
            preview,
            confirmed,
            cancellationToken);
    }

    public Task<AdoptionPreview> PreviewAdoptionAsync(
        EnvironmentFingerprint fingerprint,
        CancellationToken cancellationToken = default)
    {
        return _adopter.PreviewAsync(fingerprint, cancellationToken);
    }

    public Task<ManagedEnvironment> AdoptAsync(
        EnvironmentFingerprint fingerprint,
        CancellationToken cancellationToken = default)
    {
        return _adopter.AdoptAsync(fingerprint, cancellationToken);
    }

    public Task<ManagedEnvironment> AdoptAsync(
        AdoptionPreview preview,
        CancellationToken cancellationToken = default)
    {
        return _adopter.AdoptAsync(preview, cancellationToken);
    }

    public Task<VersionSwitchPreview> PreviewVersionSwitchAsync(
        EnvironmentFingerprint fingerprint,
        CancellationToken cancellationToken = default)
    {
        return _switcher.PreviewAsync(fingerprint, cancellationToken);
    }

    public Task<OperationRecord> SwitchVersionAsync(
        VersionSwitchPreview preview,
        CancellationToken cancellationToken = default)
    {
        return _switcher.SwitchAsync(preview, cancellationToken);
    }

    public Task<MigrationPreview> PreviewMigrationAsync(
        EnvironmentFingerprint fingerprint,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        return _migrator.PreviewAsync(
            fingerprint,
            destinationPath,
            cancellationToken);
    }

    public Task<OperationRecord> MigrateEnvironmentAsync(
        MigrationPreview preview,
        CancellationToken cancellationToken = default)
    {
        return _migrator.MigrateAsync(preview, cancellationToken);
    }

    public Task<EnvironmentDeletionPreview> PreviewEnvironmentDeletionAsync(
        EnvironmentFingerprint fingerprint,
        IReadOnlyList<string>? associatedState = null,
        CancellationToken cancellationToken = default)
    {
        return _deletionService.PreviewAsync(
            fingerprint,
            associatedState,
            cancellationToken);
    }

    public Task<OperationRecord> QuarantineEnvironmentAsync(
        EnvironmentDeletionPreview preview,
        CancellationToken cancellationToken = default)
    {
        return _deletionService.QuarantineAsync(
            preview,
            cancellationToken);
    }

    public Task<IReadOnlyList<QuarantinedEnvironment>>
        ListQuarantinedEnvironmentsAsync(
            CancellationToken cancellationToken = default)
    {
        return _deletionService.ListAsync(cancellationToken);
    }

    public Task<OperationRecord> RestoreQuarantinedEnvironmentAsync(
        string quarantineId,
        CancellationToken cancellationToken = default)
    {
        return _deletionService.RestoreAsync(
            quarantineId,
            cancellationToken);
    }

    public Task<EnvironmentRestorePreview> PreviewQuarantineRestoreAsync(
        string quarantineId,
        CancellationToken cancellationToken = default)
    {
        return _deletionService.PreviewRestoreAsync(
            quarantineId,
            cancellationToken);
    }

    public Task<OperationRecord> RestoreQuarantinedEnvironmentAsync(
        EnvironmentRestorePreview preview,
        CancellationToken cancellationToken = default)
    {
        return _deletionService.RestoreAsync(
            preview,
            cancellationToken);
    }

    public Task<PermanentDeletePreview> PreviewPermanentDeleteAsync(
        string quarantineId,
        CancellationToken cancellationToken = default)
    {
        return _deletionService.PreviewPermanentDeleteAsync(
            quarantineId,
            cancellationToken);
    }

    public Task PermanentDeleteAsync(
        PermanentDeletePreview preview,
        bool confirmed,
        CancellationToken cancellationToken = default)
    {
        return _deletionService.PermanentDeleteAsync(
            preview,
            confirmed,
            cancellationToken);
    }

    public Task<int> RebuildEnvironmentIndexAsync(
        CancellationToken cancellationToken = default)
    {
        return _adopter.RebuildIndexAsync(cancellationToken);
    }

    public Task<OperationRecord> RollbackOperationAsync(
        string operationId,
        CancellationToken cancellationToken = default)
    {
        return RollbackOperationCoreAsync(operationId, cancellationToken);
    }

    public async Task<OperationRollbackPlan> PreviewRollbackAsync(
        string operationId,
        CancellationToken cancellationToken = default)
    {
        var operation = await FindOperationAsync(
            operationId,
            cancellationToken);
        var canRollbackCompletedMigration =
            operation.Type == OperationType.Migrate
            && operation.State == OperationState.Succeeded;
        var canRollbackCompletedUpdate =
            operation.Type == OperationType.EnvironmentVariables
            && operation.State == OperationState.Succeeded;
        if (operation.State != OperationState.Failed
            && !canRollbackCompletedMigration
            && !canRollbackCompletedUpdate)
        {
            throw new InvalidOperationException(
                "只有失败操作、已完成迁移或已完成环境变量事务可以生成回滚计划。");
        }

        if (string.IsNullOrWhiteSpace(operation.RecoveryPointId)
            || string.IsNullOrWhiteSpace(operation.Target)
            || string.IsNullOrWhiteSpace(operation.Impact))
        {
            throw new InvalidOperationException(
                "操作缺少目标、影响范围或恢复点。");
        }

        if (operation.Type == OperationType.EnvironmentVariables)
        {
            var point = await _environmentVariableRecoveryPointStore.GetAsync(
                operation.RecoveryPointId,
                cancellationToken)
                ?? throw new InvalidOperationException("恢复点不存在。");
            return new OperationRollbackPlan(
                operation.Id,
                operation.Target,
                $"恢复 {point.OriginalValues.Count} 个环境变量原值。",
                point.Id,
                "环境变量恢复为操作前值。");
        }

        if (operation.Type == OperationType.Migrate)
        {
            var migrationPoint = await _recoveryPointStore.GetAsync(
                operation.RecoveryPointId,
                cancellationToken)
                ?? throw new InvalidOperationException("恢复点不存在。");
            var original = migrationPoint.PreviousManifest
                ?? throw new InvalidOperationException(
                    "迁移恢复点缺少原环境记录。");
            var originalPath =
                operation.SourceTarget
                ?? migrationPoint.PreviousManifest.Location;
            return new OperationRollbackPlan(
                operation.Id,
                originalPath,
                $"将 {operation.Target} 移回 {originalPath}，并恢复激活目标 {operation.PreviousTarget ?? originalPath}。",
                migrationPoint.Id,
                operation.ExpectedResult
                    ?? "迁移前目录和激活目标恢复完成。");
        }

        if (operation.Type == OperationType.GitConfigurationBackup)
        {
            return new OperationRollbackPlan(
                operation.Id,
                operation.Target
                    ?? throw new InvalidOperationException(
                        "Git 配置备份操作缺少目标。"),
                "删除 Git 配置备份文件。",
                operation.RecoveryPointId,
                "Git 配置备份文件已删除。");
        }

        if (operation.Type == OperationType.ArtifactImport)
        {
            return new OperationRollbackPlan(
                operation.Id,
                operation.ArtifactCachePath
                    ?? throw new InvalidOperationException(
                        "制品导入操作缺少缓存路径。"),
                "删除导入的制品缓存文件。",
                operation.RecoveryPointId,
                "导入制品缓存文件已删除。");
        }

        if (operation.Type == OperationType.DiagnosticExport)
        {
            return new OperationRollbackPlan(
                operation.Id,
                operation.Target
                    ?? throw new InvalidOperationException(
                        "诊断包导出操作缺少目标。"),
                "删除本地诊断包文件。",
                operation.RecoveryPointId,
                "诊断包文件已删除。");
        }

        var recoveryPoint = await _recoveryPointStore.GetAsync(
            operation.RecoveryPointId,
            cancellationToken)
            ?? throw new InvalidOperationException("恢复点不存在。");
        return new OperationRollbackPlan(
            operation.Id,
            operation.Target,
            recoveryPoint.Description,
            recoveryPoint.Id,
            "环境资产恢复为操作前状态。");
    }

    public Task<IReadOnlyList<OperationRecord>> ListOperationsAsync(
        CancellationToken cancellationToken = default)
    {
        return _operationJournal.ReadAllAsync(cancellationToken);
    }

    public Task<IReadOnlyList<AdoptionRecoveryPoint>>
        ListEnvironmentRecoveryPointsAsync(
            CancellationToken cancellationToken = default)
    {
        return _recoveryPointStore.ReadAllAsync(cancellationToken);
    }

    public Task<IReadOnlyList<EnvironmentVariableRecoveryPoint>>
        ListEnvironmentVariableRecoveryPointsAsync(
            CancellationToken cancellationToken = default)
    {
        return _environmentVariableRecoveryPointStore.ReadAllAsync(
            cancellationToken);
    }

    public VersionResolutionResult ResolveRuntimeVersion(
        VersionResolutionRequest request)
    {
        return VersionResolver.Resolve(request);
    }

    public Task<EnvironmentVariableUpdatePreview> PreviewManagedPathUpdateAsync(
        IReadOnlyList<string> managedEntries,
        CancellationToken cancellationToken = default)
    {
        return _environmentVariables.PreviewManagedPathUpdateAsync(
            managedEntries,
            cancellationToken);
    }

    public Task<EnvironmentVariableEditorSnapshot>
        InspectEnvironmentVariableEditorAsync(
            CancellationToken cancellationToken = default)
    {
        return _environmentVariables.InspectAsync(cancellationToken);
    }

    public Task<EnvironmentVariableUpdatePreview> PreviewManagedVariableUpdateAsync(
        IReadOnlyList<EnvironmentVariableChange> changes,
        CancellationToken cancellationToken = default)
    {
        return _environmentVariables.PreviewManagedVariablesAsync(
            changes,
            cancellationToken);
    }

    public Task<EnvironmentVariableUpdatePreview>
        PreviewManagedEnvironmentUpdateAsync(
            IReadOnlyList<string>? managedEntries,
            IReadOnlyList<EnvironmentVariableChange>? variableChanges,
            CancellationToken cancellationToken = default)
    {
        return _environmentVariables.PreviewManagedEnvironmentUpdateAsync(
            managedEntries,
            variableChanges,
            cancellationToken);
    }

    public Task<EnvironmentVariableTransactionResult> ApplyEnvironmentVariableUpdateAsync(
        EnvironmentVariableUpdatePreview preview,
        CancellationToken cancellationToken = default)
    {
        return _environmentVariables.ApplyAsync(
            preview,
            cancellationToken);
    }

    public Task<AgentDiscoveryResult> DiscoverAgentAsync(
        string agentName,
        AgentDiscoveryRequest request,
        CancellationToken cancellationToken = default)
    {
        return GetAgentAdapter(agentName).DiscoverAsync(
            request,
            cancellationToken);
    }

    public Task<AgentBindingPlan> CreateAgentBindingPlanAsync(
        string agentName,
        AgentBindingRequest request,
        CancellationToken cancellationToken = default)
    {
        return GetAgentAdapter(agentName).CreatePlanAsync(
            request,
            cancellationToken);
    }

    public async Task<AgentBinding> BindAgentAsync(
        string agentName,
        AgentBindingPlan plan,
        CancellationToken cancellationToken = default)
    {
        var manifests = await _manifestStore.ReadAllAsync(cancellationToken);
        var managedEntry = Path.GetFullPath(plan.ManagedEntryPath);
        if (!manifests.Any(manifest =>
                !string.IsNullOrWhiteSpace(manifest.ManagedEntryPath)
                && string.Equals(
                    Path.GetFullPath(manifest.ManagedEntryPath),
                    managedEntry,
                    StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                "Agent 绑定只能使用已纳管环境的受管入口。");
        }

        return await new AgentBindingManager(
                GetAgentAdapter(agentName),
                _operationJournal)
            .BindAsync(plan, cancellationToken);
    }

    public Task<AgentHealthCheckResult> CheckAgentHealthAsync(
        string agentName,
        AgentBinding binding,
        CancellationToken cancellationToken = default)
    {
        return GetAgentAdapter(agentName).CheckHealthAsync(
            binding,
            cancellationToken);
    }

    public Task RollbackAgentBindingAsync(
        string agentName,
        AgentBindingPlan plan,
        AgentConfigurationRecoveryPoint recoveryPoint,
        CancellationToken cancellationToken = default)
    {
        return GetAgentAdapter(agentName).RollbackAsync(
            plan,
            recoveryPoint,
            cancellationToken);
    }

    private async Task<OperationRecord> RollbackOperationCoreAsync(
        string operationId,
        CancellationToken cancellationToken)
    {
        var operation = await FindOperationAsync(
            operationId,
            cancellationToken);
        return operation.Type switch
        {
            OperationType.Install =>
                await _runtimeInstallation.RollbackAsync(
                    operationId,
                    cancellationToken),
            OperationType.Switch => await _switcher.RollbackAsync(
                operationId,
                cancellationToken),
            OperationType.Migrate => await _migrator.RollbackAsync(
                operationId,
                cancellationToken),
            OperationType.Delete or OperationType.Purge =>
                await _deletionService.RollbackDeleteAsync(
                    operationId,
                    cancellationToken),
            OperationType.EnvironmentVariables =>
                await _environmentVariables.RollbackAsync(
                    operationId,
                    cancellationToken),
            OperationType.GitConfigurationBackup =>
                await _gitConfigurationBackup.RollbackAsync(
                    operationId,
                    cancellationToken),
            OperationType.ArtifactImport =>
                await RollbackArtifactImportAsync(
                    operationId,
                    cancellationToken),
            OperationType.DiagnosticExport =>
                await _diagnosticPackage.RollbackAsync(
                    operationId,
                    cancellationToken),
            _ => await _adopter.RollbackOperationAsync(
                operationId,
                cancellationToken)
        };
    }

    private async Task<OperationRecord> RollbackArtifactImportAsync(
        string operationId,
        CancellationToken cancellationToken)
    {
        var operation = await FindOperationAsync(
            operationId,
            cancellationToken);
        if (operation.Type != OperationType.ArtifactImport)
        {
            throw new InvalidOperationException(
                "该操作不是制品导入操作。");
        }

        if (operation.State is OperationState.Succeeded
            or OperationState.RolledBack)
        {
            operation = OperationStateMachine.Reject(
                operation,
                OperationState.RolledBack,
                "该操作当前状态不能回滚。",
                _timeProvider);
            await _operationJournal.SaveAsync(
                operation,
                CancellationToken.None);
            throw new InvalidOperationException(
                "该操作当前状态不能回滚。");
        }

        if (!string.IsNullOrWhiteSpace(operation.ArtifactCachePath)
            && File.Exists(operation.ArtifactCachePath))
        {
            var cachePath = Path.GetFullPath(
                operation.ArtifactCachePath);
            var cacheRoot = Path.GetFullPath(_artifactCacheDirectory);
            var allowedRoot = cacheRoot.EndsWith(
                Path.DirectorySeparatorChar)
                ? cacheRoot
                : cacheRoot + Path.DirectorySeparatorChar;
            if (!cachePath.StartsWith(
                allowedRoot,
                StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "制品缓存路径无效。");
            }

            File.Delete(cachePath);
        }

        if (operation.State != OperationState.Failed)
        {
            operation = OperationStateMachine.Fail(
                operation,
                "用户请求回滚制品导入。",
                _timeProvider);
            await _operationJournal.SaveAsync(
                operation,
                cancellationToken);
        }

        operation = OperationStateMachine.Rollback(
            operation,
            _timeProvider);
        await _operationJournal.SaveAsync(
            operation,
            cancellationToken);
        return operation;
    }

    private async Task<OperationRecord> FindOperationAsync(
        string operationId,
        CancellationToken cancellationToken)
    {
        return await _operationJournal.GetAsync(
            operationId,
            cancellationToken)
            ?? throw new KeyNotFoundException("未找到操作记录。");
    }

    private IAgentAdapter GetAgentAdapter(string agentName)
    {
        return _agentAdapters.TryGetValue(agentName, out var adapter)
            ? adapter
            : throw new KeyNotFoundException(
                $"未注册 Agent 适配器: {agentName}");
    }
}
