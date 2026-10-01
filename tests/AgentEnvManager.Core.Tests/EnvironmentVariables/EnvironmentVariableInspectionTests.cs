using AgentEnvManager.Core.Adoption;
using AgentEnvManager.Core.EnvironmentVariables;
using AgentEnvManager.Core.Inspection;
using AgentEnvManager.Core.Storage;
using AgentEnvManager.Core.Tests.TestSupport;

namespace AgentEnvManager.Core.Tests.EnvironmentVariables;

public sealed class EnvironmentVariableInspectionTests
{
    [Fact]
    public async Task InspectEnvironmentVariableEditorAsync_marks_ownership_and_machine_scope()
    {
        var managedRoot = ManagerPaths.Resolve().ShimDirectory;
        var nodeEntry = Path.Combine(managedRoot, "node");
        var store = new RecordingUserEnvironmentVariableStore(
            new Dictionary<string, string?>
            {
                ["Path"] = $@"C:\Tools;{nodeEntry};C:\Other",
                ["AGENT_ENV_MANAGER_MODE"] = "project",
                ["EDITOR"] = "vim",
                ["PATHEXT"] = ".COM;.EXE"
            });
        var machine = new RecordingMachineEnvironmentVariableReader(
            new Dictionary<string, string?>
            {
                ["Path"] = @"C:\Windows;C:\Windows\System32",
                ["PROCESSOR_ARCHITECTURE"] = "AMD64"
            });
        var manager = new EnvironmentManager(
            new StubEnvironmentProbe(
                new EnvironmentProbeResult([], [])),
            assetHasher: new FixedAssetHasher("asset-hash"),
            manifestStore: CreateManifestStore(nodeEntry),
            userEnvironmentVariableStore: store,
            machineEnvironmentVariableReader: machine);

        var snapshot = await manager.InspectEnvironmentVariableEditorAsync();

        var managedVariable = Assert.Single(
            snapshot.Variables,
            variable => variable.Name == "AGENT_ENV_MANAGER_MODE");
        Assert.True(managedVariable.IsManaged);
        var externalVariable = Assert.Single(
            snapshot.Variables,
            variable => variable.Name == "EDITOR");
        Assert.False(externalVariable.IsManaged);
        Assert.Equal("vim", externalVariable.Value);
        Assert.True(Assert.Single(
            snapshot.Variables,
            variable => variable.Name == "PATHEXT").IsHighRisk);

        var managedEntry = Assert.Single(
            snapshot.PathEntries,
            entry => entry.ManagedEntryPath == nodeEntry);
        Assert.True(managedEntry.IsManaged);
        var externalEntry = Assert.Single(
            snapshot.PathEntries,
            entry => entry.ManagedEntryPath == @"C:\Other");
        Assert.False(externalEntry.IsManaged);
        Assert.False(Assert.Single(
            snapshot.PathEntries,
            entry => entry.ManagedEntryPath == @"C:\Tools").IsManaged);

        Assert.Equal(2, snapshot.MachineVariables.Count);
        Assert.All(
            snapshot.MachineVariables,
            variable => Assert.False(variable.IsManaged));
        Assert.Contains(
            "只读",
            snapshot.MachineScopeDescription,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task PreviewManagedEnvironmentUpdateAsync_rejects_external_variables()
    {
        var store = new RecordingUserEnvironmentVariableStore(
            new Dictionary<string, string?>
            {
                ["EDITOR"] = "vim"
            });
        var manager = new EnvironmentManager(
            new StubEnvironmentProbe(
                new EnvironmentProbeResult([], [])),
            userEnvironmentVariableStore: store);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => manager.PreviewManagedVariableUpdateAsync(
                [new EnvironmentVariableChange("EDITOR", "nvim")]));

        Assert.Contains("EDITOR", exception.Message, StringComparison.Ordinal);
    }

    private static InMemoryManifestStore CreateManifestStore(
        string managedEntry)
    {
        var store = new InMemoryManifestStore();
        store.SaveAsync(new EnvironmentManifest(
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
            managedEntry))
            .GetAwaiter()
            .GetResult();
        return store;
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
