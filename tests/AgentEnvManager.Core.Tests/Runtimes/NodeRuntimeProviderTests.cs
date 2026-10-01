using AgentEnvManager.Core.Inspection;
using AgentEnvManager.Core.Runtimes;
using AgentEnvManager.Core.Storage;
using AgentEnvManager.Core.Tests.TestSupport;
using System.Text;

namespace AgentEnvManager.Core.Tests.Runtimes;

public sealed class NodeRuntimeProviderTests
{
    [Fact]
    public void DescribeRuntimeProviders_declares_node_official_archive()
    {
        var manager = new EnvironmentManager(
            new StubEnvironmentProbe(
                new EnvironmentProbeResult([], [])));

        var node = Assert.Single(
            manager.DescribeRuntimeProviders(),
            provider => provider.Id == "node");

        Assert.Equal("Node.js", node.Name);
        Assert.Equal(RuntimeProviderMode.Installable, node.Mode);
        Assert.Equal(DiscoverySourceInfo.RuntimeProvider, node.Source);
        Assert.Equal("https://nodejs.org/dist/v24.1.0/", node.OfficialSource);
        Assert.Equal("MIT", node.License);
        Assert.Equal(RuntimeInstallStrategy.OfficialArchive, node.InstallStrategy);
        var artifact = Assert.Single(node.Artifacts);
        Assert.Equal("24.1.0", artifact.Version);
        Assert.Equal(
            "81d6774f5c1581c7ddd32fb25cf6138f68755dfbb245025d05a249aafa35ea9d",
            artifact.Sha256);
        Assert.Contains("win-x64", artifact.SupportedArchitectures);
        Assert.Equal(
            "https://nodejs.org/dist/v24.1.0/node-v24.1.0-win-x64.zip",
            artifact.DownloadUrl);
    }

    [Fact]
    public void CreateInstallCommand_binds_npm_state_to_managed_version()
    {
        var provider = new NodeRuntimeProvider();
        var artifact = Assert.Single(provider.Descriptor.Artifacts);
        var runtimeStateDirectory = Path.Combine(
            Path.GetTempPath(),
            "agent-env-manager-node-state",
            "runtime-node-24.1.0-win-x64");
        var context = new RuntimeInstallContext(
            artifact,
            Path.Combine(
                Path.GetTempPath(),
                "agent-env-manager-node-runtime",
                "24.1.0"),
            runtimeStateDirectory);

        var command = provider.CreateInstallCommand(context);

        Assert.Equal("powershell.exe", command.Executable);
        var encodedIndex = Array.IndexOf(
            command.Arguments.ToArray(),
            "-EncodedCommand");
        Assert.True(encodedIndex >= 0);
        var script = Encoding.Unicode.GetString(
            Convert.FromBase64String(
                command.Arguments[encodedIndex + 1]));
        Assert.Contains(artifact.DownloadUrl, script);
        Assert.Contains(artifact.Sha256, script);
        Assert.Contains(
            Path.Combine(runtimeStateDirectory, "npm-global")
                .Replace('\\', '/'),
            script);
        Assert.Contains(
            Path.Combine(runtimeStateDirectory, "npm-cache")
                .Replace('\\', '/'),
            script);
        Assert.Contains(
            "node-v24.1.0-win-x64/node_modules/npm/npmrc",
            script.Replace('\\', '/'));
    }

    [Fact]
    public async Task InstallRuntimeAsync_activates_node_through_managed_entry()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"agent-env-manager-node-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var link = new RecordingActivationLink();
            var healthCheck = new RecordingRuntimeHealthCheck(
                isHealthy: true);
            var runner = new RecordingRuntimeCommandRunner(invocation =>
            {
                var executable = Path.Combine(
                    invocation.WorkingDirectory,
                    "node-v24.1.0-win-x64",
                    "node.exe");
                Directory.CreateDirectory(
                    Path.GetDirectoryName(executable)!);
                File.WriteAllText(executable, string.Empty);
                return new RuntimeCommandResult(
                    0,
                    "installed",
                    string.Empty);
            });
            var manager = new EnvironmentManager(
                new StubEnvironmentProbe(
                    new EnvironmentProbeResult([], [])),
                manifestStore: new InMemoryManifestStore(),
                recoveryPointStore: new RecordingRecoveryPointStore(),
                operationJournal: new RecordingOperationJournal(),
                activationLink: link,
                healthCheck: healthCheck,
                managerPaths: ManagerPaths.Resolve(root),
                runtimeRoot: Path.Combine(root, "runtimes"),
                runtimeProviders: [new NodeRuntimeProvider()],
                runtimeCommandRunner: runner);

            var preview = await manager.PreviewRuntimeInstallAsync(
                "node",
                "24.1.0");
            var installed = await manager.InstallRuntimeAsync(preview);

            Assert.Equal("Node.js", installed.Manifest.Name);
            Assert.Equal(preview.Location, installed.Manifest.Location);
            Assert.Equal(
                preview.Location,
                await link.GetTargetAsync(
                    preview.StableActivationPath));
            Assert.Equal(
                preview.StableActivationPath,
                await link.GetTargetAsync(preview.ManagedEntryPath));
            Assert.Equal(
                preview.ManagedEntryPath,
                healthCheck.LastManagedEntryPath);
            Assert.True(File.Exists(installed.ExecutablePath));

            var versionStateRoot = Path.Combine(
                root,
                "data",
                "runtime-state",
                installed.Manifest.Identity.Value);
            Directory.CreateDirectory(Path.Combine(
                versionStateRoot,
                "npm-global",
                "node_modules",
                "left-pad"));
            Directory.CreateDirectory(Path.Combine(
                versionStateRoot,
                "npm-cache"));
            var state = await manager.InspectRuntimeStateAsync(
                preview.Fingerprint);

            Assert.Contains(
                state,
                entry => entry.Contains(
                    "全局包",
                    StringComparison.Ordinal)
                    && entry.Contains(
                        "left-pad",
                        StringComparison.Ordinal));
            Assert.Contains(
                state,
                entry => entry.Contains(
                    "缓存",
                    StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
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
