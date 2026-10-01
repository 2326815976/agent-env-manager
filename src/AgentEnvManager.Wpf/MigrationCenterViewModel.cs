using System.Windows.Input;
using AgentEnvManager.Core.Migrations;

namespace AgentEnvManager.Wpf;

public sealed class MigrationCenterViewModel : AsyncOperationViewModel
{
    private readonly IMigrationClient _client;
    private EnvironmentRowViewModel? _selectedEnvironment;
    private string _destinationPath = string.Empty;
    private MigrationPreview? _pendingMigration;
    private string _migrationImpact = string.Empty;
    private string _pendingTarget = string.Empty;
    private string _pendingRecoveryPoint = string.Empty;
    private string _expectedResult = string.Empty;

    public MigrationCenterViewModel(
        IMigrationClient client,
        Func<Task>? refreshOperations = null)
        : base(refreshOperations, "请选择已纳管环境和迁移目标。")
    {
        _client = client;
        PreviewMigrationCommand = new RelayCommand(
            PreviewMigrationAsync,
            () =>
                !IsBusy
                && SelectedEnvironment?.IsManaged == true
                && !string.IsNullOrWhiteSpace(DestinationPath),
            HandleException);
        MigrateCommand = new RelayCommand(
            MigrateAsync,
            () => !IsBusy && _pendingMigration is not null,
            HandleException);
    }

    public EnvironmentRowViewModel? SelectedEnvironment
    {
        get => _selectedEnvironment;
        set
        {
            if (SetProperty(ref _selectedEnvironment, value))
            {
                ClearPendingMigration();
                RaiseCommandStates();
            }
        }
    }

    public string DestinationPath
    {
        get => _destinationPath;
        set
        {
            if (SetProperty(ref _destinationPath, value))
            {
                ClearPendingMigration();
                RaiseCommandStates();
            }
        }
    }

    public string MigrationImpact
    {
        get => _migrationImpact;
        private set => SetProperty(ref _migrationImpact, value);
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

    public string ExpectedResult
    {
        get => _expectedResult;
        private set => SetProperty(ref _expectedResult, value);
    }

    public ICommand PreviewMigrationCommand { get; }

    public ICommand MigrateCommand { get; }

    public async Task PreviewMigrationAsync()
    {
        if (SelectedEnvironment?.IsManaged != true)
        {
            throw new InvalidOperationException(
                "请选择已纳管环境。");
        }

        if (string.IsNullOrWhiteSpace(DestinationPath))
        {
            throw new InvalidOperationException(
                "请输入迁移目标路径。");
        }

        await RunBusyAsync(async () =>
        {
            _pendingMigration = await _client.PreviewMigrationAsync(
                SelectedEnvironment.Fingerprint,
                DestinationPath);
            MigrationImpact = _pendingMigration.Impact;
            PendingTarget = _pendingMigration.DestinationPath;
            PendingRecoveryPoint =
                _pendingMigration.RecoveryPointId ?? "未记录";
            ExpectedResult = _pendingMigration.ExpectedResult;
            StatusMessage = "迁移预览已生成，请确认影响范围。";
        });
    }

    public async Task MigrateAsync()
    {
        if (_pendingMigration is null)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var operation = await _client.MigrateEnvironmentAsync(
                _pendingMigration);
            StatusMessage =
                $"已纳管环境已迁移，操作 ID：{operation.Id}。";
            _pendingMigration = null;
            ClearPendingMigration();
            await RefreshOperationsAsync();
        });
    }

    private void HandleException(Exception exception)
    {
        SetError("迁移操作", exception);
    }

    protected override void RaiseCommandStates()
    {
        ((RelayCommand)PreviewMigrationCommand).RaiseCanExecuteChanged();
        ((RelayCommand)MigrateCommand).RaiseCanExecuteChanged();
    }

    private void ClearPendingMigration()
    {
        _pendingMigration = null;
        MigrationImpact = string.Empty;
        PendingTarget = string.Empty;
        PendingRecoveryPoint = string.Empty;
        ExpectedResult = string.Empty;
        RaiseCommandStates();
    }
}
