using System.Collections.ObjectModel;
using System.Windows.Input;
using AgentEnvManager.Core.Diagnostics;

namespace AgentEnvManager.Wpf;

public sealed class DiagnosticsPackageViewModel : AsyncOperationViewModel
{
    private readonly IDiagnosticsPackageClient _client;
    private DiagnosticPackagePreview? _reviewedPreview;
    private string _destinationPath = string.Empty;
    private string _exportResult = string.Empty;
    private DiagnosticPackageEntry? _selectedEntry;
    private string _impact = string.Empty;
    private string _telemetryLabel = string.Empty;
    private string _automaticUploadLabel = string.Empty;

    public DiagnosticsPackageViewModel(
        IDiagnosticsPackageClient client,
        Func<Task>? refreshOperations = null)
        : base(refreshOperations, "尚未生成诊断包预览。")
    {
        _client = client;
        PreviewCommand = new RelayCommand(
            PreviewAsync,
            () => !IsBusy,
            HandleException);
        ExportCommand = new RelayCommand(
            ExportAsync,
            () => !IsBusy && CanExport(),
            HandleException);
    }

    public ObservableCollection<DiagnosticPackageEntry> Entries
    { get; } = [];

    public DiagnosticPackageEntry? SelectedEntry
    {
        get => _selectedEntry;
        set => SetProperty(ref _selectedEntry, value);
    }

    public string Impact
    {
        get => _impact;
        private set => SetProperty(ref _impact, value);
    }

    public string TelemetryLabel
    {
        get => _telemetryLabel;
        private set => SetProperty(ref _telemetryLabel, value);
    }

    public string AutomaticUploadLabel
    {
        get => _automaticUploadLabel;
        private set => SetProperty(ref _automaticUploadLabel, value);
    }

    public string DestinationPath
    {
        get => _destinationPath;
        set
        {
            if (SetProperty(ref _destinationPath, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public string ExportResult
    {
        get => _exportResult;
        private set => SetProperty(ref _exportResult, value);
    }

    public ICommand PreviewCommand { get; }

    public ICommand ExportCommand { get; }

    public async Task PreviewAsync()
    {
        await RunBusyAsync(async () =>
        {
            _reviewedPreview =
                await _client.PreviewDiagnosticPackageAsync();
            Entries.Replace(_reviewedPreview.Entries);
            Impact = _reviewedPreview.Impact;
            TelemetryLabel =
                _reviewedPreview.TelemetryEnabled ? "启用" : "关闭";
            AutomaticUploadLabel =
                _reviewedPreview.AllowsAutomaticUpload
                    ? "允许"
                    : "禁止";
            SelectedEntry = Entries.FirstOrDefault();
            ExportResult = string.Empty;
            StatusMessage = "诊断包预览已生成，请审阅后再导出。";
        });
    }

    public async Task ExportAsync()
    {
        if (_reviewedPreview is null)
        {
            throw new InvalidOperationException(
                "请先生成并审阅诊断包预览。");
        }

        EnsureLocalDestinationPath();
        await RunBusyAsync(async () =>
        {
            var result = await _client.ExportDiagnosticPackageAsync(
                _reviewedPreview,
                DestinationPath);
            ExportResult =
                $"诊断包: {result.DestinationPath}{Environment.NewLine}" +
                $"条目数量: {result.EntryCount}{Environment.NewLine}" +
                $"操作 ID: {result.OperationId}";
            StatusMessage = "脱敏诊断包已导出。";
            await RefreshOperationsAsync();
        });
    }

    private bool CanExport()
    {
        if (_reviewedPreview is null
            || string.IsNullOrWhiteSpace(DestinationPath))
        {
            return false;
        }

        try
        {
            EnsureLocalDestinationPath();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void EnsureLocalDestinationPath()
    {
        if (Uri.TryCreate(
                DestinationPath,
                UriKind.Absolute,
                out var uri)
            && !uri.IsFile)
        {
            throw new InvalidOperationException(
                "诊断包只能导出到本地路径。");
        }

        DiagnosticDestinationPolicy.EnsureLocal(DestinationPath);
    }

    private void HandleException(Exception exception)
    {
        SetError("诊断包操作", exception);
    }

    protected override void RaiseCommandStates()
    {
        ((RelayCommand)PreviewCommand).RaiseCanExecuteChanged();
        ((RelayCommand)ExportCommand).RaiseCanExecuteChanged();
    }
}
