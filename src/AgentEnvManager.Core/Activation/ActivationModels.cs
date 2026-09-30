using AgentEnvManager.Core.Adoption;

namespace AgentEnvManager.Core.Activation;

public sealed record ActivationKey(EnvironmentIdentity Identity)
{
    public string Value => Identity.Value;
}

public sealed record VersionSwitchPreview(
    EnvironmentFingerprint TargetFingerprint,
    EnvironmentManifest Target,
    EnvironmentManifest? Active,
    string ActivationPath,
    string ManagedEntryPath,
    string Impact,
    bool IsAlreadyActive);

public sealed record RuntimeHealthCheckResult(
    bool IsHealthy,
    string Message);

public interface IEnvironmentActivationLink
{
    Task<string?> GetTargetAsync(
        string activationPath,
        CancellationToken cancellationToken = default);

    Task SetTargetAsync(
        string activationPath,
        string targetPath,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        string activationPath,
        CancellationToken cancellationToken = default);

    Task<bool> TargetExistsAsync(
        string targetPath,
        CancellationToken cancellationToken = default);

    string GetActivationTarget(string location);
}

public interface IRuntimeHealthCheck
{
    Task<RuntimeHealthCheckResult> CheckAsync(
        EnvironmentManifest manifest,
        string managedEntryPath,
        CancellationToken cancellationToken = default);
}
