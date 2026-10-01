using System.IO;
using System.Collections.ObjectModel;
using System.Windows.Input;
using AgentEnvManager.Core.Agents;

namespace AgentEnvManager.Wpf;

public sealed class AgentBindingViewModel : AsyncOperationViewModel
{
    private readonly IAgentDiscoveryClient _discoveryClient;
    private readonly IAgentBindingClient _bindingClient;
    private readonly IFileSystemPicker? _filePicker;
    private string? _selectedAgent;
    private string _configurationDirectory = string.Empty;
    private string _executable = string.Empty;
    private string _managedEntryPath = string.Empty;
    private string _runtimeName = string.Empty;
    private string _runtimeVersion = string.Empty;
    private string _workspacePath = string.Empty;
    private string _runtimeCommand = string.Empty;
    private AgentBindingPlan? _pendingPlan;
    private AgentBinding? _boundBinding;
    private string _discoveryResult = "尚未发现 Agent。";
    private string _bindingFilePath = string.Empty;
    private string _recoveryPointInformation = string.Empty;
    private string _bindingSummary = string.Empty;
    private string _healthResult = "尚未执行健康检查。";
    private string _recoveryResult = string.Empty;
    private RuntimeSelectionOption? _selectedRuntimeOption;

    public AgentBindingViewModel(
        IAgentDiscoveryClient discoveryClient,
        IAgentBindingClient bindingClient,
        IRuntimeCatalogClient runtimeCatalog,
        IFileSystemPicker? filePicker = null,
        Func<Task>? refreshOperations = null)
        : base(refreshOperations, "请选择 Agent 并填写绑定参数。")
    {
        _discoveryClient = discoveryClient;
        _bindingClient = bindingClient;
        _filePicker = filePicker;
        AvailableAgents = new ObservableCollection<string>(
            discoveryClient.DescribeAgentAdapters());
        AvailableRuntimeOptions =
            new ObservableCollection<RuntimeSelectionOption>(
                runtimeCatalog.DescribeRuntimeProviders()
                    .SelectMany(provider =>
                        provider.Artifacts.Select(artifact =>
                            new RuntimeSelectionOption(
                                provider.Name,
                                artifact.Version))));
        _selectedAgent = AvailableAgents.FirstOrDefault();
        DiscoverCommand = new RelayCommand(
            DiscoverAgentAsync,
            () => !IsBusy && !string.IsNullOrWhiteSpace(SelectedAgent),
            HandleException);
        PreviewBindingCommand = new RelayCommand(
            PreviewBindingAsync,
            () => !IsBusy && HasBindingInputs(),
            HandleException);
        BindCommand = new RelayCommand(
            BindAsync,
            () => !IsBusy && _pendingPlan is not null,
            HandleException);
        HealthCheckCommand = new RelayCommand(
            HealthCheckAsync,
            () => !IsBusy && _boundBinding is not null,
            HandleException);
        SelectConfigurationDirectoryCommand = new RelayCommand(
            SelectConfigurationDirectoryAsync,
            () => !IsBusy && _filePicker is not null,
            HandleException);
        SelectExecutableCommand = new RelayCommand(
            SelectExecutableAsync,
            () => !IsBusy && _filePicker is not null,
            HandleException);
        SelectWorkspaceCommand = new RelayCommand(
            SelectWorkspaceAsync,
            () => !IsBusy && _filePicker is not null,
            HandleException);
    }

    public ObservableCollection<string> AvailableAgents { get; }

    public ObservableCollection<RuntimeSelectionOption> AvailableRuntimeOptions
    { get; }

    public string? SelectedAgent
    {
        get => _selectedAgent;
        set
        {
            if (SetProperty(ref _selectedAgent, value))
            {
                DiscoveryResult = "尚未发现 Agent。";
                ClearPendingBinding();
            }
        }
    }

    public string ConfigurationDirectory
    {
        get => _configurationDirectory;
        set
        {
            if (SetProperty(ref _configurationDirectory, value))
            {
                ClearPendingBinding();
            }
        }
    }

    public string Executable
    {
        get => _executable;
        set
        {
            if (SetProperty(ref _executable, value))
            {
                ClearPendingBinding();
            }
        }
    }

    public string ManagedEntryPath
    {
        get => _managedEntryPath;
        set
        {
            if (SetProperty(ref _managedEntryPath, value))
            {
                ClearPendingBinding();
            }
        }
    }

    public string RuntimeName
    {
        get => _runtimeName;
        set
        {
            if (SetProperty(ref _runtimeName, value))
            {
                ClearPendingBinding();
            }
        }
    }

