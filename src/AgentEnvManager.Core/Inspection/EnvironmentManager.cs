using AgentEnvManager.Core.Activation;
using AgentEnvManager.Core.Adoption;
using AgentEnvManager.Core.EnvironmentVariables;
using AgentEnvManager.Core.Operations;
using AgentEnvManager.Core.Storage;

namespace AgentEnvManager.Core.Inspection;

public sealed class EnvironmentManager
{
    private readonly EnvironmentInspector _inspector;
    private readonly EnvironmentAdopter _adopter;
    private readonly VersionSwitcher _switcher;
    private readonly IOperationJournal _operationJournal;
    private readonly EnvironmentVariableService _environmentVariables;
    private readonly IEnvironmentRecoveryPointStore _recoveryPointStore;
    private readonly IEnvironmentVariableRecoveryPointStore
        _environmentVariableRecoveryPointStore;

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
        ManagerPaths? managerPaths = null)
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
}
