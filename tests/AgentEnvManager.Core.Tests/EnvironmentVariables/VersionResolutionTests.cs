using AgentEnvManager.Core.EnvironmentVariables;
using AgentEnvManager.Core.Inspection;
using AgentEnvManager.Core.Tests.TestSupport;

namespace AgentEnvManager.Core.Tests.EnvironmentVariables;

public sealed class VersionResolutionTests
{
    [Fact]
    public void ResolveRuntimeVersion_prefers_launch_override()
    {
        var manager = CreateManager();
        var request = new VersionResolutionRequest(
            "Node.js",
            LaunchOverride: "24.1.0",
            Project: "22.22.0",
            AgentBinding: "20.18.0",
            UserDefault: "18.20.0",
            System: "16.20.0");

        var result = manager.ResolveRuntimeVersion(request);

        Assert.Equal("24.1.0", result.Version);
        Assert.Equal(VersionResolutionSource.LaunchOverride, result.Source);
        Assert.Contains("单次启动", result.Explanation);
        Assert.Equal(
            [
                VersionResolutionSource.LaunchOverride,
                VersionResolutionSource.Project,
                VersionResolutionSource.AgentBinding,
                VersionResolutionSource.UserDefault,
                VersionResolutionSource.System
            ],
            result.Candidates.Select(candidate => candidate.Source));
    }

    [Fact]
    public void ResolveRuntimeVersion_uses_expected_fallback_order()
    {
        var manager = CreateManager();
        var cases = new[]
        {
            (
                new VersionResolutionRequest(
                    "Node.js",
                    Project: "22.22.0",
                    AgentBinding: "20.18.0",
                    UserDefault: "18.20.0",
                    System: "16.20.0"),
                "22.22.0",
                VersionResolutionSource.Project),
            (
                new VersionResolutionRequest(
                    "Node.js",
                    AgentBinding: "20.18.0",
                    UserDefault: "18.20.0",
                    System: "16.20.0"),
                "20.18.0",
                VersionResolutionSource.AgentBinding),
            (
                new VersionResolutionRequest(
                    "Node.js",
                    UserDefault: "18.20.0",
                    System: "16.20.0"),
                "18.20.0",
                VersionResolutionSource.UserDefault),
            (
                new VersionResolutionRequest(
                    "Node.js",
                    System: "16.20.0"),
                "16.20.0",
                VersionResolutionSource.System)
        };

        foreach (var (request, expectedVersion, expectedSource) in cases)
        {
            var result = manager.ResolveRuntimeVersion(request);

            Assert.Equal(expectedVersion, result.Version);
            Assert.Equal(expectedSource, result.Source);
        }

        var systemFallback = manager.ResolveRuntimeVersion(
            new VersionResolutionRequest(
                "Node.js",
                System: "16.20.0"));
        Assert.Contains("单次启动覆盖 未提供", systemFallback.Explanation);
        Assert.Contains("系统当前值 提供 16.20.0", systemFallback.Explanation);
    }

    private static EnvironmentManager CreateManager()
    {
        return new EnvironmentManager(
            new StubEnvironmentProbe(
                new EnvironmentProbeResult([], [])),
            assetHasher: new FixedAssetHasher("asset-hash"));
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
