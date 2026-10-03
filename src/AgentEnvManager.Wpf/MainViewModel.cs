using System.Collections.ObjectModel;
using System.Windows.Input;
using AgentEnvManager.Core.Activation;
using AgentEnvManager.Core.Adoption;
using AgentEnvManager.Core.Deletion;
using AgentEnvManager.Core.Inspection;

namespace AgentEnvManager.Wpf;

public sealed class MainViewModel : ObservableObject
{
    private readonly IEnvironmentManagerClient _client;
    private EnvironmentRowViewModel? _selectedEnvironment;
    private AdoptionPreview? _pendingAdoption;
    private VersionSwitchPreview? _pendingSwitch;
    private EnvironmentDeletionPreview? _pendingDeletion;
    private bool _isBusy;
    private string _statusMessage = "尚未扫描。";
    private string _adoptionImpact = string.Empty;
    private string _pendingTarget = string.Empty;
    private string _pendingRecoveryPoint = string.Empty;
    private string _deletionImpact = string.Empty;
    private string _deletionTarget = string.Empty;
    private string _deletionRecoveryPoint = string.Empty;
    private DateTimeOffset? _reportGeneratedAt;

    public MainViewModel(IEnvironmentManagerClient client)
    {
        _client = client;
        OperationCenter = new OperationCenterViewModel(
            operations: client,
            deletion: client);
        RuntimeCenter = new RuntimeCenterViewModel(
            client,
            OperationCenter.RefreshAsync,
            new WindowsFileSystemPicker());
        DiagnosticsPackage = new DiagnosticsPackageViewModel(
            client,
            OperationCenter.RefreshAsync);
        EnvironmentVariables = new EnvironmentVariablesViewModel(
            client,
            OperationCenter.RefreshAsync);
        MigrationCenter = new MigrationCenterViewModel(
            client,
            async () =>
            {
                await ScanAsync();
                await OperationCenter.RefreshAsync();
            });
        AgentBinding = new AgentBindingViewModel(
            discoveryClient: client,
            bindingClient: client,
            runtimeCatalog: client,
            filePicker: new WindowsFileSystemPicker(),
            refreshOperations: OperationCenter.RefreshAsync);
        CcSwitch = new CcSwitchViewModel(
            client,
            OperationCenter.RefreshAsync);
        CoordinatedMigration = new CoordinatedMigrationViewModel(
            client,
            OperationCenter.RefreshAsync);
        ScanCommand = new RelayCommand(
            ScanAsync,
            () => !IsBusy,
            HandleException);
        PreviewAdoptionCommand = new RelayCommand(
            PreviewAdoptionAsync,
            () => !IsBusy && SelectedEnvironment is not null,
            HandleException);
        AdoptCommand = new RelayCommand(
            AdoptAsync,
            () => !IsBusy && _pendingAdoption is not null,
            HandleException);
        PreviewSwitchCommand = new RelayCommand(
            PreviewSwitchAsync,
            () => !IsBusy && SelectedEnvironment is not null,
            HandleException);
        SwitchCommand = new RelayCommand(
            SwitchAsync,
            () => !IsBusy && _pendingSwitch is not null,
            HandleException);
        PreviewDeletionCommand = new RelayCommand(
            PreviewDeletionAsync,
            () => !IsBusy && SelectedEnvironment?.IsManaged == true,
            HandleException);
        ConfirmDeletionCommand = new RelayCommand(
            ConfirmDeletionAsync,
            () => !IsBusy && _pendingDeletion is not null,
            HandleException);
    }

    public ObservableCollection<EnvironmentRowViewModel> Environments { get; } = [];

    public OperationCenterViewModel OperationCenter { get; }

    public RuntimeCenterViewModel RuntimeCenter { get; }

    public DiagnosticsPackageViewModel DiagnosticsPackage { get; }

    public EnvironmentVariablesViewModel EnvironmentVariables { get; }

    public MigrationCenterViewModel MigrationCenter { get; }

    public AgentBindingViewModel AgentBinding { get; }

    public CcSwitchViewModel CcSwitch { get; }

    public CoordinatedMigrationViewModel CoordinatedMigration { get; }

    public ObservableCollection<PathConflictViewModel> PathConflicts { get; } = [];

    public ObservableCollection<CommandPathConflictViewModel>
        CommandPathConflicts
    { get; } = [];

    public DateTimeOffset? ReportGeneratedAt
    {
        get => _reportGeneratedAt;
        private set => SetProperty(ref _reportGeneratedAt, value);
    }

