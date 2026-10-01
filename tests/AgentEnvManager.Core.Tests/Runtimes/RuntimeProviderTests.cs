using AgentEnvManager.Core.Inspection;
using AgentEnvManager.Core.Runtimes;
using AgentEnvManager.Core.Storage;
using AgentEnvManager.Core.Tests.TestSupport;

namespace AgentEnvManager.Core.Tests.Runtimes;

public sealed class RuntimeProviderTests
{
    [Fact]
    public void DescribeRuntimeProviders_declares_python_provenance_and_conda_observation()
    {
        var manager = CreateManager();

        var providers = manager.DescribeRuntimeProviders();

        var python = Assert.Single(
            providers,
            provider => provider.Id == "python");
        Assert.Equal(RuntimeProviderMode.Installable, python.Mode);
        Assert.Equal(EnvironmentAssetKind.ToolRuntime, python.Kind);
        Assert.Equal(DiscoverySourceInfo.UvRuntime, python.Source);
        Assert.Equal(
            "https://github.com/astral-sh/python-build-standalone",
            python.OfficialSource);
        Assert.Equal("PSF-2.0", python.License);
        Assert.Equal(
            RuntimeInstallStrategy.UvManagedDownload,
            python.InstallStrategy);
        var artifact = Assert.Single(python.Artifacts);
        Assert.Equal("3.13.7", artifact.Version);
        Assert.Equal(
            "bc229e5364699a8456b6ceabde8348c75e62312ffd62631dd1e494a1755a45ed",
            artifact.Sha256);
        Assert.Contains("win-x64", artifact.SupportedArchitectures);
        Assert.Contains(
            "astral-sh/python-build-standalone/releases/download/20250918",
            artifact.DownloadUrl,
            StringComparison.Ordinal);

        var conda = Assert.Single(
            providers,
            provider => provider.Id == "conda");
        Assert.Equal(RuntimeProviderMode.ObservedOnly, conda.Mode);
        Assert.Equal(
            RuntimeInstallStrategy.ObservedRebuild,
            conda.InstallStrategy);
        Assert.Empty(conda.Artifacts);
    }

    [Fact]
    public async Task PreviewRuntimeInstallAsync_accepts_declared_official_archive_strategy()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"agent-env-manager-provider-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var manager = new EnvironmentManager(
                new StubEnvironmentProbe(
                    new EnvironmentProbeResult([], [])),
                managerPaths: ManagerPaths.Resolve(root),
                runtimeRoot: Path.Combine(root, "runtimes"),
                runtimeProviders: [new ArchiveTestProvider()]);

            var preview = await manager.PreviewRuntimeInstallAsync(
                "archive-tool",
                "1.0.0");

            Assert.Equal(
                RuntimeInstallStrategy.OfficialArchive,
                preview.Artifact.InstallStrategy);
            Assert.Equal(
                DiscoverySourceInfo.RuntimeProvider,
                preview.Provider.Source);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task InstallRuntimeAsync_preserves_provider_source_in_manifest()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"agent-env-manager-provider-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var provider = new ArchiveTestProvider();
            var manager = new EnvironmentManager(
                new StubEnvironmentProbe(
                    new EnvironmentProbeResult([], [])),
                manifestStore: new InMemoryManifestStore(),
                recoveryPointStore: new RecordingRecoveryPointStore(),
                operationJournal: new RecordingOperationJournal(),
                activationLink: new RecordingActivationLink(),
                healthCheck: new RecordingRuntimeHealthCheck(
                    isHealthy: true),
                managerPaths: ManagerPaths.Resolve(root),
                runtimeRoot: Path.Combine(root, "runtimes"),
                runtimeProviders: [provider],
                runtimeCommandRunner: new RecordingRuntimeCommandRunner(
                    invocation =>
                    {
                        var executable = Path.Combine(
                            invocation.WorkingDirectory,
                            "bin",
                            "tool.exe");
                        Directory.CreateDirectory(
                            Path.GetDirectoryName(executable)!);
                        File.WriteAllText(executable, string.Empty);
                        return new RuntimeCommandResult(
                            0,
                            string.Empty,
                            string.Empty);
                    }),
                artifactCache: new FixedArtifactCache(
                    Path.Combine(root, "cached-tool.exe")));

            var preview = await manager.PreviewRuntimeInstallAsync(
                provider.Descriptor.Id,
                "1.0.0");
            var installed = await manager.InstallRuntimeAsync(preview);

            Assert.Equal(
                DiscoverySourceInfo.RuntimeProvider,
                installed.Manifest.Source);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void PythonProvider_uses_mirror_without_bypassing_uv_verification()
    {
        var provider = new PythonRuntimeProvider();
        var artifact = Assert.Single(provider.Descriptor.Artifacts);
        var command = provider.CreateInstallCommand(
            new RuntimeInstallContext(
                artifact,
                Path.Combine(
                    Path.GetTempPath(),
                    "agent-env-manager-python-mirror"),
                CachedArtifactPath: null,
                MirrorUrl: "https://mirror.test/python"));

        Assert.Contains("--mirror", command.Arguments);
        Assert.Contains("https://mirror.test/python", command.Arguments);
        Assert.Equal("uv", command.Executable);
    }

    private static EnvironmentManager CreateManager()
    {
        return new EnvironmentManager(
            new StubEnvironmentProbe(
                new EnvironmentProbeResult([], [])));
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

    private sealed class ArchiveTestProvider : IRuntimeProvider
    {
        public RuntimeProviderDescriptor Descriptor { get; } = new(
            "archive-tool",
            "Archive Tool",
            EnvironmentAssetKind.ToolRuntime,
            RuntimeProviderMode.Installable,
            DiscoverySourceInfo.RuntimeProvider,
            "https://example.test/archive-tool",
            "MIT",
            RuntimeInstallStrategy.OfficialArchive,
            [
                new RuntimeArtifactDescriptor(
                    "1.0.0",
                    "https://example.test/archive-tool/1.0.0",
                    "MIT",
                    "sha-1.0.0",
                    RuntimeInstallStrategy.OfficialArchive,
                    ["win-x64"],
                    "https://example.test/archive-tool-1.0.0.zip")
            ]);

        public RuntimeInstallCommand CreateInstallCommand(
            RuntimeInstallContext context)
        {
            return new RuntimeInstallCommand(
                "archive-tool-installer",
                ["install", context.Artifact.Version],
                context.InstallRoot);
        }

        public string GetExecutableRelativePath(
            RuntimeArtifactDescriptor artifact)
        {
            return Path.Combine("bin", "tool.exe");
        }

        public IReadOnlyList<RuntimeStateFile> CreateStateFiles(
            RuntimeStateBindingContext context)
        {
            return [];
        }
    }
}
