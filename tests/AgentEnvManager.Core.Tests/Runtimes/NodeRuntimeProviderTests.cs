using AgentEnvManager.Core.Inspection;
using AgentEnvManager.Core.Runtimes;
using AgentEnvManager.Core.Storage;
using AgentEnvManager.Core.Tests.TestSupport;
using AgentEnvManager.Core.Adoption;
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
    public void CreateInstallCommand_verifies_official_archive()
    {
        var provider = new NodeRuntimeProvider();
        var artifact = Assert.Single(provider.Descriptor.Artifacts);
        var context = new RuntimeInstallContext(
            artifact,
            Path.Combine(
                Path.GetTempPath(),
                "agent-env-manager-node-runtime",
                "24.1.0"));

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
        Assert.Contains("Expand-Archive", script);
    }

    [Fact]
    public void CreateStateFiles_binds_npm_state_to_managed_version()
    {
        var provider = new NodeRuntimeProvider();
        var runtimeStateDirectory = Path.Combine(
            Path.GetTempPath(),
            "agent-env-manager-node-state",
            "runtime-node-24.1.0-win-x64");
        var nodeLocation = Path.Combine(
            Path.GetTempPath(),
            "agent-env-manager-node-runtime",
            "node-v24.1.0-win-x64");
        var manifest = CreateNodeManifest(nodeLocation);

        var files = provider.CreateStateFiles(
            new RuntimeStateBindingContext(
                manifest,
                runtimeStateDirectory));

        var npmrc = Assert.Single(
            files,
            file => file.Path.EndsWith(
                "npmrc",
                StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            Path.Combine(runtimeStateDirectory, "npm-global")
                .Replace('\\', '/'),
            npmrc.Content);
        Assert.Contains(
            Path.Combine(runtimeStateDirectory, "npm-cache")
                .Replace('\\', '/'),
            npmrc.Content);

        var npm = Assert.Single(
            files,
            file => file.Path.EndsWith(
                "npm.cmd",
                StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            "NPM_CONFIG_PREFIX=",
            npm.Content,
            StringComparison.Ordinal);
        Assert.Contains(
            "NPM_CONFIG_CACHE=",
            npm.Content,
            StringComparison.Ordinal);
        Assert.Contains(
            "npm-cli.js",
            npm.Content,
            StringComparison.Ordinal);

        var npx = Assert.Single(
            files,
            file => file.Path.EndsWith(
                "npx.cmd",
                StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            "npx-cli.js",
            npx.Content,
            StringComparison.Ordinal);
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
                Directory.CreateDirectory(Path.Combine(
                    invocation.WorkingDirectory,
                    "node-v24.1.0-win-x64",
                    "node_modules",
                    "npm"));
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
            var npmCommand = Path.Combine(
                preview.Location,
                "npm.cmd");
            Assert.True(File.Exists(npmCommand));
            Assert.Contains(
                "NPM_CONFIG_PREFIX=",
                await File.ReadAllTextAsync(npmCommand),
                StringComparison.Ordinal);

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

    [Fact]
    public async Task InstallRuntimeAsync_rebinds_node_state_when_data_root_changes()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"agent-env-manager-node-rebind-tests-{Guid.NewGuid():N}");
        var stateRoot = Path.Combine(root, "state");
        var firstDataRoot = Path.Combine(root, "data-a");
        var secondDataRoot = Path.Combine(root, "data-b");
        var runtimeRoot = Path.Combine(firstDataRoot, "runtimes");
        Directory.CreateDirectory(root);
        try
        {
            var manifestStore = new InMemoryManifestStore();
            var link = new RecordingActivationLink();
            var runner = new RecordingRuntimeCommandRunner(invocation =>
            {
                CreateNodeInstallation(invocation.WorkingDirectory);
                return new RuntimeCommandResult(
                    0,
                    "installed",
                    string.Empty);
            });
            var firstManager = CreateNodeManager(
                manifestStore,
                link,
                runner,
                ManagerPaths.Resolve(stateRoot, firstDataRoot),
                runtimeRoot);
            var preview = await firstManager.PreviewRuntimeInstallAsync(
                "node",
                "24.1.0");
            var installed = await firstManager.InstallRuntimeAsync(preview);
            var npmCommandPath = Path.Combine(preview.Location, "npm.cmd");

            Assert.Contains(
                ManagedPath(firstDataRoot, installed.Manifest.Identity.Value),
                await File.ReadAllTextAsync(npmCommandPath),
                StringComparison.Ordinal);

            var secondManager = CreateNodeManager(
                manifestStore,
                link,
                runner,
                ManagerPaths.Resolve(stateRoot, secondDataRoot),
                runtimeRoot);
            var existingPreview = await secondManager.PreviewRuntimeInstallAsync(
                "node",
                "24.1.0");

            await secondManager.InstallRuntimeAsync(existingPreview);

            Assert.True(existingPreview.IsAlreadyInstalled);
            Assert.Contains(
                ManagedPath(secondDataRoot, installed.Manifest.Identity.Value),
                await File.ReadAllTextAsync(npmCommandPath),
                StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task InstallRuntimeAsync_removes_new_node_state_when_health_fails()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"agent-env-manager-node-cleanup-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var link = new RecordingActivationLink();
            var runner = new RecordingRuntimeCommandRunner(invocation =>
            {
                CreateNodeInstallation(invocation.WorkingDirectory);
                return new RuntimeCommandResult(
                    0,
                    "installed",
                    string.Empty);
            });
            var manager = CreateNodeManager(
                new InMemoryManifestStore(),
                link,
                runner,
                ManagerPaths.Resolve(root),
                Path.Combine(root, "runtimes"),
                isHealthy: false);
            var preview = await manager.PreviewRuntimeInstallAsync(
                "node",
                "24.1.0");

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => manager.InstallRuntimeAsync(preview));

            Assert.False(Directory.Exists(preview.InstallRoot));
            Assert.False(Directory.Exists(Path.Combine(
                root,
                "data",
                "runtime-state",
                preview.Identity.Value)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SwitchVersionAsync_does_not_modify_adopted_node_runtime()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"agent-env-manager-node-adopted-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var firstPath = Path.Combine(root, "node-22");
            var secondPath = Path.Combine(root, "node-24");
            Directory.CreateDirectory(firstPath);
            Directory.CreateDirectory(secondPath);
            File.WriteAllText(
                Path.Combine(firstPath, "node.cmd"),
                "@echo off\r\necho 22.22.0\r\n");
            File.WriteAllText(
                Path.Combine(secondPath, "node.cmd"),
                "@echo off\r\necho 24.1.0\r\n");
            var manifestStore = new InMemoryManifestStore();
            var link = new RecordingActivationLink();
            var manager = new EnvironmentManager(
                new StubEnvironmentProbe(
                    new EnvironmentProbeResult(
                        [
                            CreateNodeAsset(firstPath, "22.22.0"),
                            CreateNodeAsset(secondPath, "24.1.0")
                        ],
                        [])),
                manifestStore: manifestStore,
                recoveryPointStore: new RecordingRecoveryPointStore(),
                operationJournal: new RecordingOperationJournal(),
                activationLink: link,
                healthCheck: new RecordingRuntimeHealthCheck(
                    isHealthy: true),
                managerPaths: ManagerPaths.Resolve(root),
                runtimeRoot: Path.Combine(root, "runtimes"),
                runtimeProviders: [new NodeRuntimeProvider()]);
            var inventory = await manager.InspectAsync();
            var first = await manager.AdoptAsync(
                inventory.Environments[0].Fingerprint);
            var second = await manager.AdoptAsync(
                inventory.Environments[1].Fingerprint);
            await link.SetTargetAsync(
                first.Manifest.StableActivationPath,
                first.Manifest.Location);

            var preview = await manager.PreviewVersionSwitchAsync(
                second.Manifest.Fingerprint);
            await manager.SwitchVersionAsync(preview);

            Assert.False(File.Exists(Path.Combine(secondPath, "npm.cmd")));
            Assert.False(File.Exists(Path.Combine(secondPath, "npx.cmd")));
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

    private static EnvironmentManifest CreateNodeManifest(
        string nodeLocation)
    {
        var identity = "runtime-node-24.1.0-win-x64";
        return new EnvironmentManifest(
            new EnvironmentIdentity(identity),
            new EnvironmentFingerprint(identity),
            EnvironmentAssetKind.ToolRuntime,
            "Node.js",
            "24.1.0",
            DiscoverySourceInfo.RuntimeProvider,
            nodeLocation,
            @"C:\activations\node\current",
            "asset-hash",
            "recovery",
            "install",
            IsSystemComponent: false,
            DateTimeOffset.UnixEpoch,
            "node-activation",
            @"C:\shims\node");
    }

    private static EnvironmentAsset CreateNodeAsset(
        string directory,
        string version)
    {
        return new EnvironmentAsset(
            EnvironmentAssetKind.ToolRuntime,
            "Node.js",
            version,
            Path.Combine(directory, "node.cmd"),
            IsSystemComponent: false,
            Source: DiscoverySourceInfo.PathCommand);
    }

    private static EnvironmentManager CreateNodeManager(
        IEnvironmentManifestStore manifestStore,
        RecordingActivationLink link,
        RecordingRuntimeCommandRunner runner,
        ManagerPaths managerPaths,
        string runtimeRoot,
        bool isHealthy = true)
    {
        return new EnvironmentManager(
            new StubEnvironmentProbe(
                new EnvironmentProbeResult([], [])),
            manifestStore: manifestStore,
            recoveryPointStore: new RecordingRecoveryPointStore(),
            operationJournal: new RecordingOperationJournal(),
            activationLink: link,
            healthCheck: new RecordingRuntimeHealthCheck(isHealthy),
            managerPaths: managerPaths,
            runtimeRoot: runtimeRoot,
            runtimeProviders: [new NodeRuntimeProvider()],
            runtimeCommandRunner: runner);
    }

    private static void CreateNodeInstallation(string installRoot)
    {
        var nodeDirectory = Path.Combine(
            installRoot,
            "node-v24.1.0-win-x64");
        Directory.CreateDirectory(nodeDirectory);
        File.WriteAllText(
            Path.Combine(nodeDirectory, "node.exe"),
            string.Empty);
        Directory.CreateDirectory(Path.Combine(
            nodeDirectory,
            "node_modules",
            "npm"));
    }

    private static string ManagedPath(
        string dataRoot,
        string identity)
    {
        return Path.Combine(
            Path.GetFullPath(dataRoot),
            "runtime-state",
            identity,
            "npm-global").Replace('\\', '/');
    }
}
