namespace AgentEnvManager.Core.Adoption;

internal sealed class InMemoryEnvironmentManifestStore
    : IEnvironmentManifestStore
{
    private readonly List<EnvironmentManifest> _manifests = [];

    public Task<IReadOnlyList<EnvironmentManifest>> ReadAllAsync(
        CancellationToken cancellationToken = default)
    {
        lock (_manifests)
        {
            return Task.FromResult<IReadOnlyList<EnvironmentManifest>>(
                _manifests.ToArray());
        }
    }

    public Task<EnvironmentManifest?> FindByFingerprintAsync(
        EnvironmentFingerprint fingerprint,
        CancellationToken cancellationToken = default)
    {
        lock (_manifests)
        {
            return Task.FromResult(_manifests.FirstOrDefault(
                manifest => manifest.Fingerprint == fingerprint));
        }
    }

    public Task<EnvironmentManifest> SaveAsync(
        EnvironmentManifest manifest,
        CancellationToken cancellationToken = default)
    {
        lock (_manifests)
        {
            var existing = _manifests.FirstOrDefault(item =>
                item.Fingerprint == manifest.Fingerprint);
            if (existing is not null)
            {
                manifest = manifest with
                {
                    Identity = existing.Identity,
                    AdoptedAtUtc = existing.AdoptedAtUtc
                };
            }

            _manifests.RemoveAll(item =>
                item.Identity == manifest.Identity
                || item.Fingerprint == manifest.Fingerprint);
            _manifests.Add(manifest);
            return Task.FromResult(manifest);
        }
    }

    public Task DeleteAsync(
        EnvironmentIdentity identity,
        CancellationToken cancellationToken = default)
    {
        lock (_manifests)
        {
            _manifests.RemoveAll(item => item.Identity == identity);
        }

        return Task.CompletedTask;
    }
}
