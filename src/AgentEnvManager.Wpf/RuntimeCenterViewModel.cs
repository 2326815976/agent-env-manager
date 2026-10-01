using System.Collections.ObjectModel;
using System.Windows.Input;
using AgentEnvManager.Core.Runtimes;

namespace AgentEnvManager.Wpf;

public sealed class RuntimeCenterViewModel : AsyncOperationViewModel
{
    private readonly IRuntimeCenterClient _client;
    private RuntimeProviderRowViewModel? _selectedProvider;
    private RuntimeArtifactRowViewModel? _selectedArtifact;
    private RuntimeInstallPreview? _pendingInstall;
    private string _mirrorUrl = string.Empty;
    private string _installImpact = string.Empty;
    private string _pendingTarget = string.Empty;
    private string _pendingRecoveryPoint = string.Empty;
    private string _artifactPath = string.Empty;
    private string _importResult = string.Empty;

    public RuntimeCenterViewModel(
        IRuntimeCenterClient client,
        Func<Task>? refreshOperations = null)
        : base(refreshOperations, "尚未加载运行时提供者。")
    {
        _client = client;
        LoadProvidersCommand = new RelayCommand(
            LoadProvidersAsync,
            () => !IsBusy,
            HandleException);
        PreviewInstallCommand = new RelayCommand(
            PreviewInstallAsync,
            () => !IsBusy && SelectedArtifact is not null,
            HandleException);
        InstallCommand = new RelayCommand(
            InstallAsync,
            () => !IsBusy && _pendingInstall is not null,
            HandleException);
        ImportArtifactCommand = new RelayCommand(
            ImportArtifactAsync,
            () =>
                !IsBusy
                && SelectedArtifact is not null
                && !string.IsNullOrWhiteSpace(ArtifactPath),
            HandleException);
    }

    public ObservableCollection<RuntimeProviderRowViewModel> Providers
    { get; } = [];

    public RuntimeProviderRowViewModel? SelectedProvider
    {
        get => _selectedProvider;
        set
        {
            if (SetProperty(ref _selectedProvider, value))
            {
                SelectedArtifact = value?.Artifacts.FirstOrDefault();
                ClearPendingInstall();
            }
        }
    }

    public RuntimeArtifactRowViewModel? SelectedArtifact
    {
        get => _selectedArtifact;
        set
        {
            if (SetProperty(ref _selectedArtifact, value))
            {
                ClearPendingInstall();
                RaiseCommandStates();
            }
        }
    }

    public string MirrorUrl
    {
        get => _mirrorUrl;
        set
        {
            if (SetProperty(ref _mirrorUrl, value))
            {
                ClearPendingInstall();
            }
        }
    }

    public string InstallImpact
    {
        get => _installImpact;
        private set => SetProperty(ref _installImpact, value);
    }

    public string PendingTarget
    {
        get => _pendingTarget;
        private set => SetProperty(ref _pendingTarget, value);
    }

    public string PendingRecoveryPoint
    {
        get => _pendingRecoveryPoint;
        private set => SetProperty(ref _pendingRecoveryPoint, value);
    }

