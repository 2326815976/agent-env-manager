using System.Collections.ObjectModel;
using System.Windows.Input;
using AgentEnvManager.Core.Adoption;
using AgentEnvManager.Core.EnvironmentVariables;
using AgentEnvManager.Core.Operations;

namespace AgentEnvManager.Wpf;

public sealed class OperationCenterViewModel : ObservableObject
{
    private readonly IEnvironmentManagerClient _client;
    private OperationRecordRowViewModel? _selectedOperation;
    private bool _isBusy;
    private string _statusMessage = "尚未加载操作记录。";
    private string _rollbackTarget = string.Empty;
    private string _rollbackImpact = string.Empty;
    private string _rollbackRecoveryPoint = string.Empty;
    private string? _pendingRollbackOperationId;

    public OperationCenterViewModel(IEnvironmentManagerClient client)
    {
        _client = client;
        RefreshCommand = new RelayCommand(
            RefreshAsync,
            () => !IsBusy,
            HandleException);
        PrepareRollbackCommand = new RelayCommand(
            PrepareRollbackAsync,
            () => !IsBusy && SelectedOperation?.CanRollback == true,
            HandleException);
        ConfirmRollbackCommand = new RelayCommand(
            RollbackSelectedAsync,
            () => !IsBusy && _pendingRollbackOperationId is not null,
            HandleException);
    }

    public ObservableCollection<OperationRecordRowViewModel> Operations { get; } = [];

    public ObservableCollection<RecoveryPointRowViewModel> RecoveryPoints { get; } = [];

    public OperationRecordRowViewModel? SelectedOperation
    {
        get => _selectedOperation;
        set
        {
            if (SetProperty(ref _selectedOperation, value))
            {
                ClearRollbackPlan();
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

    public string RollbackTarget
    {
        get => _rollbackTarget;
        private set => SetProperty(ref _rollbackTarget, value);
    }

    public string RollbackImpact
    {
        get => _rollbackImpact;
        private set => SetProperty(ref _rollbackImpact, value);
    }

    public string RollbackRecoveryPoint
    {
        get => _rollbackRecoveryPoint;
        private set => SetProperty(ref _rollbackRecoveryPoint, value);
    }

    public ICommand RefreshCommand { get; }

    public ICommand PrepareRollbackCommand { get; }

    public ICommand ConfirmRollbackCommand { get; }

    public async Task RefreshAsync()
    {
        IsBusy = true;
        try
        {
            var operations = await _client.ListOperationsAsync();
            Operations.Replace(
                operations.Select(
                    operation => new OperationRecordRowViewModel(operation)));

            var environmentPoints =
                await _client.ListEnvironmentRecoveryPointsAsync();
            var variablePoints =
                await _client.ListEnvironmentVariableRecoveryPointsAsync();
            RecoveryPoints.Replace(
                environmentPoints
                    .Select(point => RecoveryPointRowViewModel.From(point))
                    .Concat(variablePoints.Select(
                        point => RecoveryPointRowViewModel.From(point))));
            ClearRollbackPlan();
            StatusMessage = $"已加载 {Operations.Count} 条操作记录。";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public Task PrepareRollbackAsync()
    {
        if (SelectedOperation?.CanRollback != true)
        {
            throw new InvalidOperationException("所选操作当前不能回滚。");
        }

        return PrepareRollbackCoreAsync(SelectedOperation.Id);
    }

    private async Task PrepareRollbackCoreAsync(string operationId)
    {
        var plan = await _client.PreviewRollbackAsync(operationId);
        _pendingRollbackOperationId = plan.OperationId;
        RollbackTarget = plan.Target;
        RollbackImpact = $"{plan.Impact} 预期结果：{plan.ExpectedResult}";
        RollbackRecoveryPoint = plan.RecoveryPointId;
        StatusMessage = "回滚计划已生成，请确认目标、影响范围和恢复点。";
        RaiseCommandStates();
    }

    public async Task RollbackSelectedAsync()
    {
        if (_pendingRollbackOperationId is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var operation = await _client.RollbackOperationAsync(
                _pendingRollbackOperationId);
            await RefreshAsync();
            StatusMessage = $"操作 {operation.Id} 已回滚。";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void HandleException(Exception exception)
    {
        StatusMessage = $"操作中心失败：{exception.Message}";
    }

    private void RaiseCommandStates()
    {
        ((RelayCommand)RefreshCommand).RaiseCanExecuteChanged();
        ((RelayCommand)PrepareRollbackCommand).RaiseCanExecuteChanged();
        ((RelayCommand)ConfirmRollbackCommand).RaiseCanExecuteChanged();
    }

    private void ClearRollbackPlan()
    {
        _pendingRollbackOperationId = null;
        RollbackTarget = string.Empty;
        RollbackImpact = string.Empty;
        RollbackRecoveryPoint = string.Empty;
        RaiseCommandStates();
    }

}

public sealed class OperationRecordRowViewModel(OperationRecord operation)
{
    public string Id { get; } = operation.Id;

    public string TypeLabel { get; } = operation.Type switch
    {
        OperationType.Adopt => "纳管",
        OperationType.Switch => "版本切换",
        OperationType.EnvironmentVariables => "环境变量事务",
        _ => operation.Type.ToString()
    };

    public string StateLabel { get; } = operation.State switch
    {
        OperationState.Draft => "计划",
        OperationState.Validated => "已校验",
        OperationState.RecoveryReady => "恢复点就绪",
        OperationState.Executing => "执行中",
        OperationState.Verifying => "验证中",
        OperationState.Succeeded => "成功",
        OperationState.Failed => "失败",
        OperationState.RolledBack => "已回滚",
        _ => operation.State.ToString()
    };

    public string Summary { get; } = operation.Summary;

    public string Target { get; } = operation.Target ?? "未记录";

    public string Impact { get; } = operation.Impact ?? "未记录";

    public string RecoveryPointId { get; } =
        operation.RecoveryPointId ?? "未记录";

    public string UpdatedAt { get; } =
        operation.UpdatedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

    public string FailureReason { get; } =
        operation.FailureReason ?? string.Empty;

    public bool CanRollback { get; } =
        !string.IsNullOrWhiteSpace(operation.RecoveryPointId)
        && !string.IsNullOrWhiteSpace(operation.Target)
        && !string.IsNullOrWhiteSpace(operation.Impact)
        && operation.State == OperationState.Failed;
}

public sealed record RecoveryPointRowViewModel(
    string Id,
    string TypeLabel,
    string Description,
    string CreatedAt)
{
    public static RecoveryPointRowViewModel From(
        AdoptionRecoveryPoint point)
    {
        return new RecoveryPointRowViewModel(
            point.Id,
            "环境资产",
            point.Description,
            point.CreatedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));
    }

    public static RecoveryPointRowViewModel From(
        EnvironmentVariableRecoveryPoint point)
    {
        return new RecoveryPointRowViewModel(
            point.Id,
            "环境变量",
            $"原值 {point.OriginalValues.Count} 项",
            point.CreatedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));
    }
}
