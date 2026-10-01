using System.Text;
using AgentEnvManager.Core.Adoption;
using AgentEnvManager.Core.Inspection;
using AgentEnvManager.Core.Runtimes;
using AgentEnvManager.Core.Storage;
using AgentEnvManager.Core.Tests.TestSupport;

namespace AgentEnvManager.Core.Tests.Runtimes;

public sealed class FirstVersionDependencyTests
{
    private static readonly string[] FirstVersionProviderIds =
    [
        "python",
        "node",
        "git",
        "powershell",
        "uv",
        "npm",
        "pnpm",
        "ripgrep",
        "gh"
    ];

    [Fact]
    public void DescribeRuntimeProviders_covers_first_version_dependency_cards()
    {
        var manager = new EnvironmentManager(
            new StubEnvironmentProbe(
                new EnvironmentProbeResult([], [])));

        var providers = manager.DescribeRuntimeProviders();
        var installable = providers
            .Where(provider =>
                provider.Mode == RuntimeProviderMode.Installable)
            .Select(provider => provider.Id)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            FirstVersionProviderIds
                .OrderBy(id => id, StringComparer.Ordinal),
            installable);

        foreach (var provider in providers.Where(candidate =>
                     candidate.Mode == RuntimeProviderMode.Installable))
        {
            Assert.False(string.IsNullOrWhiteSpace(provider.Name));
            Assert.False(string.IsNullOrWhiteSpace(provider.OfficialSource));
            Assert.False(string.IsNullOrWhiteSpace(provider.License));
            Assert.NotEmpty(provider.Artifacts);
            foreach (var artifact in provider.Artifacts)
            {
                Assert.False(string.IsNullOrWhiteSpace(artifact.Version));
                Assert.False(
                    string.IsNullOrWhiteSpace(artifact.OfficialSource));
                Assert.False(string.IsNullOrWhiteSpace(artifact.License));
                Assert.False(string.IsNullOrWhiteSpace(artifact.Sha256));
                Assert.False(string.IsNullOrWhiteSpace(artifact.DownloadUrl));
                Assert.Contains(
                    "win-x64",
                    artifact.SupportedArchitectures);
            }
        }

