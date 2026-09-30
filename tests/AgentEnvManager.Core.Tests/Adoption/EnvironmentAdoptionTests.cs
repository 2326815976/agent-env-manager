using AgentEnvManager.Core.Adoption;
using AgentEnvManager.Core.Inspection;
using AgentEnvManager.Core.Tests.TestSupport;

namespace AgentEnvManager.Core.Tests.Adoption;

public sealed class EnvironmentAdoptionTests
{
    [Fact]
    public async Task AdoptAsync_persists_manifest_and_reuses_identity_for_same_fingerprint()
    {
        var probe = new StubEnvironmentProbe(
            new EnvironmentProbeResult(
                [
                    new EnvironmentAsset(
                        EnvironmentAssetKind.ToolRuntime,
                        "Node.js",
                        "22.22.0",
                        @"D:\Software\node",
                        IsSystemComponent: false,
                        Source: DiscoverySourceInfo.PathCommand,
                        ResolutionOrder: 0)
                ],
                []));
        var store = new InMemoryManifestStore();
        var index = new RecordingIndex();
        var recoveryStore = new RecordingRecoveryPointStore();
        var manager = new EnvironmentManager(
            probe,
            manifestStore: store,
            index: index,
            assetHasher: new FixedAssetHasher("asset-hash"),
            recoveryPointStore: recoveryStore,
            activationPathFactory: new FixedActivationPathFactory());
        var inventory = await manager.InspectAsync();
        var observed = Assert.Single(inventory.Environments);

        var preview = await manager.PreviewAdoptionAsync(observed.Fingerprint);
        var first = await manager.AdoptAsync(observed.Fingerprint);
        var second = await manager.AdoptAsync(observed.Fingerprint);
        var refreshed = await manager.InspectAsync();

        Assert.Equal(observed.Asset, preview.Asset);
        Assert.False(preview.IsAlreadyManaged);
        Assert.Equal(observed.Fingerprint.Value, first.Identity.Value);
        Assert.Equal(first.Identity, second.Identity);
        Assert.StartsWith(
            @"%LOCALAPPDATA%\AgentEnvManager\activations\",
            first.Manifest.StableActivationPath);
        Assert.EndsWith(@"\current", first.Manifest.StableActivationPath);
        Assert.Equal("22.22.0", first.Manifest.Version);
        Assert.Equal("asset-hash", first.Manifest.AssetHash);
        Assert.False(string.IsNullOrWhiteSpace(first.Manifest.RecoveryPointId));
        Assert.Single(await store.ReadAllAsync());
        Assert.Single(index.LastRebuiltManifests);
        Assert.Single(recoveryStore.Points);
        var managed = Assert.Single(refreshed.Environments);
        Assert.Equal(ManagementState.Managed, managed.ManagementState);
        Assert.Equal(first.Identity, managed.Identity);
    }

    private sealed class StubEnvironmentProbe(EnvironmentProbeResult result) : IEnvironmentProbe
    {
        public Task<EnvironmentProbeResult> ProbeAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(result);
        }
    }

    private sealed class InMemoryManifestStore : IEnvironmentManifestStore
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
            return Task.FromResult(_manifests.SingleOrDefault(
                manifest => manifest.Fingerprint == fingerprint));
        }

        public Task<EnvironmentManifest> SaveAsync(
            EnvironmentManifest manifest,
            CancellationToken cancellationToken = default)
        {
            _manifests.RemoveAll(item => item.Identity == manifest.Identity);
            _manifests.Add(manifest);
            return Task.FromResult(manifest);
        }
    }

    private sealed class RecordingIndex : IEnvironmentIndex
    {
        public IReadOnlyList<EnvironmentManifest> LastRebuiltManifests { get; private set; } = [];

        public Task RebuildAsync(
            IReadOnlyList<EnvironmentManifest> manifests,
            CancellationToken cancellationToken = default)
        {
            LastRebuiltManifests = manifests;
            return Task.CompletedTask;
        }

    }

    private sealed class RecordingRecoveryPointStore : IEnvironmentRecoveryPointStore
    {
        public IReadOnlyList<AdoptionRecoveryPoint> Points { get; private set; } = [];

        public Task<AdoptionRecoveryPoint> CreateAsync(
            AdoptionPreview preview,
            EnvironmentManifest? existingManifest,
            CancellationToken cancellationToken = default)
        {
            var point = new AdoptionRecoveryPoint(
                $"recovery-{Points.Count + 1}",
                preview.Fingerprint,
                existingManifest?.Identity,
                "测试恢复点",
                DateTimeOffset.UnixEpoch);
            Points = [.. Points, point];
            return Task.FromResult(point);
        }
    }
}
