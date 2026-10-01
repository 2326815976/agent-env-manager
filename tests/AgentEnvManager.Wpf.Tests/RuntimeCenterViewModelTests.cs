using AgentEnvManager.Core.Adoption;
using AgentEnvManager.Core.Inspection;
using AgentEnvManager.Core.Runtimes;
using AgentEnvManager.Wpf;

namespace AgentEnvManager.Wpf.Tests;

public sealed class RuntimeCenterViewModelTests
{
    [Fact]
    public async Task LoadProvidersAsync_populates_runtime_catalog()
    {
        var client = new StubRuntimeClient();
        var viewModel = new RuntimeCenterViewModel(client);

        await viewModel.LoadProvidersAsync();

        var provider = Assert.Single(viewModel.Providers);
        Assert.Equal("Python", provider.Name);
        Assert.Equal("python", provider.Id);
        Assert.Equal("可安装", provider.ModeLabel);
        Assert.Contains("python-build-standalone", provider.Source);
        Assert.Equal("PSF-2.0", provider.License);
        var artifact = Assert.Single(provider.Artifacts);
        Assert.Equal("3.13.7", artifact.Version);
        Assert.Equal("uv 托管下载", artifact.StrategyLabel);
        Assert.Equal("3.13.7", provider.VersionLabel);
        Assert.Equal("工具运行时", provider.KindLabel);
    }

    [Fact]
    public async Task LoadProvidersAsync_reports_managed_runtime_status()
    {
        var client = new StubRuntimeClient
        {
            Providers =
            [
                new PythonRuntimeProvider().Descriptor,
                new UvRuntimeProvider().Descriptor,
                CondaEnvironmentService.Descriptor
            ],
            ManagedStatuses =
            [
                new ManagedRuntimeStatus(
                    "python",
                    "Python",
                    IsManaged: true,
                    Version: "3.13.7",
                    Location: @"D:\AgentRuntimes\python",
                    ManagedEntryPath: @"C:\shims\python",
                    Identity: "runtime-python-3.13.7-win-x64")
            ]
        };
        var viewModel = new RuntimeCenterViewModel(client);

        await viewModel.LoadProvidersAsync();

        var python = viewModel.Providers.Single(
            provider => provider.Id == "python");
        Assert.Equal("已纳管", python.StatusLabel);
        Assert.Equal("3.13.7", python.ManagedVersionLabel);
        Assert.Equal(@"C:\shims\python", python.ManagedEntryLabel);

        var uv = viewModel.Providers.Single(
            provider => provider.Id == "uv");
        Assert.Equal("未纳管", uv.StatusLabel);
        Assert.Equal("—", uv.ManagedVersionLabel);
        Assert.Equal("—", uv.ManagedEntryLabel);

        Assert.Equal(
            "仅观测",
            viewModel.Providers.Single(
                provider => provider.Id == "conda").StatusLabel);
    }

    [Fact]
    public async Task PreviewAndInstall_uses_selection_and_refreshes_operations()
    {
        var client = new StubRuntimeClient();
        var refreshCount = 0;
        var viewModel = new RuntimeCenterViewModel(
            client,
            () =>
            {
                refreshCount++;
                return Task.CompletedTask;
            });
        await viewModel.LoadProvidersAsync();
        viewModel.SelectedProvider = Assert.Single(viewModel.Providers);
        viewModel.SelectedArtifact =
            Assert.Single(viewModel.SelectedProvider.Artifacts);
        viewModel.MirrorUrl = "https://mirror.test/python";
        viewModel.InstallRoot = @"D:\AgentRuntimes\python-3.13.7";

        await viewModel.PreviewInstallAsync();

        Assert.Equal("python", client.LastProviderId);
        Assert.Equal("3.13.7", client.LastVersion);
        Assert.Equal(
            "https://mirror.test/python",
            client.LastMirrorUrl);
        Assert.Equal(
            @"D:\AgentRuntimes\python-3.13.7",
            client.LastInstallRoot);
        Assert.Equal("安装 Python 3.13.7。", viewModel.InstallImpact);
        Assert.Equal(
            @"D:\AgentRuntimes\python-3.13.7",
            viewModel.PendingTarget);
        Assert.Equal("recovery-install", viewModel.PendingRecoveryPoint);
        Assert.Equal(@"C:\shims\python", viewModel.PendingManagedEntry);
        Assert.Equal(
            @"C:\activation\python\current",
            viewModel.PendingActivationPath);
        Assert.Contains("缓存制品", viewModel.PendingCacheStatus);

        await viewModel.InstallAsync();

        Assert.Equal(1, client.InstallCalls);
        Assert.NotNull(client.LastInstallPreview);
        Assert.Equal(1, refreshCount);
        Assert.Contains("已安装", viewModel.StatusMessage);
        Assert.Contains("已安装", viewModel.InstallResult);
        Assert.Contains(
            @"C:\shims\python",
            viewModel.InstallResult);
        Assert.Contains("operation-install", viewModel.InstallResult);
        Assert.Contains("recovery-install", viewModel.InstallResult);
    }

