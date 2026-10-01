using System.Security.Cryptography;
using System.Text;
using AgentEnvManager.Core.Inspection;
using AgentEnvManager.Core.Operations;
using AgentEnvManager.Core.Runtimes;
using AgentEnvManager.Core.Storage;
using AgentEnvManager.Core.Tests.TestSupport;

namespace AgentEnvManager.Core.Tests.Runtimes;

public sealed class RuntimeArtifactCacheTests
{
    [Fact]
    public async Task AcquireAsync_downloads_verifies_and_reuses_cache()
    {
        var root = CreateTempRoot();
        try
        {
            var bytes = Encoding.UTF8.GetBytes("verified artifact");
            var artifact = CreateArtifact(bytes);
            var downloader = new RecordingArtifactDownloader(url =>
                url == artifact.DownloadUrl
                    ? bytes
                    : throw new InvalidOperationException("unexpected url"));
            var cache = new RuntimeArtifactCache(root, downloader);

            var first = await cache.AcquireAsync(artifact, mirrorUrl: null);
            var second = await cache.AcquireAsync(artifact, mirrorUrl: null);

            Assert.Equal(RuntimeArtifactSource.Official, first.Source);
            Assert.Equal(RuntimeArtifactSource.Cache, second.Source);
            Assert.Equal(first.Path, second.Path);
            Assert.True(File.Exists(first.Path));
            Assert.Single(downloader.Urls);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task AcquireAsync_rejects_mirror_that_fails_official_hash()
    {
        var root = CreateTempRoot();
        try
        {
            var artifact = CreateArtifact(
                Encoding.UTF8.GetBytes("expected artifact"));
            var downloader = new RecordingArtifactDownloader(url =>
                Encoding.UTF8.GetBytes("tampered artifact"));
            var cache = new RuntimeArtifactCache(root, downloader);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => cache.AcquireAsync(
                    artifact,
                    "https://mirror.test/tool.zip"));

            Assert.Contains("哈希不匹配", exception.Message);
            Assert.Equal(
                ["https://mirror.test/tool.zip"],
                downloader.Urls);
            Assert.Empty(Directory.GetFiles(root, "*", SearchOption.AllDirectories));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ImportAsync_supports_offline_reuse_without_downloader()
    {
        var root = CreateTempRoot();
        try
        {
            var bytes = Encoding.UTF8.GetBytes("offline artifact");
            var artifact = CreateArtifact(bytes);
            var sourcePath = Path.Combine(root, "import.zip");
            await File.WriteAllBytesAsync(sourcePath, bytes);
            var downloader = new RecordingArtifactDownloader(_ =>
                throw new InvalidOperationException("network must not be used"));
            var cache = new RuntimeArtifactCache(
                Path.Combine(root, "cache"),
                downloader);

            var imported = await cache.ImportAsync(artifact, sourcePath);
            var acquired = await cache.AcquireAsync(artifact, mirrorUrl: null);

            Assert.Equal(RuntimeArtifactSource.Imported, imported.Source);
            Assert.Equal(RuntimeArtifactSource.Cache, acquired.Source);
            Assert.Equal(imported.Path, acquired.Path);
            Assert.Empty(downloader.Urls);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Imported_artifact_is_reused_by_install_and_recorded()
    {
        var root = CreateTempRoot();
        try
        {
            var bytes = Encoding.UTF8.GetBytes("offline install artifact");
            var artifact = CreateArtifact(bytes);
            var provider = new TestArchiveProvider(artifact);
            var sourcePath = Path.Combine(root, "import.zip");
            await File.WriteAllBytesAsync(sourcePath, bytes);
            var downloader = new RecordingArtifactDownloader(_ =>
                throw new InvalidOperationException("network must not be used"));
            var cache = new RuntimeArtifactCache(
                Path.Combine(root, "cache"),
                downloader);
            var journal = new RecordingOperationJournal();
            var runner = new RecordingRuntimeCommandRunner(invocation =>
            {
                var executable = Path.Combine(
                    invocation.WorkingDirectory,
                    "bin",
                    "tool.exe");
                Directory.CreateDirectory(
                    Path.GetDirectoryName(executable)!);
                File.WriteAllText(executable, string.Empty);
                return new RuntimeCommandResult(0, string.Empty, string.Empty);
            });
            var manager = new EnvironmentManager(
                new StubEnvironmentProbe(
                    new EnvironmentProbeResult([], [])),
                manifestStore: new InMemoryManifestStore(),
                recoveryPointStore: new RecordingRecoveryPointStore(),
                operationJournal: journal,
                activationLink: new RecordingActivationLink(),
                healthCheck: new RecordingRuntimeHealthCheck(
                    isHealthy: true),
                managerPaths: ManagerPaths.Resolve(root),
                runtimeRoot: Path.Combine(root, "runtimes"),
                runtimeProviders: [provider],
                runtimeCommandRunner: runner,
                artifactCache: cache);

            var imported = await manager.ImportRuntimeArtifactAsync(
                provider.Descriptor.Id,
                "1.0.0",
                sourcePath);
            var preview = await manager.PreviewRuntimeInstallAsync(
                provider.Descriptor.Id,
                "1.0.0");
            var installed = await manager.InstallRuntimeAsync(preview);
            var operation = Assert.Single(
                journal.History
                    .Where(record =>
                        record.Id == preview.OperationId)
                    .GroupBy(record => record.Id)
                    .Select(group => group.Last()));

            Assert.Equal(
                imported.Path,
                provider.LastContext?.CachedArtifactPath);
            var importOperation = Assert.Single(
                journal.History
                    .Where(record =>
                        record.Id == imported.OperationId)
                    .GroupBy(record => record.Id)
                    .Select(group => group.Last()));
            Assert.Equal(
                OperationType.ArtifactImport,
                importOperation.Type);
            Assert.Equal(
                OperationState.Succeeded,
                importOperation.State);
            Assert.Equal(imported.Path, importOperation.ArtifactCachePath);
            Assert.Equal(artifact.Sha256, importOperation.ArtifactSha256);
            Assert.Equal("Cache", operation.ArtifactSource);
            Assert.Equal(imported.Path, operation.ArtifactCachePath);
            Assert.Equal(artifact.Sha256, operation.ArtifactSha256);
            Assert.Contains(
                "缓存",
                operation.VerificationResult,
                StringComparison.Ordinal);
            Assert.Empty(downloader.Urls);
            Assert.Equal("Test Tool", installed.Manifest.Name);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task InstallAsync_records_mirror_verification_failure()
    {
        var root = CreateTempRoot();
        try
        {
            var artifact = CreateArtifact(
                Encoding.UTF8.GetBytes("expected artifact"));
            var provider = new TestArchiveProvider(artifact);
            var journal = new RecordingOperationJournal();
            var manager = new EnvironmentManager(
                new StubEnvironmentProbe(
                    new EnvironmentProbeResult([], [])),
                manifestStore: new InMemoryManifestStore(),
                recoveryPointStore: new RecordingRecoveryPointStore(),
                operationJournal: journal,
                activationLink: new RecordingActivationLink(),
                healthCheck: new RecordingRuntimeHealthCheck(
                    isHealthy: true),
                managerPaths: ManagerPaths.Resolve(root),
                runtimeRoot: Path.Combine(root, "runtimes"),
                runtimeProviders: [provider],
                runtimeCommandRunner: new RecordingRuntimeCommandRunner(_ =>
                    new RuntimeCommandResult(0, string.Empty, string.Empty)),
                artifactCache: new FailingArtifactCache(
                    "镜像制品哈希不匹配"));
            const string mirrorUrl = "https://mirror.test/tool.zip";
            var preview = await manager.PreviewRuntimeInstallAsync(
                provider.Descriptor.Id,
                "1.0.0",
                mirrorUrl);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => manager.InstallRuntimeAsync(preview));

            var operation = Assert.Single(
                journal.History
                    .Where(record =>
                        record.Id == preview.OperationId)
                    .GroupBy(record => record.Id)
                    .Select(group => group.Last()));
            Assert.Contains(
                "哈希不匹配",
                operation.FailureReason,
                StringComparison.Ordinal);
            Assert.Equal("Mirror", operation.ArtifactSource);
            Assert.Equal(mirrorUrl, operation.MirrorUrl);
            Assert.Contains(
                "校验失败",
                operation.VerificationResult,
                StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Python_uses_cached_archive_offline_when_imported()
    {
        var root = CreateTempRoot();
        try
        {
            var provider = new PythonRuntimeProvider();
            var cachedArtifactPath = Path.Combine(
                root,
                "cache",
                "python.tar.gz");
            var journal = new RecordingOperationJournal();
            var runner = new RecordingRuntimeCommandRunner(invocation =>
            {
                Assert.Equal("tar.exe", invocation.Executable);
                Assert.Contains(
                    cachedArtifactPath,
                    invocation.Arguments);
                var executable = Path.Combine(
                    invocation.WorkingDirectory,
                    "python",
                    "python.exe");
                Directory.CreateDirectory(
                    Path.GetDirectoryName(executable)!);
                File.WriteAllText(executable, string.Empty);
                return new RuntimeCommandResult(0, string.Empty, string.Empty);
            });
            var manager = new EnvironmentManager(
                new StubEnvironmentProbe(
                    new EnvironmentProbeResult([], [])),
                manifestStore: new InMemoryManifestStore(),
                recoveryPointStore: new RecordingRecoveryPointStore(),
                operationJournal: journal,
                activationLink: new RecordingActivationLink(),
                healthCheck: new RecordingRuntimeHealthCheck(
                    isHealthy: true),
                managerPaths: ManagerPaths.Resolve(root),
                runtimeRoot: Path.Combine(root, "runtimes"),
                runtimeProviders: [provider],
                runtimeCommandRunner: runner,
                artifactCache: new FixedArtifactCache(cachedArtifactPath));

            var preview = await manager.PreviewRuntimeInstallAsync(
                provider.Descriptor.Id,
                "3.13.7");
            var installed = await manager.InstallRuntimeAsync(preview);

            Assert.True(preview.IsCachedArtifactAvailable);
            Assert.EndsWith(
                Path.Combine("python", "python.exe"),
                installed.ExecutablePath);
            Assert.EndsWith(
                "python",
                installed.Manifest.Location);
            var operation = Assert.Single(
                journal.History
                    .Where(record =>
                        record.Id == preview.OperationId)
                    .GroupBy(record => record.Id)
                    .Select(group => group.Last()));
            Assert.Equal("Cache", operation.ArtifactSource);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static RuntimeArtifactDescriptor CreateArtifact(byte[] bytes)
    {
        var hash = Convert.ToHexString(SHA256.HashData(bytes))
            .ToLowerInvariant();
        return new RuntimeArtifactDescriptor(
            "1.0.0",
            "https://official.test/tool/1.0.0",
            "MIT",
            hash,
            RuntimeInstallStrategy.OfficialArchive,
            ["win-x64"],
            "https://official.test/tool-1.0.0.zip");
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"agent-env-manager-artifact-cache-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    private sealed class RecordingArtifactDownloader(
        Func<string, byte[]> respond) : IArtifactDownloader
    {
        public List<string> Urls { get; } = [];

        public Task<Stream> OpenReadAsync(
            string url,
            CancellationToken cancellationToken = default)
        {
            Urls.Add(url);
            return Task.FromResult<Stream>(
                new MemoryStream(respond(url)));
        }
    }

    private sealed class TestArchiveProvider(
        RuntimeArtifactDescriptor artifact) : IRuntimeProvider
    {
        public RuntimeProviderDescriptor Descriptor { get; } = new(
            "test-archive",
            "Test Tool",
            EnvironmentAssetKind.ToolRuntime,
            RuntimeProviderMode.Installable,
            DiscoverySourceInfo.RuntimeProvider,
            "https://official.test/tool/1.0.0",
            "MIT",
            RuntimeInstallStrategy.OfficialArchive,
            [artifact]);

        public RuntimeInstallContext? LastContext { get; private set; }

        public RuntimeInstallCommand CreateInstallCommand(
            RuntimeInstallContext context)
        {
            LastContext = context;
            return new RuntimeInstallCommand(
                "test-installer",
                ["install", context.CachedArtifactPath ?? string.Empty],
                context.InstallRoot);
        }

        public string GetExecutableRelativePath(
            RuntimeArtifactDescriptor artifact,
            bool fromCache = false)
        {
            return Path.Combine("bin", "tool.exe");
        }

        public IReadOnlyList<RuntimeStateFile> CreateStateFiles(
            RuntimeStateBindingContext context)
        {
            return [];
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
