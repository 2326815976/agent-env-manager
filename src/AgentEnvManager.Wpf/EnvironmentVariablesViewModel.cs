using System.Collections.ObjectModel;
using System.Windows.Input;
using AgentEnvManager.Core.EnvironmentVariables;

namespace AgentEnvManager.Wpf;

public sealed class EnvironmentVariablesViewModel : AsyncOperationViewModel
{
    private readonly IEnvironmentVariablesClient _client;
    private readonly HashSet<string> _removedVariableNames =
        new(StringComparer.OrdinalIgnoreCase);
    private EnvironmentVariableUpdatePreview? _reviewedPreview;
    private ManagedVariableViewModel? _selectedVariable;
    private string _newVariableName = string.Empty;
    private string _newVariableValue = string.Empty;
    private string _previewImpact = string.Empty;
    private string _pendingRecoveryPoint = string.Empty;
    private string _machineScopeNote = string.Empty;

    public EnvironmentVariablesViewModel(
        IEnvironmentVariablesClient client,
        Func<Task>? refreshOperations = null)
        : base(refreshOperations, "尚未加载环境变量。")
    {
        _client = client;
        LoadCommand = new RelayCommand(
            LoadAsync,
            () => !IsBusy,
            HandleException);
        PreviewCommand = new RelayCommand(
            PreviewAsync,
            () => !IsBusy && HasChanges(),
            HandleException);
        ApplyCommand = new RelayCommand(
            ApplyAsync,
            () => !IsBusy && _reviewedPreview is not null,
            HandleException);
        AddVariableCommand = new RelayCommand(
            AddVariableAsync,
            () =>
                !IsBusy
                && !string.IsNullOrWhiteSpace(NewVariableName),
            HandleException);
        RemoveVariableCommand = new RelayCommand(
            RemoveSelectedVariableAsync,
            () => !IsBusy && SelectedVariable is not null,
            HandleException);
    }

    public ObservableCollection<ManagedPathEntryViewModel> PathEntries
    { get; } = [];

    public ObservableCollection<ManagedVariableViewModel> Variables
    { get; } = [];

    public ObservableCollection<EnvironmentVariableRowViewModel>
        ExternalVariables
    { get; } = [];

    public ObservableCollection<EnvironmentVariableRowViewModel>
        MachineVariables
    { get; } = [];

    public ObservableCollection<ExternalPathEntryViewModel>
        ExternalPathEntries
    { get; } = [];

