using AgentEnvManager.Core.Adoption;
using AgentEnvManager.Core.Inspection;
using AgentEnvManager.Core.Storage;
using AgentEnvManager.Core.Tests.TestSupport;

namespace AgentEnvManager.Core.Tests.Storage;

public sealed class FileSystemEnvironmentAssetHasherTests
{
    [Fact]
    public async Task DirectoryHash_changes_when_nested_file_changes()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "AgentEnvManager.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "nested"));
        var nestedFile = Path.Combine(root, "nested", "config.json");
        await File.WriteAllTextAsync(nestedFile, "{\"version\":1}");

        try
        {
            var store = new RecordingManifestStore();
            var manager = new EnvironmentManager(
                new StubEnvironmentProbe(root),
                manifestStore: store,
                index: new NoOpIndex(),
                assetHasher: new FileSystemEnvironmentAssetHasher(),
                activationPathFactory: new FixedActivationPathFactory());

            var firstInventory = await manager.InspectAsync();
            var first = Assert.Single(firstInventory.Environments);
            var firstManifest = await manager.AdoptAsync(first.Fingerprint);

            await File.WriteAllTextAsync(nestedFile, "{\"version\":2}");
            var secondInventory = await manager.InspectAsync();
            var second = Assert.Single(secondInventory.Environments);
            var secondManifest = await manager.AdoptAsync(second.Fingerprint);

            Assert.NotEqual(
                firstManifest.Manifest.AssetHash,
                secondManifest.Manifest.AssetHash);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class StubEnvironmentProbe(string path) : IEnvironmentProbe
    {
        public Task<EnvironmentProbeResult> ProbeAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new EnvironmentProbeResult(
                [
                    new EnvironmentAsset(
                        EnvironmentAssetKind.AgentConfiguration,
                        "Test",
                        Version: null,
                        path,
                        IsSystemComponent: false,
                        DiscoverySourceInfo.AgentConfiguration)
                ],
                []));
        }
    }

    private sealed class RecordingManifestStore : IEnvironmentManifestStore
    {
        private readonly List<EnvironmentManifest> _manifests = [];

        public Task<IReadOnlyList<EnvironmentManifest>> ReadAllAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<EnvironmentManifest>>(_manifests);
        }

        public Task<EnvironmentManifest?> FindByFingerprintAsync(
            EnvironmentFingerprint fingerprint,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_manifests.FirstOrDefault(
                manifest => manifest.Fingerprint == fingerprint));
        }

        public Task<EnvironmentManifest> SaveAsync(
            EnvironmentManifest manifest,
            CancellationToken cancellationToken = default)
        {
            _manifests.RemoveAll(item =>
                item.Identity == manifest.Identity
                || item.Fingerprint == manifest.Fingerprint);
            _manifests.Add(manifest);
            return Task.FromResult(manifest);
        }

        public Task DeleteAsync(
            EnvironmentIdentity identity,
            CancellationToken cancellationToken = default)
        {
            _manifests.RemoveAll(item => item.Identity == identity);
            return Task.CompletedTask;
        }
    }

    private sealed class NoOpIndex : IEnvironmentIndex
    {
        public Task RebuildAsync(
            IReadOnlyList<EnvironmentManifest> manifests,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