    public string RuntimeVersion
    {
        get => _runtimeVersion;
        set
        {
            if (SetProperty(ref _runtimeVersion, value))
            {
                ClearPendingBinding();
            }
        }
    }

    public string WorkspacePath
    {
        get => _workspacePath;
        set
        {
            if (SetProperty(ref _workspacePath, value))
            {
                ClearPendingBinding();
            }
        }
    }

    public string RuntimeCommand
    {
        get => _runtimeCommand;
        set
        {
            if (SetProperty(ref _runtimeCommand, value))
            {
                ClearPendingBinding();
            }
        }
    }

    public RuntimeSelectionOption? SelectedRuntimeOption
    {
        get => _selectedRuntimeOption;
        set
        {
            if (SetProperty(ref _selectedRuntimeOption, value)
                && value is not null)
            {
                RuntimeName = value.ProviderName;
                RuntimeVersion = value.Version;
            }
        }
    }

    public string DiscoveryResult
    {
        get => _discoveryResult;
        private set => SetProperty(ref _discoveryResult, value);
    }

    public string BindingFilePath
    {
        get => _bindingFilePath;
        private set => SetProperty(ref _bindingFilePath, value);
    }

    public string RecoveryPointInformation
    {
        get => _recoveryPointInformation;
        private set => SetProperty(ref _recoveryPointInformation, value);
    }

    public string BindingSummary
    {
        get => _bindingSummary;
        private set => SetProperty(ref _bindingSummary, value);
    }

    public string HealthResult
    {
        get => _healthResult;
        private set => SetProperty(ref _healthResult, value);
    }

    public string RecoveryResult
    {
        get => _recoveryResult;
        private set => SetProperty(ref _recoveryResult, value);
    }

    public ICommand DiscoverCommand { get; }

    public ICommand PreviewBindingCommand { get; }

    public ICommand BindCommand { get; }

    public ICommand HealthCheckCommand { get; }

    public ICommand SelectConfigurationDirectoryCommand { get; }

    public ICommand SelectExecutableCommand { get; }

    public ICommand SelectWorkspaceCommand { get; }

