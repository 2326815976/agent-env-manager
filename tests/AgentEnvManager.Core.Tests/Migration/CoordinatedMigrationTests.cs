using AgentEnvManager.Core.Inspection;
using AgentEnvManager.Core.Migrations;
using AgentEnvManager.Core.Operations;
using AgentEnvManager.Core.Storage;
using AgentEnvManager.Core.Tests.TestSupport;

namespace AgentEnvManager.Core.Tests.Migration;

public sealed class CoordinatedMigrationTests
{
    [Fact]
    public async Task PreviewAsync_plans_copy_verify_rewrites_and_startup_order()
    {
        var root = CreateTempRoot();
        try
        {
            var request = CreateRequest(root);
            var journal = new RecordingOperationJournal();
            var manager = CreateManager(
                root,
                new StubProcessProbe([]),
                journal);

            var preview = await manager.PreviewCoordinatedMigrationAsync(
                request);

            Assert.True(preview.CanApply);
            Assert.Empty(preview.Blockers);
            Assert.Equal(2, preview.Targets.Count);
            Assert.Equal(
                [
                    "chatgpt",
                    "codex-app-server",
                    "cc-switch"
                ],
                preview.ProcessesToStop.Select(item => item.Name));
            Assert.Contains(
                preview.PlannedRewrites,
                item => item.Contains("CODEX_HOME", StringComparison.Ordinal));
            Assert.Contains(
                preview.PlannedRewrites,
                item => item.Contains("codexConfigDir", StringComparison.Ordinal));
            Assert.Contains(
                preview.PlannedRewrites,
                item => item.Contains("Junction", StringComparison.Ordinal));
            Assert.Contains("CC Switch", preview.StartupOrder[0]);
            Assert.Contains(
                "原路径保留到健康检查通过",
                preview.Impact,
                StringComparison.Ordinal);

            var operation = Assert.Single(
                journal.History
                    .Where(record => record.Id == preview.OperationId)
                    .GroupBy(record => record.Id)
                    .Select(group => group.Last()));
            Assert.Equal(OperationType.Migrate, operation.Type);
            Assert.Equal(OperationState.Validated, operation.State);
            Assert.Null(preview.RecoveryPointId);
            Assert.Null(operation.RecoveryPointId);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task PreviewAsync_blocks_until_related_processes_stop()
    {
        var root = CreateTempRoot();
        try
        {
            var manager = CreateManager(
                root,
                new StubProcessProbe(["ChatGPT", "cc-switch"]),
                new RecordingOperationJournal());

            var preview = await manager.PreviewCoordinatedMigrationAsync(
                CreateRequest(root));

            Assert.False(preview.CanApply);
            var blocker = Assert.Single(
                preview.Blockers,
                item => item.Code == "process-running");
            Assert.Contains("ChatGPT", blocker.Message);
            Assert.Contains("cc-switch", blocker.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task PreviewAsync_blocks_missing_source_and_existing_destination()
    {
        var root = CreateTempRoot();
        try
        {
            var request = CreateRequest(root);
            Directory.CreateDirectory(request.CodexConfigDestinationPath);
            var manager = CreateManager(
                root,
                new StubProcessProbe([]),
                new RecordingOperationJournal());

            var preview = await manager.PreviewCoordinatedMigrationAsync(
                request);

            Assert.False(preview.CanApply);
            Assert.Contains(
                preview.Blockers,
                item => item.Code == "destination-exists");

            var missing = new CoordinatedMigrationRequest(
                Path.Combine(root, "missing-codex-home"),
                Path.Combine(root, "moved-missing"),
                request.CcSwitchConfigSourcePath,
                request.CcSwitchConfigDestinationPath);
            var missingPreview = await manager.PreviewCoordinatedMigrationAsync(
                missing);

            Assert.False(missingPreview.CanApply);
            Assert.Contains(
                missingPreview.Blockers,
                item => item.Code == "source-missing");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task PreviewAsync_blocks_nested_and_overlapping_paths()
    {
        var root = CreateTempRoot();
        try
        {
            var request = CreateRequest(root);
            var manager = CreateManager(
                root,
                new StubProcessProbe([]),
                new RecordingOperationJournal());

            var nested = await manager.PreviewCoordinatedMigrationAsync(
                request with
                {
                    CodexConfigDestinationPath = Path.Combine(
                        request.CodexConfigSourcePath,
                        "inside")
                });
            var overlapping = await manager.PreviewCoordinatedMigrationAsync(
                request with
                {
                    CcSwitchConfigDestinationPath = Path.Combine(
                        request.CodexConfigDestinationPath,
                        ".cc-switch")
                });

            Assert.Contains(
                nested.Blockers,
                item => item.Code == "destination-inside-source");
            Assert.Contains(
                overlapping.Blockers,
                item => item.Code == "paths-overlap");
            Assert.Null(nested.OperationId);
            Assert.Null(overlapping.OperationId);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task PreviewAsync_reports_blank_paths_as_blockers()
    {
        var root = CreateTempRoot();
        try
        {
            var request = CreateRequest(root);
            var manager = CreateManager(
                root,
                new StubProcessProbe([]),
                new RecordingOperationJournal());

            var preview = await manager.PreviewCoordinatedMigrationAsync(
                request with { CodexConfigSourcePath = "   " });

            Assert.False(preview.CanApply);
            Assert.Contains(
                preview.Blockers,
                item => item.Code == "path-invalid");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static CoordinatedMigrationRequest CreateRequest(string root)
    {
        var codexSource = Path.Combine(root, "codex-home");
        var ccSwitchSource = Path.Combine(root, ".cc-switch");
        Directory.CreateDirectory(codexSource);
        Directory.CreateDirectory(ccSwitchSource);
        return new CoordinatedMigrationRequest(
            codexSource,
            Path.Combine(root, "moved", "codex-home"),
            ccSwitchSource,
            Path.Combine(root, "moved", ".cc-switch"));
    }

    private static EnvironmentManager CreateManager(
        string root,
        IProcessControlProbe processProbe,
        RecordingOperationJournal journal)
    {
        return new EnvironmentManager(
            new StubEnvironmentProbe(new EnvironmentProbeResult([], [])),
            operationJournal: journal,
            managerPaths: ManagerPaths.Resolve(
                Path.Combine(root, "state"),
                Path.Combine(root, "data")),
            processControlProbe: processProbe);
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"agent-env-manager-coordinated-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    private sealed class StubProcessProbe(IReadOnlyList<string> running)
        : IProcessControlProbe
    {
        public Task<IReadOnlyList<string>> FindRunningProcessesAsync(
            IReadOnlyList<string> processNames,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<string>>(
                running
                    .Where(name => processNames.Contains(
                        name,
                        StringComparer.OrdinalIgnoreCase))
                    .ToArray());
        }
    }

    private sealed class StubEnvironmentProbe(EnvironmentProbeResult result)
        : IEnvironmentProbe
    {
        public Task<EnvironmentProbeResult> ProbeAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(result);
        }
    }
}
