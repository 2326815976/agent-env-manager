using AgentEnvManager.Core.Inspection;
using AgentEnvManager.Core.Runtimes;
using AgentEnvManager.Core.Storage;
using AgentEnvManager.Core.Tests.TestSupport;
using System.Text;
using System.Text.Json;

namespace AgentEnvManager.Core.Tests.Runtimes;

public sealed class PowerShellRuntimeProviderTests
{
    [Fact]
    public void DescribeRuntimeProviders_declares_powershell_official_archive()
    {
        var manager = new EnvironmentManager(
            new StubEnvironmentProbe(
                new EnvironmentProbeResult([], [])));

        var powershell = Assert.Single(
            manager.DescribeRuntimeProviders(),
            provider => provider.Id == "powershell");

        Assert.Equal("PowerShell 7", powershell.Name);
        Assert.Equal(RuntimeProviderMode.Installable, powershell.Mode);
        Assert.Equal(DiscoverySourceInfo.RuntimeProvider, powershell.Source);
        Assert.Equal(
            "https://github.com/PowerShell/PowerShell/releases/tag/v7.6.6",
            powershell.OfficialSource);
        Assert.Equal("MIT", powershell.License);
        Assert.Equal(RuntimeInstallStrategy.OfficialArchive, powershell.InstallStrategy);
        var artifact = Assert.Single(powershell.Artifacts);
        Assert.Equal("7.6.6", artifact.Version);
        Assert.Equal(
            "02fe458be20493fbdf43f61ea20610b811ee6c738ab1676c61b9cfcd1a33c860",
            artifact.Sha256);
        Assert.Contains("win-x64", artifact.SupportedArchitectures);
        Assert.Equal(
            "https://github.com/PowerShell/PowerShell/releases/download/v7.6.6/PowerShell-7.6.6-win-x64.zip",
            artifact.DownloadUrl);
    }

    [Fact]
    public void CreateInstallCommand_verifies_archive_and_creates_entry()
    {
        var provider = new PowerShellRuntimeProvider();
        var artifact = Assert.Single(provider.Descriptor.Artifacts);
        var context = new RuntimeInstallContext(
            artifact,
            Path.Combine(
                Path.GetTempPath(),
                "agent-env-manager-powershell-runtime",
                "7.6.6"));

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
    public void CreateStateFiles_separates_modules_and_profile()
    {
        var provider = new PowerShellRuntimeProvider();
        var stateDirectory = Path.Combine(
            Path.GetTempPath(),
            "agent-env-manager-powershell-state");
        var manifest = CreatePowerShellManifest(
            Path.Combine(
                Path.GetTempPath(),
                "agent-env-manager-powershell-runtime",
                "7.6.6"));

        var files = provider.CreateStateFiles(
            new RuntimeStateBindingContext(manifest, stateDirectory));

        var config = Assert.Single(
            files,
            file => file.Path.EndsWith(
                "powershell.config.json",
                StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            "PSModulePath",
            config.Content,
            StringComparison.Ordinal);
        using var document = JsonDocument.Parse(config.Content);
        Assert.Equal(
            Path.Combine(stateDirectory, "PowerShell", "Modules"),
            document.RootElement
                .GetProperty("PSModulePath")
                .GetString());
        Assert.Contains(
            files,
            file => file.Path.EndsWith(
                "profile.ps1",
                StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            files,
            file => file.Path.EndsWith(
                ".keep",
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task InstallRuntimeAsync_activates_powershell_through_managed_entry()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"agent-env-manager-powershell-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var link = new RecordingActivationLink();
            var healthCheck = new RecordingRuntimeHealthCheck(
                isHealthy: true);
            var runner = new RecordingRuntimeCommandRunner(invocation =>
            {
                File.WriteAllText(
                    Path.Combine(invocation.WorkingDirectory, "pwsh.exe"),
                    string.Empty);
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
                runtimeProviders: [new PowerShellRuntimeProvider()],
                runtimeCommandRunner: runner);

            var preview = await manager.PreviewRuntimeInstallAsync(
                "powershell",
                "7.6.6");
            var installed = await manager.InstallRuntimeAsync(preview);

            Assert.Equal("PowerShell 7", installed.Manifest.Name);
            Assert.Equal(preview.Location, installed.Manifest.Location);
            Assert.Equal(
                preview.Location,
                await link.GetTargetAsync(
                    preview.StableActivationPath));
            Assert.Equal(
                preview.ManagedEntryPath,
                healthCheck.LastManagedEntryPath);
            Assert.True(File.Exists(installed.ExecutablePath));
            Assert.Contains(
                "PSModulePath",
                await File.ReadAllTextAsync(
                    Path.Combine(
                        preview.Location,
                        "powershell.config.json")),
                StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task InspectAsync_keeps_windows_powershell_observed_only()
    {
        var manager = new EnvironmentManager(
            new StubEnvironmentProbe(
                new EnvironmentProbeResult(
                    [
                        new EnvironmentAsset(
                            EnvironmentAssetKind.Shell,
                            "Windows PowerShell",
                            "5.1",
                            @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe",
                            IsSystemComponent: true,
                            Source: DiscoverySourceInfo.SystemPath)
                    ],
                    [])));

        var report = await manager.InspectAsync();

        var observed = Assert.Single(report.Environments);
        Assert.Equal(ManagementState.Observed, observed.ManagementState);
        Assert.True(observed.Asset.IsSystemComponent);
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

    private static AgentEnvManager.Core.Adoption.EnvironmentManifest
        CreatePowerShellManifest(string location)
    {
        return new AgentEnvManager.Core.Adoption.EnvironmentManifest(
            new AgentEnvManager.Core.Adoption.EnvironmentIdentity(
                "runtime-powershell-7.6.6-win-x64"),
            new AgentEnvManager.Core.Adoption.EnvironmentFingerprint(
                "runtime-powershell-7.6.6-win-x64"),
            EnvironmentAssetKind.Shell,
            "PowerShell 7",
            "7.6.6",
            DiscoverySourceInfo.RuntimeProvider,
            location,
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