    public EnvironmentRowViewModel? SelectedEnvironment
    {
        get => _selectedEnvironment;
        set
        {
            if (SetProperty(ref _selectedEnvironment, value))
            {
                MigrationCenter.SelectedEnvironment = value;
                ClearPendingPlans();
                RaiseCommandStates();
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string AdoptionImpact
    {
        get => _adoptionImpact;
        private set => SetProperty(ref _adoptionImpact, value);
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

    public ICommand ScanCommand { get; }

    public ICommand PreviewAdoptionCommand { get; }

    public ICommand AdoptCommand { get; }

    public ICommand PreviewSwitchCommand { get; }

    public ICommand SwitchCommand { get; }

    public ICommand PreviewDeletionCommand { get; }

    public ICommand ConfirmDeletionCommand { get; }

    public string DeletionImpact
    {
        get => _deletionImpact;
        private set => SetProperty(ref _deletionImpact, value);
    }

    public string DeletionTarget
    {
        get => _deletionTarget;
        private set => SetProperty(ref _deletionTarget, value);
    }

    public string DeletionRecoveryPoint
    {
        get => _deletionRecoveryPoint;
        private set => SetProperty(ref _deletionRecoveryPoint, value);
    }

    public async Task ScanAsync()
    {
        IsBusy = true;
        try
        {
            var report = await _client.InspectAsync();
            Environments.Replace(
                report.Environments.Select(
                    environment => new EnvironmentRowViewModel(environment)));
            PathConflicts.Replace(
                report.PathConflicts.Select(PathConflictViewModel.From));
            CommandPathConflicts.Replace(
                report.CommandPathConflicts.Select(
                    CommandPathConflictViewModel.From));

            ReportGeneratedAt = report.GeneratedAtUtc;
            StatusMessage =
                $"体检完成：发现 {Environments.Count} 个环境清单条目。";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task PreviewAdoptionAsync()
    {
        if (SelectedEnvironment is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            ClearPendingPlans();
            _pendingAdoption = await _client.PreviewAdoptionAsync(
                new EnvironmentFingerprint(
                    SelectedEnvironment.Fingerprint.Value));
            AdoptionImpact = _pendingAdoption.Impact;
            PendingTarget = _pendingAdoption.Asset.Location;
            PendingRecoveryPoint = string.IsNullOrWhiteSpace(
                _pendingAdoption.RecoveryPointId)
                ? "未记录"
                : $"恢复点 {_pendingAdoption.RecoveryPointId}";
            StatusMessage = "纳管预览已生成，请确认影响范围。";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task AdoptAsync()
    {
        if (_pendingAdoption is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var managed = await _client.AdoptAsync(_pendingAdoption);
            StatusMessage =
                $"{managed.Manifest.Name} 已纳管。";
            _pendingAdoption = null;
            AdoptionImpact = string.Empty;
            PendingTarget = string.Empty;
            PendingRecoveryPoint = string.Empty;
            await ScanAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task PreviewSwitchAsync()
    {
        if (SelectedEnvironment is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            ClearPendingPlans();
            _pendingSwitch = await _client.PreviewVersionSwitchAsync(
                new EnvironmentFingerprint(
                    SelectedEnvironment.Fingerprint.Value));
            AdoptionImpact = _pendingSwitch.Impact;
            PendingTarget = _pendingSwitch.Target.Location;
            PendingRecoveryPoint = string.IsNullOrWhiteSpace(
                _pendingSwitch.RecoveryPointId)
                ? "未记录"
                : $"恢复点 {_pendingSwitch.RecoveryPointId}";
            StatusMessage = "版本切换预览已生成，请确认影响范围。";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task SwitchAsync()
    {
        if (_pendingSwitch is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await _client.SwitchVersionAsync(_pendingSwitch);
            StatusMessage = $"{_pendingSwitch.Target.Name} 已切换。";
            _pendingSwitch = null;
            AdoptionImpact = string.Empty;
            PendingTarget = string.Empty;
            PendingRecoveryPoint = string.Empty;
            await ScanAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task PreviewDeletionAsync()
    {
        if (SelectedEnvironment?.IsManaged != true)
        {
            throw new InvalidOperationException(
                "只有已纳管环境可以取消纳管。");
        }

        IsBusy = true;
        try
        {
            ClearPendingDeletion();
            _pendingDeletion =
                await _client.PreviewEnvironmentDeletionAsync(
                    new EnvironmentFingerprint(
                        SelectedEnvironment.Fingerprint.Value));
            DeletionImpact = _pendingDeletion.Impact;
            DeletionTarget = _pendingDeletion.QuarantinePath;
            DeletionRecoveryPoint =
                _pendingDeletion.RecoveryPointId ?? "未记录";
            StatusMessage =
                $"取消纳管预览：{DeletionImpact} 隔离路径 " +
                $"{DeletionTarget}；恢复点 {DeletionRecoveryPoint}。";
            RaiseCommandStates();
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ConfirmDeletionAsync()
    {
        if (_pendingDeletion is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var operation = await _client.QuarantineEnvironmentAsync(
                _pendingDeletion);
            ClearPendingDeletion();
            await ScanAsync();
            StatusMessage =
                $"已取消纳管并移入隔离区，操作 ID：{operation.Id}。";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void RaiseCommandStates()
    {
        ((RelayCommand)ScanCommand).RaiseCanExecuteChanged();
        ((RelayCommand)PreviewAdoptionCommand).RaiseCanExecuteChanged();
        ((RelayCommand)AdoptCommand).RaiseCanExecuteChanged();
        ((RelayCommand)PreviewSwitchCommand).RaiseCanExecuteChanged();
        ((RelayCommand)SwitchCommand).RaiseCanExecuteChanged();
        ((RelayCommand)PreviewDeletionCommand).RaiseCanExecuteChanged();
        ((RelayCommand)ConfirmDeletionCommand).RaiseCanExecuteChanged();
    }

    private void HandleException(Exception exception)
    {
        StatusMessage = $"操作失败：{exception.Message}";
    }

    private void ClearPendingPlans()
    {
        _pendingAdoption = null;
        _pendingSwitch = null;
        AdoptionImpact = string.Empty;
        PendingTarget = string.Empty;
        PendingRecoveryPoint = string.Empty;
        RaiseCommandStates();
    }

    private void ClearPendingDeletion()
    {
        _pendingDeletion = null;
        DeletionImpact = string.Empty;
        DeletionTarget = string.Empty;
        DeletionRecoveryPoint = string.Empty;
        RaiseCommandStates();
    }

}