    [Fact]
    public async Task ImportArtifactAsync_shows_verification_and_refreshes()
    {
        var client = new StubRuntimeClient();
        var refreshCount = 0;
        var viewModel = new RuntimeCenterViewModel(
            client,
            () =>
            {
                refreshCount++;
                return Task.CompletedTask;
            });
        await viewModel.LoadProvidersAsync();
        viewModel.SelectedProvider = Assert.Single(viewModel.Providers);
        viewModel.SelectedArtifact =
            Assert.Single(viewModel.SelectedProvider.Artifacts);
        viewModel.ArtifactPath = @"D:\offline\python.tar.gz";

        await viewModel.ImportArtifactAsync();

        Assert.Equal("python", client.LastImportProviderId);
        Assert.Equal("3.13.7", client.LastImportVersion);
        Assert.Equal(
            @"D:\offline\python.tar.gz",
            client.LastImportPath);
        Assert.Contains(@"缓存路径: D:\cache\python.tar.gz", viewModel.ImportResult);
        Assert.Contains("SHA-256: official-sha256", viewModel.ImportResult);
        Assert.Contains(
            "离线导入制品通过官方 SHA-256 校验。",
            viewModel.ImportResult);
        Assert.Contains("operation-import", viewModel.ImportResult);
        Assert.Equal(1, refreshCount);
    }

    private sealed class StubRuntimeClient : StubEnvironmentManagerClient
    {
        public IReadOnlyList<RuntimeProviderDescriptor> Providers { get; init; } =
        [
            new PythonRuntimeProvider().Descriptor
        ];

        public IReadOnlyList<ManagedRuntimeStatus> ManagedStatuses
        {
            get;
            init;
        } = [];

        public override IReadOnlyList<RuntimeProviderDescriptor>
            DescribeRuntimeProviders()
        {
            return Providers;
        }

        public override Task<IReadOnlyList<ManagedRuntimeStatus>>
            DescribeManagedRuntimesAsync(
                CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ManagedStatuses);
        }

        public string? LastProviderId { get; private set; }

        public string? LastVersion { get; private set; }

        public string? LastMirrorUrl { get; private set; }

        public RuntimeInstallPreview? LastInstallPreview { get; private set; }

        public int InstallCalls { get; private set; }

        public string? LastInstallRoot { get; private set; }

        public override Task<RuntimeInstallPreview>
            PreviewRuntimeInstallAsync(
                string providerId,
                string version,
                string? mirrorUrl,
                string? installRoot,
                CancellationToken cancellationToken = default)
        {
            LastProviderId = providerId;
            LastVersion = version;
            LastMirrorUrl = mirrorUrl;
            LastInstallRoot = installRoot;
            var provider = Providers.Single(item => item.Id == providerId);
            var artifact = provider.Artifacts.Single(
                item => item.Version == version);
            LastInstallPreview = new RuntimeInstallPreview(
                provider,
                artifact,
                new Core.Adoption.EnvironmentIdentity(
                    "runtime-python-3.13.7-win-x64"),
                new Core.Adoption.EnvironmentFingerprint(
                    "runtime-python-3.13.7-win-x64"),
                installRoot ?? @"D:\runtimes\python\3.13.7",
                (installRoot ?? @"D:\runtimes\python\3.13.7") + @"\python",
                (installRoot ?? @"D:\runtimes\python\3.13.7") +
                @"\python\python.exe",
                "python-activation",
                @"C:\activation\python\current",
                @"C:\shims\python",
                "安装 Python 3.13.7。",
                IsAlreadyInstalled: false,
                OperationId: "operation-install",
                RecoveryPointId: "recovery-install",
                MirrorUrl: mirrorUrl);
            return Task.FromResult(LastInstallPreview);
        }

        public override Task<InstalledRuntime> InstallRuntimeAsync(
            RuntimeInstallPreview preview,
            CancellationToken cancellationToken = default)
        {
            InstallCalls++;
            LastInstallPreview = preview;
            return Task.FromResult(new InstalledRuntime(
                preview.Provider,
                preview.Artifact,
                new Core.Adoption.EnvironmentManifest(
                    preview.Identity,
                    preview.Fingerprint,
                    preview.Provider.Kind,
                    preview.Provider.Name,
                    preview.Artifact.Version,
                    preview.Provider.Source,
                    preview.Location,
                    preview.StableActivationPath,
                    preview.Artifact.Sha256,
                    preview.RecoveryPointId!,
                    preview.OperationId!,
                    IsSystemComponent: false,
                    DateTimeOffset.UnixEpoch,
                    preview.ActivationIdentity,
                    preview.ManagedEntryPath),
                preview.ExecutablePath,
                preview.ManagedEntryPath));
        }

        public string? LastImportProviderId { get; private set; }

        public string? LastImportVersion { get; private set; }

        public string? LastImportPath { get; private set; }

        public override Task<RuntimeArtifactCacheEntry>
            ImportRuntimeArtifactAsync(
                string providerId,
                string version,
                string sourcePath,
                CancellationToken cancellationToken = default)
        {
            LastImportProviderId = providerId;
            LastImportVersion = version;
            LastImportPath = sourcePath;
            return Task.FromResult(new RuntimeArtifactCacheEntry(
                @"D:\cache\python.tar.gz",
                "official-sha256",
                RuntimeArtifactSource.Imported,
                sourcePath,
                null,
                "离线导入制品通过官方 SHA-256 校验。",
                "operation-import"));
        }
    }
}
