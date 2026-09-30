using AgentEnvManager.Core.Adoption;
using AgentEnvManager.Core.Activation;
using AgentEnvManager.Core.EnvironmentVariables;
using AgentEnvManager.Core.Inspection;
using AgentEnvManager.Core.Operations;

namespace AgentEnvManager.Wpf;

public sealed class EnvironmentManagerClient(EnvironmentManager manager)
    : IEnvironmentManagerClient
{
    public Task<InspectionReport> InspectAsync(
        CancellationToken cancellationToken = default)
    {
        return manager.InspectAsync(cancellationToken);
    }

    public Task<AdoptionPreview> PreviewAdoptionAsync(
        EnvironmentFingerprint fingerprint,
        CancellationToken cancellationToken = default)
    {
        return manager.PreviewAdoptionAsync(fingerprint, cancellationToken);
    }

    public Task<ManagedEnvironment> AdoptAsync(
        AdoptionPreview preview,
        CancellationToken cancellationToken = default)
    {
        return manager.AdoptAsync(preview, cancellationToken);
    }

    public Task<VersionSwitchPreview> PreviewVersionSwitchAsync(
        EnvironmentFingerprint fingerprint,
        CancellationToken cancellationToken = default)
    {
        return manager.PreviewVersionSwitchAsync(
            fingerprint,
            cancellationToken);
    }

    public Task<OperationRecord> SwitchVersionAsync(
        VersionSwitchPreview preview,
        CancellationToken cancellationToken = default)
    {
        return manager.SwitchVersionAsync(preview, cancellationToken);
    }

    public Task<IReadOnlyList<OperationRecord>> ListOperationsAsync(
        CancellationToken cancellationToken = default)
    {
        return manager.ListOperationsAsync(cancellationToken);
    }

    public Task<IReadOnlyList<AdoptionRecoveryPoint>>
        ListEnvironmentRecoveryPointsAsync(
            CancellationToken cancellationToken = default)
    {
        return manager.ListEnvironmentRecoveryPointsAsync(cancellationToken);
    }

    public Task<IReadOnlyList<EnvironmentVariableRecoveryPoint>>
        ListEnvironmentVariableRecoveryPointsAsync(
            CancellationToken cancellationToken = default)
    {
        return manager.ListEnvironmentVariableRecoveryPointsAsync(
            cancellationToken);
    }

    public Task<OperationRecord> RollbackOperationAsync(
        string operationId,
        CancellationToken cancellationToken = default)
    {
        return manager.RollbackOperationAsync(operationId, cancellationToken);
    }
}
