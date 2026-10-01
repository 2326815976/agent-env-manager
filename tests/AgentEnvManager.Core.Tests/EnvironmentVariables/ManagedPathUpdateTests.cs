using AgentEnvManager.Core.Adoption;
using AgentEnvManager.Core.EnvironmentVariables;
using AgentEnvManager.Core.Inspection;
using AgentEnvManager.Core.Storage;
using AgentEnvManager.Core.Tests.TestSupport;

namespace AgentEnvManager.Core.Tests.EnvironmentVariables;

public sealed class ManagedPathUpdateTests
{
    [Fact]
    public async Task InspectEnvironmentVariableEditorAsync_lists_managed_paths_and_variables()
    {
        var managedRoot = ManagerPaths.Resolve().ShimDirectory;
        var nodeEntry = Path.Combine(managedRoot, "node");
        var pythonEntry = Path.Combine(managedRoot, "python");
        var store = new RecordingUserEnvironmentVariableStore(
            new Dictionary<string, string?>
            {
                ["Path"] = $@"C:\Tools;{nodeEntry}",
                ["AGENT_ENV_MANAGER_MODE"] = "project"
            });
        var manager = new EnvironmentManager(
            new StubEnvironmentProbe(
                new EnvironmentProbeResult([], [])),
            assetHasher: new FixedAssetHasher("asset-hash"),
            manifestStore: CreateManifestStore(nodeEntry, pythonEntry),
            userEnvironmentVariableStore: store);

        var snapshot = await manager.InspectEnvironmentVariableEditorAsync();

        var currentNode = Assert.Single(
            snapshot.PathEntries,
            entry => entry.ManagedEntryPath == nodeEntry);
        Assert.True(currentNode.IsEnabled);
        Assert.Contains(
            snapshot.PathEntries,
            entry =>
                entry.ManagedEntryPath == pythonEntry
                && !entry.IsEnabled);
        var variable = Assert.Single(snapshot.Variables);
        Assert.Equal("AGENT_ENV_MANAGER_MODE", variable.Name);
        Assert.Equal("project", variable.Value);
    }

    [Fact]
    public async Task PreviewManagedEnvironmentUpdateAsync_combines_path_and_variables()
    {
        var managedRoot = ManagerPaths.Resolve().ShimDirectory;
        var nodeEntry = Path.Combine(managedRoot, "node");
        var store = new RecordingUserEnvironmentVariableStore(
            new Dictionary<string, string?>
            {
                ["Path"] = @"C:\Tools",
                ["AGENT_ENV_MANAGER_MODE"] = "system"
            });
        var manager = new EnvironmentManager(
            new StubEnvironmentProbe(
                new EnvironmentProbeResult([], [])),
            assetHasher: new FixedAssetHasher("asset-hash"),
            manifestStore: CreateManifestStore(nodeEntry),
            userEnvironmentVariableStore: store);

        var preview = await manager.PreviewManagedEnvironmentUpdateAsync(
            [nodeEntry],
            [new EnvironmentVariableChange(
                "AGENT_ENV_MANAGER_MODE",
                "project")]);

        Assert.Equal(
            $@"C:\Tools;{nodeEntry}",
            preview.DesiredValues["Path"]);
        Assert.Equal(
            "project",
            preview.DesiredValues["AGENT_ENV_MANAGER_MODE"]);
        Assert.Equal(2, preview.Changes.Count);
    }

    [Fact]
    public async Task PreviewManagedPathUpdateAsync_preserves_unknown_entries_and_order()
    {
        var managedRoot = ManagerPaths.Resolve().ShimDirectory;
        var store = new RecordingUserEnvironmentVariableStore(
            new Dictionary<string, string?>
            {
                ["Path"] =
                    $@"C:\Tools;{managedRoot}\old;D:\Other;C:\Tools"
            });
        var manifestStore = CreateManifestStore(
            Path.Combine(managedRoot, "node"),
            Path.Combine(managedRoot, "python"));
        var manager = new EnvironmentManager(
            new StubEnvironmentProbe(
                new EnvironmentProbeResult([], [])),
            assetHasher: new FixedAssetHasher("asset-hash"),
            manifestStore: manifestStore,
            userEnvironmentVariableStore: store);

        var preview = await manager.PreviewManagedPathUpdateAsync(
            [
                Path.Combine(managedRoot, "node"),
                Path.Combine(managedRoot, "python")
            ]);

        Assert.Equal(
            $@"C:\Tools;{managedRoot}\node;{managedRoot}\python;D:\Other;C:\Tools",
            preview.DesiredValues["Path"]);
        Assert.Equal(
            $@"C:\Tools;{managedRoot}\old;D:\Other;C:\Tools",
            preview.OriginalValues["Path"]);
    }

