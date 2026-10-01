using AgentEnvManager.Core.Activation;
using AgentEnvManager.Core.Adoption;
using AgentEnvManager.Core.Inspection;

namespace AgentEnvManager.Core.Tests.Activation;

public sealed class ProcessRuntimeHealthCheckTests
{
    [Fact]
    public async Task CheckAsync_does_not_fallback_to_system_command()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "AgentEnvManager.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var result = await new ProcessRuntimeHealthCheck().CheckAsync(
                CreateManifest(root, "24.1.0"),
                root);

            Assert.False(result.IsHealthy);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CheckAsync_rejects_output_from_wrong_version()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "AgentEnvManager.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(
                Path.Combine(root, "node.cmd"),
                $"@echo off{Environment.NewLine}echo 22.22.0{Environment.NewLine}");

            var result = await new ProcessRuntimeHealthCheck().CheckAsync(
                CreateManifest(root, "24.1.0"),
                root);

            Assert.False(result.IsHealthy);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CheckAsync_does_not_accept_version_substring_match()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "AgentEnvManager.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(
                Path.Combine(root, "node.cmd"),
                $"@echo off{Environment.NewLine}echo 12.0.0{Environment.NewLine}");

            var result = await new ProcessRuntimeHealthCheck().CheckAsync(
                CreateManifest(root, "2.0.0"),
                root);

            Assert.False(result.IsHealthy);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CheckAsync_rejects_prerelease_suffix_for_stable_version()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "AgentEnvManager.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(
                Path.Combine(root, "node.cmd"),
                $"@echo off{Environment.NewLine}echo 22.22.0-rc.1{Environment.NewLine}");

            var result = await new ProcessRuntimeHealthCheck().CheckAsync(
                CreateManifest(root, "22.22.0"),
                root);

            Assert.False(result.IsHealthy);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CheckAsync_accepts_v_prefix_in_node_version()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "AgentEnvManager.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(
                Path.Combine(root, "node.cmd"),
                $"@echo off{Environment.NewLine}echo v24.1.0{Environment.NewLine}");

            var result = await new ProcessRuntimeHealthCheck().CheckAsync(
                CreateManifest(root, "24.1.0"),
                root);

            Assert.True(result.IsHealthy);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static EnvironmentManifest CreateManifest(
        string location,
        string version)
    {
        return new EnvironmentManifest(
            new EnvironmentIdentity("node"),
            new EnvironmentFingerprint("node-fingerprint"),
            EnvironmentAssetKind.ToolRuntime,
            "Node.js",
            version,
            DiscoverySourceInfo.PathCommand,
            Path.Combine(location, "node.cmd"),
            Path.Combine(location, "current"),
            "asset-hash",
            "recovery",
            "operation",
            IsSystemComponent: false,
            DateTimeOffset.UnixEpoch,
            "node-key",
            Path.Combine(location, "entry"));
    }
}