    public ManagedVariableViewModel? SelectedVariable
    {
        get => _selectedVariable;
        set
        {
            if (SetProperty(ref _selectedVariable, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public string NewVariableName
    {
        get => _newVariableName;
        set
        {
            if (SetProperty(ref _newVariableName, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public string NewVariableValue
    {
        get => _newVariableValue;
        set => SetProperty(ref _newVariableValue, value);
    }

    public string PreviewImpact
    {
        get => _previewImpact;
        private set => SetProperty(ref _previewImpact, value);
    }

    public string PendingRecoveryPoint
    {
        get => _pendingRecoveryPoint;
        private set => SetProperty(ref _pendingRecoveryPoint, value);
    }

    public string MachineScopeNote
    {
        get => _machineScopeNote;
        private set => SetProperty(ref _machineScopeNote, value);
    }

    public ICommand LoadCommand { get; }

    public ICommand PreviewCommand { get; }

    public ICommand ApplyCommand { get; }

    public ICommand AddVariableCommand { get; }

    public ICommand RemoveVariableCommand { get; }

    public async Task LoadAsync()
    {
        await RunBusyAsync(async () =>
        {
            var snapshot =
                await _client.InspectEnvironmentVariableEditorAsync();
            PathEntries.Replace(snapshot.PathEntries
                .Where(entry => entry.IsManaged)
                .Select(
                entry => new ManagedPathEntryViewModel(
                    entry,
                    ClearPendingPreview)));
            ExternalPathEntries.Replace(snapshot.PathEntries
                .Where(entry => !entry.IsManaged)
                .Select(entry =>
                    new ExternalPathEntryViewModel(entry.ManagedEntryPath)));
            Variables.Replace(snapshot.Variables
                .Where(variable => variable.IsManaged)
                .Select(
                variable => new ManagedVariableViewModel(
                    variable,
                    ClearPendingPreview)));
            ExternalVariables.Replace(snapshot.Variables
                .Where(variable => !variable.IsManaged)
                .Select(variable => new EnvironmentVariableRowViewModel(
                    variable.Name,
                    variable.Value,
                    variable.IsHighRisk)));
            MachineVariables.Replace(snapshot.MachineVariables.Select(
                variable => new EnvironmentVariableRowViewModel(
                    variable.Name,
                    variable.Value,
                    variable.IsHighRisk)));
            MachineScopeNote = snapshot.MachineScopeDescription;
            _removedVariableNames.Clear();
            SelectedVariable = Variables.FirstOrDefault();
            ClearPendingPreview();
            StatusMessage =
                $"已加载 {PathEntries.Count} 个受管入口和 " +
                $"{Variables.Count} 个受管变量，另有 " +
                $"{ExternalVariables.Count} 个用户变量和 " +
                $"{MachineVariables.Count} 个机器级变量为只读。";
        });
    }

    public async Task PreviewAsync()
    {
        await RunBusyAsync(async () =>
        {
            var managedEntries = PathEntries
                .Where(entry => entry.IsEnabled)
                .Select(entry => entry.ManagedEntryPath)
                .ToArray();
            var variableChanges = Variables
                .Select(variable => new EnvironmentVariableChange(
                    variable.Name,
                    string.IsNullOrWhiteSpace(variable.Value)
                        ? null
                        : variable.Value))
                .Concat(_removedVariableNames.Select(name =>
                    new EnvironmentVariableChange(name, null)))
                .ToArray();
            _reviewedPreview =
                await _client.PreviewManagedEnvironmentUpdateAsync(
                    managedEntries,
                    variableChanges);
            PreviewImpact = _reviewedPreview.Impact;
            PendingRecoveryPoint =
                _reviewedPreview.Changes.Count == 0
                    ? "未记录"
                    : "应用后创建恢复点";
            StatusMessage = "环境变量预览已生成，请确认后再应用。";
            RaiseCommandStates();
        });
    }

    public async Task ApplyAsync()
    {
        if (_reviewedPreview is null)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var result = await _client.ApplyEnvironmentVariableUpdateAsync(
                _reviewedPreview);
            PendingRecoveryPoint = result.RecoveryPoint.Id;
            StatusMessage =
                $"环境变量已应用，恢复点：{result.RecoveryPoint.Id}。";
            _reviewedPreview = null;
            await RefreshOperationsAsync();
        });
    }

    public Task AddVariableAsync()
    {
        var name = NewVariableName.Trim();
        if (!name.StartsWith(
                "AGENT_ENV_MANAGER_",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "受管变量名必须以 AGENT_ENV_MANAGER_ 开头。");
        }

        var existing = Variables.FirstOrDefault(variable =>
            string.Equals(
                variable.Name,
                name,
                StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            Variables.Add(new ManagedVariableViewModel(
                new EnvironmentVariableEditorVariable(
                    name,
                    NewVariableValue,
                    IsExpandable: false),
                ClearPendingPreview));
        }
        else
        {
            existing.Value = NewVariableValue;
        }

        _removedVariableNames.Remove(name);
        NewVariableName = string.Empty;
        NewVariableValue = string.Empty;
        ClearPendingPreview();
        return Task.CompletedTask;
    }

    public void RemoveSelectedVariable()
    {
        if (SelectedVariable is not null)
        {
            _removedVariableNames.Add(SelectedVariable.Name);
            Variables.Remove(SelectedVariable);
            SelectedVariable = null;
            ClearPendingPreview();
        }
    }

    private Task RemoveSelectedVariableAsync()
    {
        RemoveSelectedVariable();
        return Task.CompletedTask;
    }

    private bool HasChanges()
    {
        return PathEntries.Count > 0 || Variables.Count > 0;
    }

    private void ClearPendingPreview()
    {
        _reviewedPreview = null;
        PreviewImpact = string.Empty;
        PendingRecoveryPoint = string.Empty;
        RaiseCommandStates();
    }

    private void HandleException(Exception exception)
    {
        SetError("环境变量操作", exception);
    }

    protected override void RaiseCommandStates()
    {
        ((RelayCommand)LoadCommand).RaiseCanExecuteChanged();
        ((RelayCommand)PreviewCommand).RaiseCanExecuteChanged();
        ((RelayCommand)ApplyCommand).RaiseCanExecuteChanged();
        ((RelayCommand)AddVariableCommand).RaiseCanExecuteChanged();
        ((RelayCommand)RemoveVariableCommand).RaiseCanExecuteChanged();
    }
}

public sealed class ManagedPathEntryViewModel : ObservableObject
{
    private readonly Action _changed;
    private bool _isEnabled;

    public ManagedPathEntryViewModel(
        EnvironmentVariableEditorPathEntry entry,
        Action changed)
    {
        Name = entry.Name;
        Version = entry.Version ?? string.Empty;
        ManagedEntryPath = entry.ManagedEntryPath;
        _isEnabled = entry.IsEnabled;
        _changed = changed;
    }

    public string Name { get; }

    public string Version { get; }

    public string ManagedEntryPath { get; }

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (SetProperty(ref _isEnabled, value))
            {
                _changed();
            }
        }
    }
}

public sealed record ExternalPathEntryViewModel(string Entry)
{
    public string EditabilityLabel => "只读";
}

public sealed record EnvironmentVariableRowViewModel(
    string Name,
    string? Value,
    bool IsHighRisk)
{
    public string ValueLabel => Value ?? string.Empty;

    public string EditabilityLabel =>
        IsHighRisk ? "只读（高风险）" : "只读";
}

public sealed class ManagedVariableViewModel : ObservableObject
{
    private readonly Action _changed;
    private string? _value;

    public ManagedVariableViewModel(
        EnvironmentVariableEditorVariable variable,
        Action changed)
    {
        Name = variable.Name;
        _value = variable.Value;
        IsExpandable = variable.IsExpandable;
        _changed = changed;
    }

    public string Name { get; }

    public string? Value
    {
        get => _value;
        set
        {
            if (SetProperty(ref _value, value))
            {
                _changed();
            }
        }
    }

    public bool IsExpandable { get; }
}
