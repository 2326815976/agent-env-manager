namespace AgentEnvManager.Core.Adoption;

internal sealed class EmptyManifestStore : IEnvironmentManifestStore
{
    public static EmptyManifestStore Instance { get; } = new();

    public Task<IReadOnlyList<EnvironmentManifest>> ReadAllAsync(
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<EnvironmentManifest>>([]);
    }

    public Task<EnvironmentManifest?> FindByFingerprintAsync(
        EnvironmentFingerprint fingerprint,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<EnvironmentManifest?>(null);
    }

    public Task<EnvironmentManifest> SaveAsync(
        EnvironmentManifest manifest,
        CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException("尚未配置环境 manifest 存储。");
    }
}
