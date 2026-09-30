using AgentEnvManager.Core.Inspection;
using AgentEnvManager.Core.Tests.TestSupport;

namespace AgentEnvManager.Core.Tests.Inspection;

public sealed class EnvironmentManagerTests
{
    [Fact]
    public async Task InspectAsync_returns_observed_assets_from_probe()
    {
        var asset = new EnvironmentAsset(
            EnvironmentAssetKind.Shell,
            "cmd",
            "Windows",
            @"C:\Windows\System32\cmd.exe",
            IsSystemComponent: true,
            Source: DiscoverySourceInfo.SystemPath);
        var probe = new StubEnvironmentProbe(
            new EnvironmentProbeResult([asset], []));
        var clock = new FixedTimeProvider(
            new DateTimeOffset(2026, 9, 30, 4, 0, 0, TimeSpan.Zero));
        var manager = new EnvironmentManager(
            probe,
            clock,
            assetHasher: new FixedAssetHasher("asset-hash"));

        var report = await manager.InspectAsync();

        var observed = Assert.Single(report.Environments);
        Assert.Equal(asset, observed.Asset);
        Assert.Equal(ManagementState.Observed, observed.ManagementState);
        Assert.Equal(HealthState.Unknown, observed.HealthState);
        Assert.True(observed.Asset.IsSystemComponent);
        Assert.Equal(clock.GetUtcNow(), report.GeneratedAtUtc);
    }

    [Fact]
    public async Task InspectAsync_reports_duplicate_path_entries_case_insensitively()
    {
        var probe = new StubEnvironmentProbe(
            new EnvironmentProbeResult(
                [],
                [
                    new PathEntry(@"C:\Tools\Bin\", PathScope.User),
                    new PathEntry(@"c:\tools\bin", PathScope.Machine),
                    new PathEntry("C:/Tools/Bin", PathScope.Process),
                    new PathEntry(@"C:\Unique", PathScope.User)
                ]));
        var manager = new EnvironmentManager(
            probe,
            assetHasher: new FixedAssetHasher("asset-hash"));

        var report = await manager.InspectAsync();

        var conflict = Assert.Single(report.PathConflicts);
        Assert.Equal(@"C:\Tools\Bin", conflict.Path);
        Assert.Equal(3, conflict.Occurrences);
        Assert.Equal(
            [PathScope.User, PathScope.Machine, PathScope.Process],
            conflict.Scopes);
    }

    [Fact]
    public async Task InspectAsync_reports_path_commands_shadowed_by_multiple_locations()
    {
        var probe = new StubEnvironmentProbe(
            new EnvironmentProbeResult(
                [
                    new EnvironmentAsset(
                        EnvironmentAssetKind.ToolRuntime,
                        "Node.js",
                        "22.22.0",
                        @"D:\First\node.exe",
                        IsSystemComponent: false,
                        Source: DiscoverySourceInfo.PathCommand,
                        ResolutionOrder: 0),
                    new EnvironmentAsset(
                        EnvironmentAssetKind.ToolRuntime,
                        "Node.js",
                        "20.18.0",
                        @"E:\Second\node.exe",
                        IsSystemComponent: false,
                        Source: DiscoverySourceInfo.PathCommand,
                        ResolutionOrder: 1)
                ],
                []));
        var manager = new EnvironmentManager(
            probe,
            assetHasher: new FixedAssetHasher("asset-hash"));

        var report = await manager.InspectAsync();

        var conflict = Assert.Single(report.CommandPathConflicts);
        Assert.Equal("Node.js", conflict.Name);
        Assert.Collection(
            conflict.Candidates,
            candidate =>
            {
                Assert.Equal(@"D:\First\node.exe", candidate.Path);
                Assert.True(candidate.Effective);
                Assert.Equal(0, candidate.Order);
            },
            candidate =>
            {
                Assert.Equal(@"E:\Second\node.exe", candidate.Path);
                Assert.False(candidate.Effective);
                Assert.Equal(1, candidate.Order);
            });
    }

    [Fact]
    public async Task InspectAsync_preserves_drive_root_when_reporting_duplicate_paths()
    {
        var probe = new StubEnvironmentProbe(
            new EnvironmentProbeResult(
                [],
                [
                    new PathEntry(@"C:\", PathScope.User),
                    new PathEntry("C:/", PathScope.Machine)
                ]));
        var manager = new EnvironmentManager(
            probe,
            assetHasher: new FixedAssetHasher("asset-hash"));

        var report = await manager.InspectAsync();

        var conflict = Assert.Single(report.PathConflicts);
        Assert.Equal(@"C:\", conflict.Path);
        Assert.Equal(2, conflict.Occurrences);
    }

    private sealed class StubEnvironmentProbe(EnvironmentProbeResult result) : IEnvironmentProbe
    {
        public Task<EnvironmentProbeResult> ProbeAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(result);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return now;
        }
    }
}
