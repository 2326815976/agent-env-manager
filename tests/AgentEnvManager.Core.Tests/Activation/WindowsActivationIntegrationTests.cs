using System.Diagnostics;
using AgentEnvManager.Core.Activation;
using AgentEnvManager.Core.Adoption;
using AgentEnvManager.Core.Inspection;
using AgentEnvManager.Core.Operations;
using AgentEnvManager.Core.Storage;
using AgentEnvManager.Core.Tests.TestSupport;

namespace AgentEnvManager.Core.Tests.Activation;

public sealed class WindowsActivationIntegrationTests
{
    [Fact]
    public async Task SwitchVersionAsync_resolves_target_version_in_child_process()
    {
        using var fixture = new ActivationFixture();
        var first = fixture.CreateNodeVersion("node-22", "22.22.0");
        var second = fixture.CreateNodeVersion("node-24", "24.1.0");
        var manager = fixture.CreateManager(first, second);
        var inventory = await manager.InspectAsync();
        var firstObserved = inventory.Environments.Single(environment =>
            environment.Asset.Location == Path.Combine(first, "node.cmd"));
        var secondObserved = inventory.Environments.Single(environment =>
            environment.Asset.Location == Path.Combine(second, "node.cmd"));

        await manager.AdoptAsync(firstObserved.Fingerprint);
        await manager.AdoptAsync(secondObserved.Fingerprint);
        await manager.SwitchVersionAsync(firstObserved.Fingerprint);
        await manager.SwitchVersionAsync(secondObserved.Fingerprint);

        var version = await RunCommandThroughActivationAsync(
            fixture.ActivationPath,
            "node --version");

        Assert.Equal("24.1.0", version);
    }

    [Fact]
    public async Task SwitchVersionAsync_restores_previous_junction_after_real_health_failure()
    {
        using var fixture = new ActivationFixture();
        var first = fixture.CreateNodeVersion("node-22", "22.22.0");
        var second = fixture.CreateBrokenNodeVersion("node-24");
        var manager = fixture.CreateManager(first, second);
        var inventory = await manager.InspectAsync();
        var firstObserved = inventory.Environments.Single(environment =>
            environment.Asset.Location == Path.Combine(first, "node.cmd"));
        var secondObserved = inventory.Environments.Single(environment =>
            environment.Asset.Location == Path.Combine(second, "node.cmd"));
        await manager.AdoptAsync(firstObserved.Fingerprint);
        await manager.AdoptAsync(secondObserved.Fingerprint);
        await manager.SwitchVersionAsync(firstObserved.Fingerprint);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => manager.SwitchVersionAsync(secondObserved.Fingerprint));

        Assert.Equal(
            first,
            await new WindowsJunctionActivationLink().GetTargetAsync(
                fixture.ActivationPath));
        var version = await RunCommandThroughActivationAsync(
            fixture.ActivationPath,
            "node --version");
        Assert.Equal("22.22.0", version);
    }

    private static async Task<string> RunCommandThroughActivationAsync(
        string activationPath,
        string command)
    {
        var startInfo = new ProcessStartInfo(
            Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = activationPath
        };
        startInfo.ArgumentList.Add("/d");
        startInfo.ArgumentList.Add("/s");
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add(command);
        startInfo.Environment["PATH"] =
            $"{activationPath};{startInfo.Environment["PATH"]}";

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("无法启动测试子进程。");
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(
            process.ExitCode == 0,
            $"子进程退出码 {process.ExitCode}: {error}");
        return output.Trim();
    }

    private sealed class ActivationFixture : IDisposable
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(),
            "AgentEnvManager.Tests",
            Guid.NewGuid().ToString("N"));

        public ActivationFixture()
        {
            Directory.CreateDirectory(_root);
            ActivationPath = Path.Combine(
                _root,
                "activations",
                "node",
                "current");
        }

        public string ActivationPath { get; }

        public string CreateNodeVersion(string directoryName, string version)
        {
            var path = Path.Combine(_root, directoryName);
            Directory.CreateDirectory(path);
            File.WriteAllText(
                Path.Combine(path, "node.cmd"),
                $"@echo off{Environment.NewLine}echo {version}{Environment.NewLine}");
            return path;
        }

        public string CreateBrokenNodeVersion(string directoryName)
        {
            var path = Path.Combine(_root, directoryName);
            Directory.CreateDirectory(path);
            File.WriteAllText(
                Path.Combine(path, "node.cmd"),
                $"@echo off{Environment.NewLine}exit /b 1{Environment.NewLine}");
            return path;
        }

        public EnvironmentManager CreateManager(
            string firstPath,
            string secondPath)
        {
            var manifestStore = new InMemoryManifestStore();
            var journal = new RecordingOperationJournal();
            return new EnvironmentManager(
                new FixedNodeProbe(firstPath, secondPath),
                manifestStore: manifestStore,
                assetHasher: new FileSystemEnvironmentAssetHasher(),
                recoveryPointStore: new RecordingRecoveryPointStore(),
                operationJournal: journal,
                activationPathFactory: new FixedActivationPathFactory(
                    ActivationPath),
                activationLink: new WindowsJunctionActivationLink(),
                healthCheck: new ProcessRuntimeHealthCheck());
        }

        public void Dispose()
        {
            try
            {
                new WindowsJunctionActivationLink()
                    .DeleteAsync(ActivationPath)
                    .GetAwaiter()
                    .GetResult();
            }
            catch (InvalidOperationException)
            {
            }

            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class FixedActivationPathFactory(string activationPath)
        : IStableActivationPathFactory
    {
        public string Create(EnvironmentIdentity identity)
        {
            return activationPath;
        }
    }

    private sealed class FixedNodeProbe(string firstPath, string secondPath)
        : IEnvironmentProbe
    {
        public Task<EnvironmentProbeResult> ProbeAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new EnvironmentProbeResult(
                [
                    new EnvironmentAsset(
                        EnvironmentAssetKind.ToolRuntime,
                        "Node.js",
                        "22.22.0",
                        Path.Combine(firstPath, "node.cmd"),
                        IsSystemComponent: false,
                        Source: DiscoverySourceInfo.PathCommand),
                    new EnvironmentAsset(
                        EnvironmentAssetKind.ToolRuntime,
                        "Node.js",
                        "24.1.0",
                        Path.Combine(secondPath, "node.cmd"),
                        IsSystemComponent: false,
                        Source: DiscoverySourceInfo.PathCommand)
                ],
                []));
        }
    }

}