    [Fact]
    public async Task ApplyEnvironmentVariableUpdateAsync_updates_and_broadcasts_path()
    {
        var managedRoot = ManagerPaths.Resolve().ShimDirectory;
        var store = new RecordingUserEnvironmentVariableStore(
            new Dictionary<string, string?>
            {
                ["Path"] = @"C:\Tools"
            });
        var manifestStore = CreateManifestStore(
            Path.Combine(managedRoot, "node"));
        var manager = new EnvironmentManager(
            new StubEnvironmentProbe(
                new EnvironmentProbeResult([], [])),
            assetHasher: new FixedAssetHasher("asset-hash"),
            manifestStore: manifestStore,
            userEnvironmentVariableStore: store);
        var preview = await manager.PreviewManagedPathUpdateAsync(
            [Path.Combine(managedRoot, "node")]);

        await manager.ApplyEnvironmentVariableUpdateAsync(preview);

        Assert.Equal(
            $@"C:\Tools;{managedRoot}\node",
            await store.GetAsync("Path"));
        Assert.Equal(1, store.BroadcastCount);
    }

    [Fact]
    public async Task PreviewManagedPathUpdateAsync_uses_configured_manager_root()
    {
        var stateRoot = Path.Combine(
            Path.GetTempPath(),
            "AgentEnvManager.Tests",
            Guid.NewGuid().ToString("N"));
        var paths = ManagerPaths.Resolve(stateRoot);
        var store = new RecordingUserEnvironmentVariableStore(
            new Dictionary<string, string?>
            {
                ["Path"] = @"C:\Tools"
            });
        var manifestStore = CreateManifestStore(
            Path.Combine(paths.ShimDirectory, "node"));
        var manager = new EnvironmentManager(
            new StubEnvironmentProbe(
                new EnvironmentProbeResult([], [])),
            assetHasher: new FixedAssetHasher("asset-hash"),
            manifestStore: manifestStore,
            userEnvironmentVariableStore: store,
            managerPaths: paths);

        var preview = await manager.PreviewManagedPathUpdateAsync(
            [Path.Combine(paths.ShimDirectory, "node")]);

        Assert.Equal(
            $@"C:\Tools;{paths.ShimDirectory}\node",
            preview.DesiredValues["Path"]);
    }

    [Fact]
    public async Task PreviewManagedPathUpdateAsync_rejects_entry_without_managed_manifest()
    {
        var managedRoot = ManagerPaths.Resolve().ShimDirectory;
        var manager = new EnvironmentManager(
            new StubEnvironmentProbe(
                new EnvironmentProbeResult([], [])),
            assetHasher: new FixedAssetHasher("asset-hash"),
            manifestStore: CreateManifestStore());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => manager.PreviewManagedPathUpdateAsync(
                [Path.Combine(managedRoot, "node")]));

        Assert.Contains("未绑定到已纳管环境", exception.Message);
    }

    [Fact]
    public async Task ApplyEnvironmentVariableUpdateAsync_rejects_path_if_manifest_was_removed()
    {
        var managedRoot = ManagerPaths.Resolve().ShimDirectory;
        var managedEntry = Path.Combine(managedRoot, "node");
        var manifest = CreateManifest(managedEntry);
        var manifestStore = new InMemoryManifestStore();
        await manifestStore.SaveAsync(manifest);
        var store = new RecordingUserEnvironmentVariableStore();
        var manager = new EnvironmentManager(
            new StubEnvironmentProbe(
                new EnvironmentProbeResult([], [])),
            assetHasher: new FixedAssetHasher("asset-hash"),
            manifestStore: manifestStore,
            userEnvironmentVariableStore: store);
        var preview = await manager.PreviewManagedPathUpdateAsync(
            [managedEntry]);
        await manifestStore.DeleteAsync(manifest.Identity);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => manager.ApplyEnvironmentVariableUpdateAsync(preview));

        Assert.Contains("未绑定到已纳管环境", exception.Message);
    }

    private static InMemoryManifestStore CreateManifestStore(
        params string[] managedEntries)
    {
        var store = new InMemoryManifestStore();
        foreach (var managedEntry in managedEntries)
        {
            store.SaveAsync(CreateManifest(managedEntry))
                .GetAwaiter()
                .GetResult();
        }

        return store;
    }

    private static EnvironmentManifest CreateManifest(string managedEntry)
    {
        return new EnvironmentManifest(
            new EnvironmentIdentity(managedEntry),
            new EnvironmentFingerprint(managedEntry),
            EnvironmentAssetKind.ToolRuntime,
            "Node.js",
            "24.1.0",
            DiscoverySourceInfo.PathCommand,
            Path.Combine(managedEntry, "node.exe"),
            Path.Combine(managedEntry, "current"),
            "asset-hash",
            "recovery",
            "operation",
            IsSystemComponent: false,
            DateTimeOffset.UnixEpoch,
            "activation-identity",
            managedEntry);
    }

    private sealed class StubEnvironmentProbe(EnvironmentProbeResult result)
        : IEnvironmentProbe
    {
        public Task<EnvironmentProbeResult> ProbeAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(result);
        }
    }
}
