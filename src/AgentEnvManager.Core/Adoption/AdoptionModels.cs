using AgentEnvManager.Core.Activation;
using AgentEnvManager.Core.Inspection;

namespace AgentEnvManager.Core.Adoption;

public sealed record EnvironmentIdentity(string Value);

public sealed record EnvironmentFingerprint(string Value);

public sealed record EnvironmentManifest(
    EnvironmentIdentity Identity,
    EnvironmentFingerprint Fingerprint,
    EnvironmentAssetKind Kind,
    string Name,
    string? Version,
    DiscoverySourceInfo Source,
    string Location,
    string StableActivationPath,
    string AssetHash,
    string RecoveryPointId,
    string OperationId,
    bool IsSystemComponent,
    DateTimeOffset AdoptedAtUtc,
    string ActivationIdentity = "",
    string ManagedEntryPath = "");

public sealed record AdoptionRecoveryPoint(
    string Id,
    string OperationId,
    EnvironmentFingerprint Fingerprint,
    EnvironmentIdentity? PreviousIdentity,
    EnvironmentManifest? PreviousManifest,
    string Description,
    DateTimeOffset CreatedAtUtc);

public sealed record AdoptionPreview(
    EnvironmentFingerprint Fingerprint,
    EnvironmentIdentity ProposedIdentity,
    EnvironmentAsset Asset,
    string AssetHash,
    string StableActivationPath,
    string Impact,
    bool IsAlreadyManaged,
    EnvironmentIdentity? ExistingIdentity,
    string ManagedEntryPath = "",
    string? OperationId = null,
    string? RecoveryPointId = null);

public sealed record ManagedEnvironment(
    EnvironmentIdentity Identity,
    EnvironmentManifest Manifest);

public interface IEnvironmentManifestStore
{
    Task<IReadOnlyList<EnvironmentManifest>> ReadAllAsync(
        CancellationToken cancellationToken = default);

    Task<EnvironmentManifest?> FindByFingerprintAsync(
        EnvironmentFingerprint fingerprint,
        CancellationToken cancellationToken = default);

    Task<EnvironmentManifest> SaveAsync(
        EnvironmentManifest manifest,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        EnvironmentIdentity identity,
        CancellationToken cancellationToken = default);
}

public interface IEnvironmentIndex
{
    Task RebuildAsync(
        IReadOnlyList<EnvironmentManifest> manifests,
        CancellationToken cancellationToken = default);
}

public interface IEnvironmentAssetHasher
{
    Task<string> ComputeHashAsync(
        EnvironmentAsset asset,
        CancellationToken cancellationToken = default);
}

public interface IStableActivationPathFactory
{
    string Create(ActivationKey key);

    string CreateManagedEntry(ActivationKey key);
}

public interface IEnvironmentRecoveryPointStore
{
    Task<IReadOnlyList<AdoptionRecoveryPoint>> ReadAllAsync(
        CancellationToken cancellationToken = default);

    Task<AdoptionRecoveryPoint> CreateAsync(
        AdoptionPreview preview,
        string operationId,
        EnvironmentManifest? existingManifest,
        CancellationToken cancellationToken = default);

    Task<AdoptionRecoveryPoint?> GetAsync(
        string recoveryPointId,
        CancellationToken cancellationToken = default);
}
