using AgentEnvManager.Core.Activation;
using AgentEnvManager.Core.Adoption;
using AgentEnvManager.Core.Agents;
using AgentEnvManager.Core.EnvironmentVariables;
using AgentEnvManager.Core.Migrations;
using AgentEnvManager.Core.Operations;
using AgentEnvManager.Core.Storage;

namespace AgentEnvManager.Core.Inspection;

public sealed class EnvironmentManager
{
    private readonly EnvironmentInspector _inspector;
    private readonly EnvironmentAdopter _adopter;
    private readonly VersionSwitcher _switcher;
    private readonly EnvironmentMigrator _migrator;
    private readonly IOperationJournal _operationJournal;
    private readonly IEnvironmentManifestStore _manifestStore;
    private readonly EnvironmentVariableService _environmentVariables;
    private readonly IEnvironmentRecoveryPointStore _recoveryPointStore;
    private readonly IEnvironmentVariableRecoveryPointStore
        _environmentVariableRecoveryPointStore;
    private readonly IReadOnlyDictionary<string, IAgentAdapter>
        _agentAdapters;

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
        IEnvironmentVariableRecoveryPointStore?
            environmentVariableRecoveryPointStore = null,
        ManagerPaths? managerPaths = null,
        IEnumerable<IAgentAdapter>? agentAdapters = null,
        IMigrationOccupancyProbe? migrationOccupancyProbe = null,
        IEnvironmentPathMover? environmentPathMover = null)
    {
        var clock = timeProvider ?? TimeProvider.System;
        var store = manifestStore ?? new InMemoryEnvironmentManifestStore();
        var environmentIndex = index ?? new InMemoryEnvironmentIndex();
        var hasher = assetHasher ?? new FileSystemEnvironmentAssetHasher();
        var recoveryStore = recoveryPointStore
            ?? InMemoryEnvironmentRecoveryPointStore.Instance;
        var journal = operationJournal ?? new InMemoryOperationJournal();
        var resolvedManagerPaths = managerPaths ?? ManagerPaths.Resolve();
        var pathFactory = activationPathFactory
            ?? new DefaultStableActivationPathFactory(
                resolvedManagerPaths.StateRoot);
        var link = activationLink ?? new WindowsJunctionActivationLink();
        var runtimeHealthCheck = healthCheck ?? new ProcessRuntimeHealthCheck();
        _operationJournal = journal;
        _manifestStore = store;
        _agentAdapters = (agentAdapters ?? [])
            .ToDictionary(
                adapter => adapter.Name,
                StringComparer.OrdinalIgnoreCase);
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
    }

    public Task<InspectionReport> InspectAsync(
        CancellationToken cancellationToken = default)
    {
        return _inspector.InspectAsync(cancellationToken);
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
        if (operation.State != OperationState.Failed)
        {
            throw new InvalidOperationException(
                "只有失败操作可以生成回滚计划。");
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

    public Task<EnvironmentVariableUpdatePreview> PreviewManagedVariableUpdateAsync(
        IReadOnlyList<EnvironmentVariableChange> changes,
        CancellationToken cancellationToken = default)
    {
        return _environmentVariables.PreviewManagedVariablesAsync(
            changes,
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
            OperationType.Switch => await _switcher.RollbackAsync(
                operationId,
                cancellationToken),
            OperationType.Migrate => await _migrator.RollbackAsync(
                operationId,
                cancellationToken),
            OperationType.EnvironmentVariables =>
                await _environmentVariables.RollbackAsync(
                    operationId,
                    cancellationToken),
            _ => await _adopter.RollbackOperationAsync(
                operationId,
                cancellationToken)
        };
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