    public async Task DiscoverAgentAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedAgent))
        {
            throw new InvalidOperationException("请选择 Agent。");
        }

        await RunBusyAsync(async () =>
        {
            var discovery = await _discoveryClient.DiscoverAgentAsync(
                SelectedAgent,
                new AgentDiscoveryRequest(
                    ConfigurationDirectory,
                    Executable));
            if (!string.IsNullOrWhiteSpace(discovery.Home))
            {
                ConfigurationDirectory = discovery.Home;
            }

            if (!string.IsNullOrWhiteSpace(discovery.Executable))
            {
                Executable = discovery.Executable;
            }

            DiscoveryResult = string.Join(
                Environment.NewLine,
                $"已安装: {(discovery.IsInstalled ? "是" : "否")}",
                $"Agent 配置环境: {discovery.Home ?? "未发现"}",
                $"配置来源: {discovery.HomeSource ?? "未知"}",
                $"可执行文件: {discovery.Executable ?? "未发现"}",
                $"内置 Codex: {discovery.BundledCodexPath ?? "未发现"}",
                $"app-server 入口: {discovery.BundledCodexPath ?? "未发现"}",
                $"兼容 Junction: {FormatJunctions(discovery)}",
                $"绑定状态: {(discovery.IsBound ? "已绑定" : "未绑定")}",
                $"绑定文件: {discovery.BindingFilePath ?? "未记录"}",
                discovery.Message);
            StatusMessage = "Agent 发现完成。";
        });
    }

    public async Task PreviewBindingAsync()
    {
        if (!HasBindingInputs())
        {
            throw new InvalidOperationException(
                "请选择 Agent，并填写 Agent 配置环境、可执行文件、受管入口和运行时版本。");
        }

        await RunBusyAsync(async () =>
        {
            _pendingPlan = await _bindingClient.CreateAgentBindingPlanAsync(
                SelectedAgent!,
                new AgentBindingRequest(
                    ConfigurationDirectory,
                    Executable,
                    ManagedEntryPath,
                    RuntimeName,
                    RuntimeVersion,
                    WorkspacePath: string.IsNullOrWhiteSpace(WorkspacePath)
                        ? null
                        : WorkspacePath,
                    RuntimeCommand: string.IsNullOrWhiteSpace(RuntimeCommand)
                        ? null
                        : RuntimeCommand));
            BindingFilePath = _pendingPlan.BindingFilePath;
            RecoveryPointInformation =
                $"配置恢复点将保存原绑定文件状态: " +
                _pendingPlan.BindingFilePath;
            BindingSummary =
                $"{_pendingPlan.AgentName} 使用 " +
                $"{_pendingPlan.RuntimeName} " +
                $"{_pendingPlan.RuntimeVersion}，受管入口 " +
                $"{_pendingPlan.ManagedEntryPath}。";
            StatusMessage = "Agent 绑定预览已生成，请确认配置恢复点信息。";
        });
    }

    public async Task BindAsync()
    {
        if (_pendingPlan is null)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            try
            {
                _boundBinding = await _bindingClient.BindAgentAsync(
                    SelectedAgent!,
                    _pendingPlan);
                RecoveryResult = _boundBinding.RecoveryPoint.Id;
                HealthResult = _boundBinding.IsHealthy
                    ? "绑定验证通过。"
                    : "绑定验证未返回健康状态。";
                StatusMessage = "Agent 绑定完成。";
                _pendingPlan = null;
                RaiseCommandStates();
                await RefreshOperationsAsync();
            }
            catch (Exception exception)
            {
                RecoveryResult = exception.Message;
                StatusMessage = $"Agent 绑定失败：{exception.Message}";
            }
        });
    }

    public async Task HealthCheckAsync()
    {
        if (_boundBinding is null)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var health = await _bindingClient.CheckAgentHealthAsync(
                SelectedAgent!,
                _boundBinding);
            HealthResult = health.IsHealthy
                ? $"健康: {health.Message}"
                : $"不健康: {health.Message}";
            StatusMessage = health.IsHealthy
                ? "Agent 健康检查通过。"
                : "Agent 健康检查失败。";
        });
    }

    public Task SelectConfigurationDirectoryAsync()
    {
        var selected = _filePicker?.SelectFolder(
            "选择 Agent 配置环境",
            ConfigurationDirectory);
        if (!string.IsNullOrWhiteSpace(selected))
        {
            ConfigurationDirectory = selected;
        }

        return Task.CompletedTask;
    }

    public Task SelectExecutableAsync()
    {
        var selected = _filePicker?.SelectFile(
            "选择 Agent 可执行文件",
            "可执行文件|*.exe;*.cmd;*.bat|所有文件|*.*",
            Path.GetDirectoryName(Executable));
        if (!string.IsNullOrWhiteSpace(selected))
        {
            Executable = selected;
        }

        return Task.CompletedTask;
    }

    public Task SelectWorkspaceAsync()
    {
        var selected = _filePicker?.SelectFolder(
            "选择工作区",
            WorkspacePath);
        if (!string.IsNullOrWhiteSpace(selected))
        {
            WorkspacePath = selected;
        }

        return Task.CompletedTask;
    }

    private bool HasBindingInputs()
    {
        return !string.IsNullOrWhiteSpace(SelectedAgent)
            && !string.IsNullOrWhiteSpace(ConfigurationDirectory)
            && !string.IsNullOrWhiteSpace(Executable)
            && !string.IsNullOrWhiteSpace(ManagedEntryPath)
            && !string.IsNullOrWhiteSpace(RuntimeName)
            && !string.IsNullOrWhiteSpace(RuntimeVersion);
    }

    private static string FormatJunctions(AgentDiscoveryResult discovery)
    {
        return discovery.CompatibilityJunctionPaths is { Count: > 0 }
            ? string.Join("、", discovery.CompatibilityJunctionPaths)
            : "未发现";
    }

    private void HandleException(Exception exception)
    {
        SetError("Agent 绑定操作", exception);
    }

    protected override void RaiseCommandStates()
    {
        ((RelayCommand)DiscoverCommand).RaiseCanExecuteChanged();
        ((RelayCommand)PreviewBindingCommand).RaiseCanExecuteChanged();
        ((RelayCommand)BindCommand).RaiseCanExecuteChanged();
        ((RelayCommand)HealthCheckCommand).RaiseCanExecuteChanged();
        ((RelayCommand)SelectConfigurationDirectoryCommand)
            .RaiseCanExecuteChanged();
        ((RelayCommand)SelectExecutableCommand).RaiseCanExecuteChanged();
        ((RelayCommand)SelectWorkspaceCommand).RaiseCanExecuteChanged();
    }

    private void ClearPendingBinding()
    {
        _pendingPlan = null;
        _boundBinding = null;
        BindingFilePath = string.Empty;
        RecoveryPointInformation = string.Empty;
        BindingSummary = string.Empty;
        RecoveryResult = string.Empty;
        HealthResult = "尚未执行健康检查。";
        RaiseCommandStates();
    }
}

public sealed record RuntimeSelectionOption(
    string ProviderName,
    string Version)
{
    public string DisplayName => $"{ProviderName} {Version}";
}
