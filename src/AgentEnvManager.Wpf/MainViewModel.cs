using System.Collections.ObjectModel;
using System.Windows.Input;
using AgentEnvManager.Core.Adoption;
using AgentEnvManager.Core.Inspection;

namespace AgentEnvManager.Wpf;

public sealed class MainViewModel : ObservableObject
{
    private readonly IEnvironmentManagerClient _client;
    private EnvironmentRowViewModel? _selectedEnvironment;
    private AdoptionPreview? _pendingAdoption;
    private bool _isBusy;
    private string _statusMessage = "尚未扫描。";
    private string _adoptionImpact = string.Empty;
    private DateTimeOffset? _reportGeneratedAt;

    public MainViewModel(IEnvironmentManagerClient client)
    {
        _client = client;
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
    }

    public ObservableCollection<EnvironmentRowViewModel> Environments { get; } = [];

    public ObservableCollection<PathConflictViewModel> PathConflicts { get; } = [];

    public ObservableCollection<CommandPathConflictViewModel>
        CommandPathConflicts { get; } = [];

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
                _pendingAdoption = null;
                AdoptionImpact = string.Empty;
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

    public ICommand ScanCommand { get; }

    public ICommand PreviewAdoptionCommand { get; }

    public ICommand AdoptCommand { get; }

    public async Task ScanAsync()
    {
        IsBusy = true;
        try
        {
            var report = await _client.InspectAsync();
            Replace(
                Environments,
                report.Environments.Select(
                    environment => new EnvironmentRowViewModel(environment)));
            Replace(
                PathConflicts,
                report.PathConflicts.Select(PathConflictViewModel.From));
            Replace(
                CommandPathConflicts,
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
            _pendingAdoption = await _client.PreviewAdoptionAsync(
                new EnvironmentFingerprint(
                    SelectedEnvironment.Fingerprint.Value));
            AdoptionImpact = _pendingAdoption.Impact;
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
            await ScanAsync();
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
    }

    private void HandleException(Exception exception)
    {
        StatusMessage = $"操作失败：{exception.Message}";
    }

    private static void Replace<T>(
        ObservableCollection<T> target,
        IEnumerable<T> values)
    {
        target.Clear();
        foreach (var value in values)
        {
            target.Add(value);
        }
    }
}
