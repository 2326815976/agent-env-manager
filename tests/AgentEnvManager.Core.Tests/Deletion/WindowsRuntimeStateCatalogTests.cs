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

    [Fact]
    public async Task DescribeAsync_marks_git_config_and_sensitive_paths()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"agent-env-manager-git-state-tests-{Guid.NewGuid():N}");
        var catalog = new WindowsRuntimeStateCatalog(root);

        var state = await catalog.DescribeAsync(CreateGitManifest());

        Assert.Contains(
            state,
            entry => entry.Contains(
                ".gitconfig",
                StringComparison.OrdinalIgnoreCase)
                && entry.Contains(
                    "确认",
                    StringComparison.Ordinal));
        Assert.Contains(
            state,
            entry => entry.Contains(
                ".ssh",
                StringComparison.OrdinalIgnoreCase)
                && entry.Contains(
                    "敏感",
                    StringComparison.Ordinal));
        Assert.Contains(
            state,
            entry => entry.Contains(
                ".git-credentials",
                StringComparison.OrdinalIgnoreCase)
                && entry.Contains(
                    "不读取",
                    StringComparison.Ordinal));
    }

    [Fact]
    public async Task DescribeAsync_reports_powershell_modules_and_profile_separately()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"agent-env-manager-powershell-state-tests-{Guid.NewGuid():N}");
        var identity = "runtime-powershell-7.6.6-win-x64";
        var stateDirectory = Path.Combine(
            root,
            identity,
            "PowerShell");
        var modulesDirectory = Path.Combine(stateDirectory, "Modules");
        var profilePath = Path.Combine(stateDirectory, "profile.ps1");
        var catalog = new WindowsRuntimeStateCatalog(root);

        var state = await catalog.DescribeAsync(
            CreatePowerShellManifest(identity));

        Assert.Contains(
            state,
            entry => entry.Contains(
                "模块目录",
                StringComparison.Ordinal)
                && entry.Contains(
                    modulesDirectory,
                    StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            state,
            entry => entry.Contains(
                "用户配置",
                StringComparison.Ordinal)
                && entry.Contains(
                    profilePath,
                    StringComparison.OrdinalIgnoreCase));
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

    private static EnvironmentManifest CreateGitManifest()
    {
        return new EnvironmentManifest(
            new EnvironmentIdentity("runtime-git-2.56.0-win-x64"),
            new EnvironmentFingerprint("runtime-git-2.56.0-win-x64"),
            EnvironmentAssetKind.ToolRuntime,
            "Git",
            "2.56.0",
            DiscoverySourceInfo.RuntimeProvider,
            @"D:\Runtimes\git\2.56.0",
            @"C:\activations\git\current",
            "asset-hash",
            "recovery",
            "install",
            IsSystemComponent: false,
            DateTimeOffset.UnixEpoch,
            "git-activation",
            @"C:\shims\git");
    }

    private static EnvironmentManifest CreatePowerShellManifest(
        string identity)
    {
        return new EnvironmentManifest(
            new EnvironmentIdentity(identity),
            new EnvironmentFingerprint(identity),
            EnvironmentAssetKind.Shell,
            "PowerShell 7",
            "7.6.6",
            DiscoverySourceInfo.RuntimeProvider,
            @"D:\Runtimes\powershell\7.6.6",
            @"C:\activations\powershell\current",
            "asset-hash",
            "recovery",
            "install",
            IsSystemComponent: false,
            DateTimeOffset.UnixEpoch,
            "powershell-activation",
            @"C:\shims\powershell");
    }
}
