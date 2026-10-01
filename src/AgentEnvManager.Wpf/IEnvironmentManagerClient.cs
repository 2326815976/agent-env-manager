using AgentEnvManager.Core.Adoption;
using AgentEnvManager.Core.Activation;
using AgentEnvManager.Core.EnvironmentVariables;
using AgentEnvManager.Core.Deletion;
using AgentEnvManager.Core.Inspection;
using AgentEnvManager.Core.Operations;

namespace AgentEnvManager.Wpf;

public interface IEnvironmentManagerClient
{
    Task<InspectionReport> InspectAsync(
        CancellationToken cancellationToken = default);

    Task<AdoptionPreview> PreviewAdoptionAsync(
        EnvironmentFingerprint fingerprint,
        CancellationToken cancellationToken = default);

    Task<ManagedEnvironment> AdoptAsync(
        AdoptionPreview preview,
        CancellationToken cancellationToken = default);

    Task<VersionSwitchPreview> PreviewVersionSwitchAsync(
        EnvironmentFingerprint fingerprint,
        CancellationToken cancellationToken = default);

    Task<OperationRecord> SwitchVersionAsync(
        VersionSwitchPreview preview,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OperationRecord>> ListOperationsAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AdoptionRecoveryPoint>>
        ListEnvironmentRecoveryPointsAsync(
            CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EnvironmentVariableRecoveryPoint>>
        ListEnvironmentVariableRecoveryPointsAsync(
            CancellationToken cancellationToken = default);

    Task<OperationRecord> RollbackOperationAsync(
        string operationId,
        CancellationToken cancellationToken = default);

    Task<EnvironmentDeletionPreview> PreviewEnvironmentDeletionAsync(
        EnvironmentFingerprint fingerprint,
        IReadOnlyList<string>? associatedState = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    Task<OperationRecord> QuarantineEnvironmentAsync(
        EnvironmentDeletionPreview preview,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    Task<IReadOnlyList<QuarantinedEnvironment>>
        ListQuarantinedEnvironmentsAsync(
            CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<QuarantinedEnvironment>>([]);
    }

    Task<EnvironmentRestorePreview> PreviewQuarantineRestoreAsync(
        string quarantineId,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    Task<OperationRecord> RestoreQuarantinedEnvironmentAsync(
        EnvironmentRestorePreview preview,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    Task<PermanentDeletePreview> PreviewPermanentDeleteAsync(
        string quarantineId,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    Task PermanentDeleteAsync(
        PermanentDeletePreview preview,
        bool confirmed,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    Task<OperationRollbackPlan> PreviewRollbackAsync(
        string operationId,
        CancellationToken cancellationToken = default);
}
