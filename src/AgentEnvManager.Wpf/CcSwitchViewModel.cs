using System.Windows.Input;
using AgentEnvManager.Core.Agents;

namespace AgentEnvManager.Wpf;

public sealed class CcSwitchViewModel : AsyncOperationViewModel
{
    private readonly ICcSwitchClient _client;
    private CcSwitchDiscovery? _discovery;
    private CcSwitchBindingPreview? _pendingBinding;
    private string _configRoot = string.Empty;
    private string _targetCodexHome = string.Empty;
    private string _executableLabel = "未发现";
    private string _versionLabel = "未知";
    private string _settingsFilePathLabel = "未发现";
    private string _codexConfigDirectoryLabel = "未设置";
    private string _junctionsLabel = "未发现";
    private string _previewImpact = string.Empty;
    private string _preservedContentLabel = string.Empty;
    private string _pendingRecoveryPoint = string.Empty;
    private string _bindingResult = string.Empty;

    public CcSwitchViewModel(
        ICcSwitchClient client,
        Func<Task>? refreshOperations = null)
        : base(refreshOperations, "尚未发现 CC Switch。")
    {
        _client = client;
        LoadCommand = new RelayCommand(
            LoadAsync,
            () => !IsBusy,
            HandleException);
        PreviewCommand = new RelayCommand(
            PreviewAsync,
            () => !IsBusy
                && !string.IsNullOrWhiteSpace(ConfigRoot)
                && !string.IsNullOrWhiteSpace(TargetCodexHome),
            HandleException);
        ApplyCommand = new RelayCommand(
            ApplyAsync,
            () => !IsBusy && _pendingBinding is not null,
            HandleException);
    }

    public string ConfigRoot
    {
        get => _configRoot;
        set
        {
            if (SetProperty(ref _configRoot, value))
            {
                ClearPendingBinding();
                RaiseCommandStates();
            }
        }
    }

    public string TargetCodexHome
    {
        get => _targetCodexHome;
        set
        {
            if (SetProperty(ref _targetCodexHome, value))
            {
                ClearPendingBinding();
                RaiseCommandStates();
            }
        }
    }

    public string ExecutableLabel
    {
        get => _executableLabel;
        private set => SetProperty(ref _executableLabel, value);
    }

    public string VersionLabel
    {
        get => _versionLabel;
        private set => SetProperty(ref _versionLabel, value);
    }

    public string SettingsFilePathLabel
    {
        get => _settingsFilePathLabel;
        private set => SetProperty(ref _settingsFilePathLabel, value);
    }

    public string CodexConfigDirectoryLabel
    {
        get => _codexConfigDirectoryLabel;
        private set => SetProperty(ref _codexConfigDirectoryLabel, value);
    }

    public string JunctionsLabel
    {
        get => _junctionsLabel;
        private set => SetProperty(ref _junctionsLabel, value);
    }

    public string PreviewImpact
    {
        get => _previewImpact;
        private set => SetProperty(ref _previewImpact, value);
    }

    public string PreservedContentLabel
    {
        get => _preservedContentLabel;
        private set => SetProperty(ref _preservedContentLabel, value);
    }

    public string PendingRecoveryPoint
    {
        get => _pendingRecoveryPoint;
        private set => SetProperty(ref _pendingRecoveryPoint, value);
    }

    public string BindingResult
    {
        get => _bindingResult;
        private set => SetProperty(ref _bindingResult, value);
    }

    public ICommand LoadCommand { get; }

    public ICommand PreviewCommand { get; }

    public ICommand ApplyCommand { get; }

    public async Task LoadAsync()
    {
        await RunBusyAsync(async () =>
        {
            var discovery = await _client.DiscoverCcSwitchAsync();
            _discovery = discovery;
            ConfigRoot = discovery.ConfigRoot;
            ExecutableLabel = discovery.Executable ?? "未发现";
            VersionLabel = discovery.Version ?? "未知";
            SettingsFilePathLabel = discovery.SettingsFilePath;
            CodexConfigDirectoryLabel =
                discovery.CodexConfigDirectory ?? "未设置";
            JunctionsLabel = discovery.CompatibilityJunctions.Count == 0
                ? "未发现"
                : string.Join(
                    "、",
                    discovery.CompatibilityJunctions);
            if (string.IsNullOrWhiteSpace(TargetCodexHome)
                && !string.IsNullOrWhiteSpace(
                    discovery.CodexConfigDirectory))
            {
                TargetCodexHome = discovery.CodexConfigDirectory;
            }

            StatusMessage = discovery.Message;
        });
    }

    public async Task PreviewAsync()
    {
        await RunBusyAsync(async () =>
        {
            BindingResult = string.Empty;
            _pendingBinding = await _client.PreviewCcSwitchCodexConfigDirAsync(
                ConfigRoot,
                TargetCodexHome);
            PreviewImpact = _pendingBinding.Impact;
            PreservedContentLabel = string.Join(
                "、",
                _pendingBinding.PreservedContent);
            JunctionsLabel = _pendingBinding.CompatibilityJunctions.Count == 0
                ? "未发现"
                : string.Join(
                    "、",
                    _pendingBinding.CompatibilityJunctions);
            PendingRecoveryPoint =
                _pendingBinding.RecoveryPointId ?? "未记录";
            StatusMessage = _pendingBinding.IsAlreadyBound
                ? "codexConfigDir 已指向目标，仍可重新应用。"
                : "绑定预览已生成，请确认后再应用。";
            RaiseCommandStates();
        });
    }

    public async Task ApplyAsync()
    {
        if (_pendingBinding is null)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var result = await _client.ApplyCcSwitchCodexConfigDirAsync(
                _pendingBinding);
            CodexConfigDirectoryLabel =
                result.Discovery.CodexConfigDirectory ?? "未设置";
            BindingResult = string.Join(
                Environment.NewLine,
                $"绑定到: {result.Discovery.CodexConfigDirectory ?? "未设置"}",
                $"操作 ID: {result.Operation.Id}",
                $"恢复点: {result.RecoveryPoint.Id}");
            PendingRecoveryPoint = result.RecoveryPoint.Id;
            _pendingBinding = null;
            StatusMessage = "CC Switch 绑定完成。";
            RaiseCommandStates();
            await RefreshOperationsAsync();
        });
    }

    private void HandleException(Exception exception)
    {
        SetError("CC Switch 操作", exception);
    }

    protected override void RaiseCommandStates()
    {
        ((RelayCommand)LoadCommand).RaiseCanExecuteChanged();
        ((RelayCommand)PreviewCommand).RaiseCanExecuteChanged();
        ((RelayCommand)ApplyCommand).RaiseCanExecuteChanged();
    }

    private void ClearPendingBinding()
    {
        _pendingBinding = null;
        PreviewImpact = string.Empty;
        PreservedContentLabel = string.Empty;
        PendingRecoveryPoint = string.Empty;
        RaiseCommandStates();
    }
}
