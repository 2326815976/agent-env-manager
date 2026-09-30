using AgentEnvManager.Core.Inspection;

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
            Source: "系统路径");
        var probe = new StubEnvironmentProbe(
            new EnvironmentProbeResult([asset], []));
        var clock = new FixedTimeProvider(
            new DateTimeOffset(2026, 9, 30, 4, 0, 0, TimeSpan.Zero));
        var manager = new EnvironmentManager(probe, clock);

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
        var manager = new EnvironmentManager(probe);

        var report = await manager.InspectAsync();

        var conflict = Assert.Single(report.PathConflicts);
        Assert.Equal(@"C:\Tools\Bin", conflict.Path);
        Assert.Equal(3, conflict.Occurrences);
        Assert.Equal(
            [PathScope.User, PathScope.Machine, PathScope.Process],
            conflict.Scopes);
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
