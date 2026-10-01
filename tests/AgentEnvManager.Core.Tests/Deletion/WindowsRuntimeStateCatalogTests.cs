using AgentEnvManager.Core.Adoption;
using AgentEnvManager.Core.Deletion;
using AgentEnvManager.Core.Inspection;

namespace AgentEnvManager.Core.Tests.Deletion;

public sealed class WindowsRuntimeStateCatalogTests
{
    [Fact]
    public async Task DescribeAsync_reports_node_global_packages_and_cache_separately()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"agent-env-manager-node-state-tests-{Guid.NewGuid():N}");
        var identity = "runtime-node-24.1.0-win-x64";
        var stateRoot = Path.Combine(root, identity);
        var globalPrefix = Path.Combine(stateRoot, "npm-global");
        var nodeModules = Path.Combine(globalPrefix, "node_modules");
        var cacheDirectory = Path.Combine(stateRoot, "npm-cache");
        Directory.CreateDirectory(Path.Combine(nodeModules, "left-pad"));
        Directory.CreateDirectory(
            Path.Combine(nodeModules, "@scope", "pkg"));
        Directory.CreateDirectory(cacheDirectory);
        var catalog = new WindowsRuntimeStateCatalog(root);

        try
        {
            var state = await catalog.DescribeAsync(
                CreateNodeManifest(identity));

            Assert.Collection(
                state,
                entry =>
                {
                    Assert.Contains("全局包", entry);
                    Assert.Contains(globalPrefix, entry);
                    Assert.Contains("left-pad", entry);
                    Assert.Contains("@scope/pkg", entry);
                },
                entry =>
                {
                    Assert.Contains("缓存", entry);
                    Assert.Contains(cacheDirectory, entry);
                });
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static EnvironmentManifest CreateNodeManifest(string identity)
    {
        return new EnvironmentManifest(
            new EnvironmentIdentity(identity),
            new EnvironmentFingerprint(identity),
            EnvironmentAssetKind.ToolRuntime,
            "Node.js",
            "24.1.0",
            DiscoverySourceInfo.RuntimeProvider,
            @"D:\Runtimes\node-24.1.0",
            @"C:\activations\node\current",
            "asset-hash",
            "recovery",
            "install",
            IsSystemComponent: false,
            DateTimeOffset.UnixEpoch,
            "node-activation",
            @"C:\shims\node");
    }
}
