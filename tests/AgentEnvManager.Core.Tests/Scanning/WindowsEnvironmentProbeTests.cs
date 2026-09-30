using AgentEnvManager.Core.Inspection;
using AgentEnvManager.Core.Scanning;

namespace AgentEnvManager.Core.Tests.Scanning;

public sealed class WindowsEnvironmentProbeTests
{
    [Fact]
    public async Task InspectAsync_maps_windows_candidates_to_observed_assets()
    {
        var snapshot = new WindowsEnvironmentSnapshot(
            Executables:
            [
                new ExecutableCandidate(
                    EnvironmentAssetKind.ToolRuntime,
                    "Node.js",
                    @"D:\Software\node\node.exe",
                    IsSystemComponent: false,
                    Source: "PATH"),
                new ExecutableCandidate(
                    EnvironmentAssetKind.Shell,
                    "cmd",
                    @"C:\Windows\System32\cmd.exe",
                    IsSystemComponent: true,
                    Source: "系统路径")
            ],
            Directories:
            [
                new DirectoryCandidate(
                    "Codex",
                    @"E:\Codex\.codex",
                    Source: "CODEX_HOME")
            ],
            PathEntries:
            [
                new PathEntry(@"D:\Software\node", PathScope.User)
            ]);
        var manager = new EnvironmentManager(
            new WindowsEnvironmentProbe(new StubSnapshotSource(snapshot)));

        var report = await manager.InspectAsync();

        Assert.Collection(
            report.Environments,
            environment =>
            {
                Assert.Equal("Node.js", environment.Asset.Name);
                Assert.Equal(EnvironmentAssetKind.ToolRuntime, environment.Asset.Kind);
                Assert.Equal(ManagementState.Observed, environment.ManagementState);
            },
            environment =>
            {
                Assert.Equal("cmd", environment.Asset.Name);
                Assert.Equal(EnvironmentAssetKind.Shell, environment.Asset.Kind);
                Assert.True(environment.Asset.IsSystemComponent);
            },
            environment =>
            {
                Assert.Equal("Codex", environment.Asset.Name);
                Assert.Equal(EnvironmentAssetKind.AgentConfiguration, environment.Asset.Kind);
            });
    }

    private sealed class StubSnapshotSource(WindowsEnvironmentSnapshot snapshot)
        : IWindowsEnvironmentSnapshotSource
    {
        public WindowsEnvironmentSnapshot Capture()
        {
            return snapshot;
        }
    }
}
