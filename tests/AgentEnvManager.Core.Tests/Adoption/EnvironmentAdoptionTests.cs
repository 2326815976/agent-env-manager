using AgentEnvManager.Core.Adoption;
using AgentEnvManager.Core.Inspection;
using AgentEnvManager.Core.Operations;
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
        var operationJournal = new RecordingOperationJournal();
        var manager = new EnvironmentManager(
            probe,
            manifestStore: store,
            index: index,
            assetHasher: new FixedAssetHasher("asset-hash"),
            recoveryPointStore: recoveryStore,
            operationJournal: operationJournal,
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
        Assert.False(string.IsNullOrWhiteSpace(first.Manifest.OperationId));
        Assert.Single(await store.ReadAllAsync());
        Assert.Single(index.LastRebuiltManifests);
        Assert.Single(recoveryStore.Points);
        Assert.Equal(
            [
                OperationState.Draft,
                OperationState.Validated,
                OperationState.RecoveryReady,
                OperationState.Executing,
                OperationState.Verifying,
                OperationState.Succeeded
            ],
            operationJournal.History.Select(record => record.State));
        var managed = Assert.Single(refreshed.Environments);
        Assert.Equal(ManagementState.Managed, managed.ManagementState);
        Assert.Equal(first.Identity, managed.Identity);
    }

    [Fact]
    public async Task AdoptAsync_rolls_back_manifest_when_index_rebuild_fails()
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
                        Source: DiscoverySourceInfo.PathCommand)
                ],
                []));
        var store = new InMemoryManifestStore();
        var operationJournal = new RecordingOperationJournal();
        var manager = new EnvironmentManager(
            probe,
            manifestStore: store,
            index: new FailOnceIndex(),
            assetHasher: new FixedAssetHasher("asset-hash"),
            recoveryPointStore: new RecordingRecoveryPointStore(),
            operationJournal: operationJournal,
            activationPathFactory: new FixedActivationPathFactory());
        var inventory = await manager.InspectAsync();
        var observed = Assert.Single(inventory.Environments);
        var preview = await manager.PreviewAdoptionAsync(observed.Fingerprint);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => manager.AdoptAsync(preview));

        Assert.Empty(await store.ReadAllAsync());
        Assert.Equal(
            [
                OperationState.Draft,
                OperationState.Validated,
                OperationState.RecoveryReady,
                OperationState.Executing,
                OperationState.Failed,
                OperationState.RolledBack
            ],
            operationJournal.History.Select(record => record.State));
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

        public Task DeleteAsync(
            EnvironmentIdentity identity,
            CancellationToken cancellationToken = default)
        {
            _manifests.RemoveAll(item => item.Identity == identity);
            return Task.CompletedTask;
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
                existingManifest,
                "测试恢复点",
                DateTimeOffset.UnixEpoch);
            Points = [.. Points, point];
            return Task.FromResult(point);
        }

        public Task<AdoptionRecoveryPoint?> GetAsync(
            string recoveryPointId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Points.FirstOrDefault(
                point => point.Id == recoveryPointId));
        }
    }

    private sealed class RecordingOperationJournal : IOperationJournal
    {
        public IReadOnlyList<OperationRecord> History { get; private set; } = [];

        public Task SaveAsync(
            OperationRecord operation,
            CancellationToken cancellationToken = default)
        {
            History = [.. History, operation];
            return Task.CompletedTask;
        }

        public Task<OperationRecord?> GetAsync(
            string operationId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(History.LastOrDefault(
                operation => operation.Id == operationId));
        }

    }

    private sealed class FailOnceIndex : IEnvironmentIndex
    {
        private bool _hasFailed;

        public Task RebuildAsync(
            IReadOnlyList<EnvironmentManifest> manifests,
            CancellationToken cancellationToken = default)
        {
            if (!_hasFailed)
            {
                _hasFailed = true;
                throw new InvalidOperationException("索引重建失败。");
            }

            return Task.CompletedTask;
        }
    }
}
