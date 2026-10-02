using System.Windows.Input;
using AgentEnvManager.Core.Migrations;

namespace AgentEnvManager.Wpf;

public sealed class CoordinatedMigrationViewModel : AsyncOperationViewModel
{
    private readonly ICoordinatedMigrationClient _client;
    private CoordinatedMigrationPreview? _pending;
    private string _codexSource = string.Empty;
    private string _codexDestination = string.Empty;
    private string _ccSwitchSource = string.Empty;
    private string _ccSwitchDestination = string.Empty;
    private string _blockersLabel = "尚未生成计划。";
    private string _impact = string.Empty;
    private string _plannedRewritesLabel = string.Empty;
    private string _startupOrderLabel = string.Empty;
    private string _pendingOperation = string.Empty;
    private string _pendingRecoveryPoint = string.Empty;
    private string _result = string.Empty;

    public CoordinatedMigrationViewModel(
        ICoordinatedMigrationClient client,
        Func<Task>? refreshOperations = null)
        : base(refreshOperations, "请填写两个配置环境的源目录与目标目录。")
    {
        _client = client;
        PreviewCommand = new RelayCommand(
            PreviewAsync,
            () => !IsBusy && HasInputs(),
            HandleException);
        ApplyCommand = new RelayCommand(
            ApplyAsync,
            () => !IsBusy && _pending?.CanApply == true,
            HandleException);
    }

    public string CodexSource
    {
        get => _codexSource;
        set => SetInput(ref _codexSource, value);
    }

    public string CodexDestination
    {
        get => _codexDestination;
        set => SetInput(ref _codexDestination, value);
    }

    public string CcSwitchSource
    {
        get => _ccSwitchSource;
        set => SetInput(ref _ccSwitchSource, value);
    }

    public string CcSwitchDestination
    {
        get => _ccSwitchDestination;
        set => SetInput(ref _ccSwitchDestination, value);
    }

    public string BlockersLabel
    {
        get => _blockersLabel;
        private set => SetProperty(ref _blockersLabel, value);
    }

    public string Impact
    {
        get => _impact;
        private set => SetProperty(ref _impact, value);
    }

    public string PlannedRewritesLabel
    {
        get => _plannedRewritesLabel;
        private set => SetProperty(ref _plannedRewritesLabel, value);
    }

    public string StartupOrderLabel
    {
        get => _startupOrderLabel;
        private set => SetProperty(ref _startupOrderLabel, value);
    }

    public string PendingOperation
    {
        get => _pendingOperation;
        private set => SetProperty(ref _pendingOperation, value);
    }

    public string PendingRecoveryPoint
    {
        get => _pendingRecoveryPoint;
        private set => SetProperty(ref _pendingRecoveryPoint, value);
    }

    public string Result
    {
        get => _result;
        private set => SetProperty(ref _result, value);
    }

    public ICommand PreviewCommand { get; }

    public ICommand ApplyCommand { get; }

    public async Task PreviewAsync()
    {
        await RunBusyAsync(async () =>
        {
            Result = string.Empty;
            _pending = await _client.PreviewCoordinatedMigrationAsync(
                new CoordinatedMigrationRequest(
                    CodexSource,
                    CodexDestination,
                    CcSwitchSource,
                    CcSwitchDestination));
            Impact = _pending.Impact;
            PlannedRewritesLabel = string.Join(
                Environment.NewLine,
                _pending.PlannedRewrites);
            StartupOrderLabel = string.Join(
                Environment.NewLine,
                _pending.StartupOrder);
            PendingOperation = _pending.OperationId ?? "未记录";
            PendingRecoveryPoint =
                _pending.RecoveryPointId ?? "执行阶段创建";
            BlockersLabel = _pending.Blockers.Count == 0
                ? "无阻断项，可以执行。"
                : string.Join(
                    Environment.NewLine,
                    _pending.Blockers.Select(blocker =>
                        $"[{blocker.Code}] {blocker.Message}"));
            StatusMessage = _pending.CanApply
                ? "迁移计划已生成，请确认影响范围与恢复点。"
                : "迁移计划包含阻断项，需先处理。";
            RaiseCommandStates();
        });
    }

    public async Task ApplyAsync()
    {
        if (_pending?.CanApply != true)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var result = await _client.ApplyCoordinatedMigrationAsync(
                _pending);
            Result = string.Join(
                Environment.NewLine,
                $"操作 ID: {result.Operation.Id}",
                $"已迁移: {string.Join("、", result.MigratedPaths)}",
                $"已重写: {string.Join("、", result.RewrittenPaths)}",
                result.QuarantinedSourceIds.Count == 0
                    ? "源目录: 仍保留在原位置"
                    : $"源目录已隔离: " +
                        string.Join("、", result.QuarantinedSourceIds));
            PendingRecoveryPoint = result.Operation.RecoveryPointId
                ?? PendingRecoveryPoint;
            _pending = null;
            StatusMessage = "协同迁移完成。";
            RaiseCommandStates();
            await RefreshOperationsAsync();
        });
    }

    private void SetInput(ref string field, string value)
    {
        if (SetProperty(ref field, value))
        {
            ClearPendingPlan();
        }
    }

    private bool HasInputs()
    {
        return !string.IsNullOrWhiteSpace(CodexSource)
            && !string.IsNullOrWhiteSpace(CodexDestination)
            && !string.IsNullOrWhiteSpace(CcSwitchSource)
            && !string.IsNullOrWhiteSpace(CcSwitchDestination);
    }

    private void HandleException(Exception exception)
    {
        SetError("协同迁移", exception);
    }

    protected override void RaiseCommandStates()
    {
        ((RelayCommand)PreviewCommand).RaiseCanExecuteChanged();
        ((RelayCommand)ApplyCommand).RaiseCanExecuteChanged();
    }

    private void ClearPendingPlan()
    {
        _pending = null;
        BlockersLabel = "尚未生成计划。";
        Impact = string.Empty;
        PlannedRewritesLabel = string.Empty;
        StartupOrderLabel = string.Empty;
        PendingOperation = string.Empty;
        PendingRecoveryPoint = string.Empty;
        RaiseCommandStates();
    }
}