        var conda = Assert.Single(
            providers,
            provider => provider.Id == "conda");
        Assert.Equal(RuntimeProviderMode.ObservedOnly, conda.Mode);
        Assert.Empty(conda.Artifacts);
    }

    [Fact]
    public void Uv_declares_official_release_metadata()
    {
        AssertProvider(
            new UvRuntimeProvider(),
            "uv",
            "uv",
            "0.12.21",
            "https://github.com/astral-sh/uv",
            "https://github.com/astral-sh/uv/releases/download/0.12.21/uv-x86_64-pc-windows-msvc.zip",
            "5d223efa0bf00208c3853246af09420419dfbd352536aa6bb8163d6170e23890",
            Path.Combine("uv.exe"));
    }

    [Fact]
    public void Ripgrep_declares_official_release_metadata()
    {
        AssertProvider(
            new RipgrepRuntimeProvider(),
            "ripgrep",
            "ripgrep",
            "15.2.0",
            "https://github.com/BurntSushi/ripgrep",
            "https://github.com/BurntSushi/ripgrep/releases/download/15.2.0/ripgrep-15.2.0-x86_64-pc-windows-msvc.zip",
            "71b2fef860abe467217a538ff31de02f5258807c0129f771846f87bd029aafc5",
            Path.Combine(
                "ripgrep-15.2.0-x86_64-pc-windows-msvc",
                "rg.exe"));
    }

    [Fact]
    public void GitHubCli_declares_official_release_metadata()
    {
        AssertProvider(
            new GitHubCliRuntimeProvider(),
            "gh",
            "GitHub CLI",
            "2.102.0",
            "https://github.com/cli/cli",
            "https://github.com/cli/cli/releases/download/v2.102.0/gh_2.102.0_windows_amd64.zip",
            "ae64e556ecc240b200f7eba60d550e4bb60d78e860e69dd88c449405b86067f4",
            Path.Combine("bin", "gh.exe"));
    }

    [Fact]
    public void Pnpm_declares_recorded_official_artifact_metadata()
    {
        AssertProvider(
            new PnpmRuntimeProvider(),
            "pnpm",
            "pnpm",
            "12.8.1",
            "https://github.com/pnpm/pnpm",
            "https://github.com/pnpm/pnpm/releases/download/v12.8.1/pnpm-win32-x64.zip",
            "823fc131326afeee292e113e2299ca9851c3ee5148a7de921c897f0170de6f05",
            "pnpm.exe");
    }

    [Fact]
    public void Npm_declares_recorded_official_artifact_metadata()
    {
        AssertProvider(
            new NpmRuntimeProvider(),
            "npm",
            "npm",
            "11.21.0",
            "https://registry.npmjs.org/npm",
            "https://registry.npmjs.org/npm/-/npm-11.21.0.tgz",
            "783e7c92bf73b442fb800c2d6ef3921e86da8894a700fed45140e37916877482",
            Path.Combine("package", "bin", "npm.cmd"));
    }

    [Fact]
    public void First_version_archives_install_into_requested_root()
    {
        IRuntimeProvider[] providers =
        [
            new UvRuntimeProvider(),
            new RipgrepRuntimeProvider(),
            new GitHubCliRuntimeProvider(),
            new PnpmRuntimeProvider(),
            new NpmRuntimeProvider()
        ];

        foreach (var provider in providers)
        {
            var artifact = Assert.Single(provider.Descriptor.Artifacts);
            var installRoot = Path.Combine(
                Path.GetTempPath(),
                "agent-env-manager-first-version",
                provider.Descriptor.Id);

            var command = provider.CreateInstallCommand(
                new RuntimeInstallContext(
                    artifact,
                    installRoot,
                    CachedArtifactPath: Path.Combine(
                        Path.GetTempPath(),
                        $"{provider.Descriptor.Id}-artifact"),
                    MirrorUrl: null));

            Assert.Equal(installRoot, command.WorkingDirectory);
            Assert.Contains(
                artifact.Sha256,
                DecodeScript(command),
                StringComparison.OrdinalIgnoreCase);
            Assert.False(
                Path.IsPathRooted(
                    provider.GetExecutableRelativePath(artifact)),
                $"{provider.Descriptor.Id} 的可执行文件相对路径应为相对路径。");
        }
    }

    [Fact]
    public void Npm_install_command_writes_runnable_cli_shims()
    {
        var provider = new NpmRuntimeProvider();
        var artifact = Assert.Single(provider.Descriptor.Artifacts);
        var installRoot = Path.Combine(
            Path.GetTempPath(),
            "agent-env-manager-npm-shim");

        var script = DecodeScript(provider.CreateInstallCommand(
            new RuntimeInstallContext(
                artifact,
                installRoot,
                CachedArtifactPath: Path.Combine(
                    Path.GetTempPath(),
                    "npm-artifact.tgz"),
                MirrorUrl: null)));

        Assert.Contains(
            Path.Combine(installRoot, "package", "bin", "npm.cmd"),
            script,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("npm-cli.js", script, StringComparison.Ordinal);
        Assert.Contains(
            Path.Combine(installRoot, "package", "bin", "npx.cmd"),
            script,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("npx-cli.js", script, StringComparison.Ordinal);
        Assert.DoesNotContain("node_modules\\npm", script);
    }

    [Fact]
    public async Task PreviewRuntimeInstallAsync_reports_cached_official_archive()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"agent-env-manager-cache-status-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var manager = new EnvironmentManager(
                new StubEnvironmentProbe(
                    new EnvironmentProbeResult([], [])),
                manifestStore: new InMemoryManifestStore(),
                recoveryPointStore: new RecordingRecoveryPointStore(),
                operationJournal: new RecordingOperationJournal(),
                managerPaths: ManagerPaths.Resolve(root),
                runtimeRoot: Path.Combine(root, "runtimes"),
                runtimeProviders: [new UvRuntimeProvider()],
                artifactCache: new FixedArtifactCache(
                    Path.Combine(root, "cache", "uv.zip")));

            var preview = await manager.PreviewRuntimeInstallAsync(
                "uv",
                "0.12.21");

            Assert.True(preview.IsCachedArtifactAvailable);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task PreviewRuntimeInstallAsync_resolves_new_provider_paths()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"agent-env-manager-first-version-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var manager = new EnvironmentManager(
                new StubEnvironmentProbe(
                    new EnvironmentProbeResult([], [])),
                managerPaths: ManagerPaths.Resolve(root),
                runtimeRoot: Path.Combine(root, "runtimes"));

            var uv = await manager.PreviewRuntimeInstallAsync(
                "uv",
                "0.12.21");
            Assert.Equal(
                Path.Combine(root, "runtimes", "uv", "0.12.21"),
                uv.InstallRoot);
            Assert.Equal(uv.InstallRoot, uv.Location);
            Assert.Equal(
                Path.Combine(uv.InstallRoot, "uv.exe"),
                uv.ExecutablePath);
            Assert.Contains(
                Path.Combine(root, "runtimes", "uv", "0.12.21"),
                uv.Impact,
                StringComparison.OrdinalIgnoreCase);

            var npm = await manager.PreviewRuntimeInstallAsync(
                "npm",
                "11.21.0");
            Assert.Equal(
                Path.Combine(npm.InstallRoot, "package", "bin"),
                npm.Location);
            Assert.Equal(
                Path.Combine(npm.Location, "npm.cmd"),
                npm.ExecutablePath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task DescribeManagedRuntimesAsync_reports_manager_installed_runtime()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"agent-env-manager-managed-runtime-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var manifestStore = new InMemoryManifestStore();
            var installRoot = Path.Combine(
                root,
                "runtimes",
                "uv",
                "0.12.21");
            Directory.CreateDirectory(installRoot);
            await manifestStore.SaveAsync(new EnvironmentManifest(
                new EnvironmentIdentity("runtime-uv-0.12.21-win-x64"),
                new EnvironmentFingerprint("runtime-uv-0.12.21-win-x64"),
                EnvironmentAssetKind.PackageManager,
                "uv",
                "0.12.21",
                DiscoverySourceInfo.RuntimeProvider,
                installRoot,
                Path.Combine(root, "state", "activation", "uv", "current"),
                "artifact-sha256",
                "recovery-1",
                "operation-1",
                IsSystemComponent: false,
                DateTimeOffset.UnixEpoch,
                "activation-identity",
                Path.Combine(root, "state", "shims", "uv")));
            var manager = new EnvironmentManager(
                new StubEnvironmentProbe(
                    new EnvironmentProbeResult([], [])),
                manifestStore: manifestStore,
                managerPaths: ManagerPaths.Resolve(root));

            var statuses = await manager.DescribeManagedRuntimesAsync();

            Assert.Equal(
                FirstVersionProviderIds
                    .OrderBy(id => id, StringComparer.Ordinal),
                statuses
                    .Select(status => status.ProviderId)
                    .OrderBy(id => id, StringComparer.Ordinal));
            var uv = Assert.Single(
                statuses,
                status => status.ProviderId == "uv");
            Assert.True(uv.IsManaged);
            Assert.Equal("0.12.21", uv.Version);
            Assert.Equal(installRoot, uv.Location);
            Assert.Equal(
                Path.Combine(root, "state", "shims", "uv"),
                uv.ManagedEntryPath);
            Assert.All(
                statuses.Where(status => status.ProviderId != "uv"),
                status => Assert.False(status.IsManaged));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void AssertProvider(
        IRuntimeProvider provider,
        string id,
        string name,
        string version,
        string officialSource,
        string downloadUrl,
        string sha256,
        string executableRelativePath)
    {
        var descriptor = provider.Descriptor;
        Assert.Equal(id, descriptor.Id);
        Assert.Equal(name, descriptor.Name);
        Assert.Equal(
            RuntimeProviderMode.Installable,
            descriptor.Mode);
        Assert.Equal(
            RuntimeInstallStrategy.OfficialArchive,
            descriptor.InstallStrategy);
        Assert.Equal(officialSource, descriptor.OfficialSource);
        Assert.False(string.IsNullOrWhiteSpace(descriptor.License));

        var artifact = Assert.Single(descriptor.Artifacts);
        Assert.Equal(version, artifact.Version);
        Assert.Equal(officialSource, artifact.OfficialSource);
        Assert.Equal(downloadUrl, artifact.DownloadUrl);
        Assert.Equal(sha256, artifact.Sha256);
        Assert.Equal(
            RuntimeInstallStrategy.OfficialArchive,
            artifact.InstallStrategy);
        Assert.Equal(
            executableRelativePath,
            provider.GetExecutableRelativePath(artifact));
    }

    private static string DecodeScript(RuntimeInstallCommand command)
    {
        var encoded = command.Arguments.Last();
        return Encoding.Unicode.GetString(Convert.FromBase64String(encoded));
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
