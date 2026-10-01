using AgentEnvManager.Core.Adoption;
using AgentEnvManager.Core.Activation;
using AgentEnvManager.Core.EnvironmentVariables;
using AgentEnvManager.Core.Deletion;
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

    public Task<OperationRollbackPlan> PreviewRollbackAsync(
        string operationId,
        CancellationToken cancellationToken = default)
    {
        return manager.PreviewRollbackAsync(operationId, cancellationToken);
    }

    public Task<EnvironmentDeletionPreview> PreviewEnvironmentDeletionAsync(
        EnvironmentFingerprint fingerprint,
        IReadOnlyList<string>? associatedState = null,
        CancellationToken cancellationToken = default)
    {
        return manager.PreviewEnvironmentDeletionAsync(
            fingerprint,
            associatedState,
            cancellationToken);
    }

    public Task<OperationRecord> QuarantineEnvironmentAsync(
        EnvironmentDeletionPreview preview,
        CancellationToken cancellationToken = default)
    {
        return manager.QuarantineEnvironmentAsync(
            preview,
            cancellationToken);
    }

    public Task<IReadOnlyList<QuarantinedEnvironment>>
        ListQuarantinedEnvironmentsAsync(
            CancellationToken cancellationToken = default)
    {
        return manager.ListQuarantinedEnvironmentsAsync(cancellationToken);
    }

    public Task<EnvironmentRestorePreview> PreviewQuarantineRestoreAsync(
        string quarantineId,
        CancellationToken cancellationToken = default)
    {
        return manager.PreviewQuarantineRestoreAsync(
            quarantineId,
            cancellationToken);
    }

    public Task<OperationRecord> RestoreQuarantinedEnvironmentAsync(
        EnvironmentRestorePreview preview,
        CancellationToken cancellationToken = default)
    {
        return manager.RestoreQuarantinedEnvironmentAsync(
            preview,
            cancellationToken);
    }

    public Task<PermanentDeletePreview> PreviewPermanentDeleteAsync(
        string quarantineId,
        CancellationToken cancellationToken = default)
    {
        return manager.PreviewPermanentDeleteAsync(
            quarantineId,
            cancellationToken);
    }

    public Task PermanentDeleteAsync(
        PermanentDeletePreview preview,
        bool confirmed,
        CancellationToken cancellationToken = default)
    {
        return manager.PermanentDeleteAsync(
            preview,
            confirmed,
            cancellationToken);
    }
}
