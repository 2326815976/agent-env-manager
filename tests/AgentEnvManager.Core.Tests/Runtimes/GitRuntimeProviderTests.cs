using AgentEnvManager.Core.Inspection;
using AgentEnvManager.Core.Migrations;
using AgentEnvManager.Core.Runtimes;
using AgentEnvManager.Core.Storage;
using AgentEnvManager.Core.Tests.TestSupport;
using System.Text;

namespace AgentEnvManager.Core.Tests.Runtimes;

public sealed class GitRuntimeProviderTests
{
    [Fact]
    public void DescribeRuntimeProviders_declares_git_official_archive()
    {
        var manager = new EnvironmentManager(
            new StubEnvironmentProbe(
                new EnvironmentProbeResult([], [])));

        var git = Assert.Single(
            manager.DescribeRuntimeProviders(),
            provider => provider.Id == "git");

        Assert.Equal("Git", git.Name);
        Assert.Equal(RuntimeProviderMode.Installable, git.Mode);
        Assert.Equal(DiscoverySourceInfo.RuntimeProvider, git.Source);
        Assert.Equal(
            "https://github.com/git-for-windows/git/releases/tag/v2.56.0.windows.1",
            git.OfficialSource);
        Assert.Equal("GPL-2.0", git.License);
        Assert.Equal(RuntimeInstallStrategy.OfficialArchive, git.InstallStrategy);
        var artifact = Assert.Single(git.Artifacts);
        Assert.Equal("2.56.0", artifact.Version);
        Assert.Equal(
            "064b440ff870ed5198527e8f3a92cdf5bd2fd0fedf5e718af95e3fdaddeff718",
            artifact.Sha256);
        Assert.Contains("win-x64", artifact.SupportedArchitectures);
        Assert.Equal(
            "https://github.com/git-for-windows/git/releases/download/v2.56.0.windows.1/MinGit-2.56.0-64-bit.zip",
            artifact.DownloadUrl);
    }

    [Fact]
    public void CreateInstallCommand_verifies_archive_and_creates_git_entry()
    {
        var provider = new GitRuntimeProvider();
        var artifact = Assert.Single(provider.Descriptor.Artifacts);
        var context = new RuntimeInstallContext(
            artifact,
            Path.Combine(
                Path.GetTempPath(),
                "agent-env-manager-git-runtime",
                "2.56.0"));

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
        Assert.Contains("git.cmd", script);
        Assert.Contains(@"cmd\git.exe", script);
    }

