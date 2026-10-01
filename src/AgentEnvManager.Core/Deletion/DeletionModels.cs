using AgentEnvManager.Core.Adoption;

namespace AgentEnvManager.Core.Deletion;

public sealed record EnvironmentDeletionPreview(
    EnvironmentFingerprint Fingerprint,
    EnvironmentManifest Target,
    string OriginalPath,
    string QuarantinePath,
    string PreviousActivationTarget,
    string Impact,
    IReadOnlyList<string> AssociatedState,
    string? OperationId = null,
    string? RecoveryPointId = null);

public sealed record QuarantinedEnvironment(
    string Id,
    EnvironmentManifest OriginalManifest,
    string QuarantinePath,
    DateTimeOffset QuarantinedAtUtc,
    IReadOnlyList<string> AssociatedState,
    string PreviousActivationTarget);

public sealed record EnvironmentRestorePreview(
    string QuarantineId,
    EnvironmentManifest OriginalManifest,
    string OriginalPath,
    string QuarantinePath,
    string Impact,
    IReadOnlyList<string> AssociatedState,
    string? OperationId = null,
    string? RecoveryPointId = null);

public sealed record PermanentDeletePreview(
    string QuarantineId,
    string Name,
    string QuarantinePath,
    string Impact,
    IReadOnlyList<string> AssociatedState,
    string OperationId);

public interface IEnvironmentQuarantineStore
{
    Task<IReadOnlyList<QuarantinedEnvironment>> ReadAllAsync(
        CancellationToken cancellationToken = default);

    Task<QuarantinedEnvironment?> GetAsync(
        string quarantineId,
        CancellationToken cancellationToken = default);

    Task<QuarantinedEnvironment> SaveAsync(
        QuarantinedEnvironment entry,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        string quarantineId,
        CancellationToken cancellationToken = default);

    string CreatePath(string identity);
}

public interface IRuntimeStateCatalog
{
    Task<IReadOnlyList<string>> DescribeAsync(
        EnvironmentManifest manifest,
        CancellationToken cancellationToken = default);
}
