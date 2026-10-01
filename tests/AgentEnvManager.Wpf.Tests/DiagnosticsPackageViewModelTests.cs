using AgentEnvManager.Core.Diagnostics;
using AgentEnvManager.Wpf;

namespace AgentEnvManager.Wpf.Tests;

public sealed class DiagnosticsPackageViewModelTests
{
    [Fact]
    public async Task PreviewAndExport_reviews_content_and_refreshes_operations()
    {
        var client = new StubDiagnosticsClient();
        var refreshCount = 0;
        var viewModel = new DiagnosticsPackageViewModel(
            client,
            () =>
            {
                refreshCount++;
                return Task.CompletedTask;
            });

        await viewModel.PreviewAsync();

        var entry = Assert.Single(viewModel.Entries);
        Assert.Equal("summary.txt", entry.Name);
        Assert.Equal("脱敏诊断摘要。", entry.Content);
        Assert.Equal("只导出脱敏内容。", viewModel.Impact);
        Assert.Equal("关闭", viewModel.TelemetryLabel);
        Assert.Equal("禁止", viewModel.AutomaticUploadLabel);
        Assert.False(viewModel.ExportCommand.CanExecute(null));

        viewModel.DestinationPath = "https://example.test/diagnostics.zip";

        Assert.False(viewModel.ExportCommand.CanExecute(null));

        viewModel.DestinationPath = @"\\server\share\diagnostics.zip";

        Assert.False(viewModel.ExportCommand.CanExecute(null));

        viewModel.DestinationPath = @"D:\diagnostics\agent-env-manager.zip";

        Assert.True(viewModel.ExportCommand.CanExecute(null));

        await viewModel.ExportAsync();

        Assert.NotNull(client.ExportedPreview);
        Assert.Equal(
            @"D:\diagnostics\agent-env-manager.zip",
            client.DestinationPath);
        Assert.Equal(1, refreshCount);
        Assert.Contains("已导出", viewModel.StatusMessage);
        Assert.Contains(
            @"D:\diagnostics\agent-env-manager.zip",
            viewModel.ExportResult);
    }

    [Fact]
    public async Task PreviewAsync_notifies_bound_preview_properties()
    {
        var viewModel = new DiagnosticsPackageViewModel(
            new StubDiagnosticsClient());
        var changed = new List<string?>();
        viewModel.PropertyChanged += (_, args) =>
            changed.Add(args.PropertyName);

        await viewModel.PreviewAsync();

        Assert.Contains(nameof(DiagnosticsPackageViewModel.Impact), changed);
        Assert.Contains(
            nameof(DiagnosticsPackageViewModel.TelemetryLabel),
            changed);
        Assert.Contains(
            nameof(DiagnosticsPackageViewModel.AutomaticUploadLabel),
            changed);
    }

    private sealed class StubDiagnosticsClient
        : StubEnvironmentManagerClient
    {
        public DiagnosticPackagePreview Preview { get; } = new(
            [
                new DiagnosticPackageEntry(
                    "summary.txt",
                    "诊断摘要。",
                    "脱敏诊断摘要。")
            ],
            "只导出脱敏内容。",
            "preview-hash",
            TelemetryEnabled: false,
            AllowsAutomaticUpload: false);

        public DiagnosticPackagePreview? ExportedPreview { get; private set; }

        public string? DestinationPath { get; private set; }

        public override Task<DiagnosticPackagePreview>
            PreviewDiagnosticPackageAsync(
                CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Preview);
        }

        public override Task<DiagnosticPackageResult>
            ExportDiagnosticPackageAsync(
                DiagnosticPackagePreview preview,
                string destinationPath,
                CancellationToken cancellationToken = default)
        {
            ExportedPreview = preview;
            DestinationPath = destinationPath;
            return Task.FromResult(new DiagnosticPackageResult(
                destinationPath,
                preview.Entries.Count,
                DateTimeOffset.UnixEpoch,
                "operation-diagnostics"));
        }
    }
}