    [Fact]
    public async Task InstallRuntimeAsync_activates_git_through_managed_entry()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"agent-env-manager-git-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var link = new RecordingActivationLink();
            var healthCheck = new RecordingRuntimeHealthCheck(
                isHealthy: true);
            var runner = new RecordingRuntimeCommandRunner(invocation =>
            {
                var gitExecutable = Path.Combine(
                    invocation.WorkingDirectory,
                    "cmd",
                    "git.exe");
                Directory.CreateDirectory(
                    Path.GetDirectoryName(gitExecutable)!);
                File.WriteAllText(gitExecutable, string.Empty);
                File.WriteAllText(
                    Path.Combine(invocation.WorkingDirectory, "git.cmd"),
                    "@echo off\r\n");
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
                runtimeProviders: [new GitRuntimeProvider()],
                runtimeCommandRunner: runner);

            var preview = await manager.PreviewRuntimeInstallAsync(
                "git",
                "2.56.0");
            var installed = await manager.InstallRuntimeAsync(preview);

            Assert.Equal("Git", installed.Manifest.Name);
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
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task InspectAsync_keeps_existing_git_observed_only()
    {
        var manager = new EnvironmentManager(
            new StubEnvironmentProbe(
                new EnvironmentProbeResult(
                    [
                        new EnvironmentAsset(
                            EnvironmentAssetKind.ToolRuntime,
                            "Git",
                            "2.50.0",
                            @"C:\Program Files\Git\cmd\git.exe",
                            IsSystemComponent: false,
                            Source: DiscoverySourceInfo.KnownInstallation)
                    ],
                    [])));

        var report = await manager.InspectAsync();

        var observed = Assert.Single(report.Environments);
        Assert.Equal(ManagementState.Observed, observed.ManagementState);
    }

    [Fact]
    public async Task MigrateEnvironmentAsync_does_not_touch_git_secrets()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"agent-env-manager-git-migration-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var sourceRuntime = Path.Combine(root, "runtime");
            var destinationRuntime = Path.Combine(root, "migrated");
            var userProfile = Path.Combine(root, "user");
            Directory.CreateDirectory(Path.Combine(sourceRuntime, "cmd"));
            File.WriteAllText(
                Path.Combine(sourceRuntime, "cmd", "git.exe"),
                "git");
            Directory.CreateDirectory(userProfile);
            File.WriteAllText(
                Path.Combine(userProfile, ".gitconfig"),
                "[user]\n\tname = Test\n");
            File.WriteAllText(
                Path.Combine(userProfile, ".git-credentials"),
                "https://secret\n");
            Directory.CreateDirectory(Path.Combine(userProfile, ".ssh"));
            File.WriteAllText(
                Path.Combine(userProfile, ".ssh", "id_rsa"),
                "PRIVATE KEY");
            var manifest = CreateManagedGitManifest(sourceRuntime);
            var manifestStore = new InMemoryManifestStore();
            await manifestStore.SaveAsync(manifest);
            var link = new RecordingActivationLink();
            await link.SetTargetAsync(
                manifest.StableActivationPath,
                sourceRuntime);
            await link.SetTargetAsync(
                manifest.ManagedEntryPath,
                manifest.StableActivationPath);
            var manager = new EnvironmentManager(
                new StubEnvironmentProbe(
                    new EnvironmentProbeResult([], [])),
                manifestStore: manifestStore,
                recoveryPointStore: new RecordingRecoveryPointStore(),
                operationJournal: new RecordingOperationJournal(),
                activationLink: link,
                healthCheck: new RecordingRuntimeHealthCheck(
                    isHealthy: true),
                environmentPathMover: new FileSystemEnvironmentPathMover(),
                managerPaths: ManagerPaths.Resolve(root),
                runtimeRoot: Path.Combine(root, "runtimes"),
                runtimeProviders: [new GitRuntimeProvider()]);

            var preview = await manager.PreviewMigrationAsync(
                manifest.Fingerprint,
                destinationRuntime);
            await manager.MigrateEnvironmentAsync(preview);

            Assert.True(Directory.Exists(destinationRuntime));
            Assert.False(Directory.Exists(sourceRuntime));
            Assert.Equal(
                "[user]\n\tname = Test\n",
                await File.ReadAllTextAsync(
                    Path.Combine(userProfile, ".gitconfig")));
            Assert.Equal(
                "https://secret\n",
                await File.ReadAllTextAsync(
                    Path.Combine(userProfile, ".git-credentials")));
            Assert.Equal(
                "PRIVATE KEY",
                await File.ReadAllTextAsync(
                    Path.Combine(userProfile, ".ssh", "id_rsa")));
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

    private static AgentEnvManager.Core.Adoption.EnvironmentManifest
        CreateManagedGitManifest(string runtimeRoot)
    {
        return new AgentEnvManager.Core.Adoption.EnvironmentManifest(
            new AgentEnvManager.Core.Adoption.EnvironmentIdentity(
                "runtime-git-2.56.0-win-x64"),
            new AgentEnvManager.Core.Adoption.EnvironmentFingerprint(
                "runtime-git-2.56.0-win-x64"),
            EnvironmentAssetKind.ToolRuntime,
            "Git",
            "2.56.0",
            DiscoverySourceInfo.RuntimeProvider,
            runtimeRoot,
            @"C:\activations\git\current",
            "asset-hash",
            "recovery",
            "install",
            IsSystemComponent: false,
            DateTimeOffset.UnixEpoch,
            "git-activation",
            @"C:\shims\git");
    }
}