    public string ArtifactPath
    {
        get => _artifactPath;
        set
        {
            if (SetProperty(ref _artifactPath, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public string ImportResult
    {
        get => _importResult;
        private set => SetProperty(ref _importResult, value);
    }

    public ICommand LoadProvidersCommand { get; }

    public ICommand PreviewInstallCommand { get; }

    public ICommand InstallCommand { get; }

    public ICommand ImportArtifactCommand { get; }

    public Task LoadProvidersAsync()
    {
        return RunBusyAsync(() =>
        {
            Providers.Replace(
                _client.DescribeRuntimeProviders()
                    .Select(provider =>
                        new RuntimeProviderRowViewModel(provider)));
            StatusMessage =
                $"已加载 {Providers.Count} 个运行时提供者。";
            return Task.CompletedTask;
        });
    }

    public async Task PreviewInstallAsync()
    {
        if (SelectedProvider is null || SelectedArtifact is null)
        {
            throw new InvalidOperationException(
                "请选择运行时提供者和版本。");
        }

        await RunBusyAsync(async () =>
        {
            _pendingInstall = await _client.PreviewRuntimeInstallAsync(
                SelectedProvider.Id,
                SelectedArtifact.Version,
                string.IsNullOrWhiteSpace(MirrorUrl)
                    ? null
                    : MirrorUrl);
            InstallImpact = _pendingInstall.Impact;
            PendingTarget = _pendingInstall.InstallRoot;
            PendingRecoveryPoint =
                _pendingInstall.RecoveryPointId ?? "未记录";
            StatusMessage = "安装预览已生成，请确认影响范围。";
        });
    }

    public async Task InstallAsync()
    {
        if (_pendingInstall is null)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var installed = await _client.InstallRuntimeAsync(
                _pendingInstall);
            StatusMessage =
                $"{installed.Provider.Name} " +
                $"{installed.Artifact.Version} 已安装。";
            _pendingInstall = null;
            ClearPendingInstall();
            await RefreshOperationsAsync();
        });
    }

    public async Task ImportArtifactAsync()
    {
        if (SelectedProvider is null || SelectedArtifact is null)
        {
            throw new InvalidOperationException(
                "请选择运行时提供者和版本。");
        }

        if (string.IsNullOrWhiteSpace(ArtifactPath))
        {
            throw new InvalidOperationException(
                "请选择要导入的本地制品。");
        }

        await RunBusyAsync(async () =>
        {
            var imported = await _client.ImportRuntimeArtifactAsync(
                SelectedProvider.Id,
                SelectedArtifact.Version,
                ArtifactPath);
            ImportResult = string.Join(
                Environment.NewLine,
                $"缓存路径: {imported.Path}",
                $"SHA-256: {imported.Sha256}",
                $"校验: {imported.VerificationResult}",
                $"操作 ID: {imported.OperationId ?? "未记录"}");
            StatusMessage = "离线制品已导入并校验。";
            await RefreshOperationsAsync();
        });
    }

    private void HandleException(Exception exception)
    {
        SetError("运行时操作", exception);
    }

    protected override void RaiseCommandStates()
    {
        ((RelayCommand)LoadProvidersCommand).RaiseCanExecuteChanged();
        ((RelayCommand)PreviewInstallCommand).RaiseCanExecuteChanged();
        ((RelayCommand)InstallCommand).RaiseCanExecuteChanged();
        ((RelayCommand)ImportArtifactCommand).RaiseCanExecuteChanged();
    }

    private void ClearPendingInstall()
    {
        _pendingInstall = null;
        InstallImpact = string.Empty;
        PendingTarget = string.Empty;
        PendingRecoveryPoint = string.Empty;
        RaiseCommandStates();
    }
}

public sealed class RuntimeProviderRowViewModel(
    RuntimeProviderDescriptor provider)
{
    public string Id { get; } = provider.Id;

    public string Name { get; } = provider.Name;

    public string ModeLabel { get; } = provider.Mode switch
    {
        RuntimeProviderMode.Installable => "可安装",
        RuntimeProviderMode.ObservedOnly => "仅观测",
        _ => provider.Mode.ToString()
    };

    public string Source { get; } = provider.OfficialSource;

    public string License { get; } = provider.License;

    public IReadOnlyList<RuntimeArtifactRowViewModel> Artifacts { get; } =
        provider.Artifacts
            .Select(artifact => new RuntimeArtifactRowViewModel(artifact))
            .ToArray();
}

public sealed record RuntimeArtifactRowViewModel(
    RuntimeArtifactDescriptor Artifact)
{
    public string Version { get; } = Artifact.Version;

    public string StrategyLabel { get; } = Artifact.InstallStrategy switch
    {
        RuntimeInstallStrategy.UvManagedDownload => "uv 托管下载",
        RuntimeInstallStrategy.OfficialArchive => "官方存档",
        RuntimeInstallStrategy.ObservedRebuild => "仅观测重建",
        _ => Artifact.InstallStrategy.ToString()
    };

    public string Sha256 { get; } = Artifact.Sha256;

    public string DownloadUrl { get; } = Artifact.DownloadUrl;
}
