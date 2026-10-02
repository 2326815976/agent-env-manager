using AgentEnvManager.Core.Inspection;
using AgentEnvManager.Core.Migrations;
using AgentEnvManager.Core.Operations;
using AgentEnvManager.Core.Storage;
using AgentEnvManager.Core.Tests.TestSupport;

namespace AgentEnvManager.Core.Tests.Migration;

public sealed class CoordinatedMigrationTests
{
    [Fact]
    public async Task ApplyAsync_rewires_compatibility_junctions()
    {
        var root = CreateTempRoot();
        try
        {
            var codexLink = Path.Combine(root, "links", "Codex");
            var ccLink = Path.Combine(root, "links", "com.ccswitch.desktop");
            var request = CreateRequest(root) with
            {
                CompatibilityJunctions =
                [
                    new CoordinatedJunctionTarget(
                        codexLink,
                        Path.Combine(root, "moved", "codex-home")),
                    new CoordinatedJunctionTarget(
                        ccLink,
                        Path.Combine(
                            root,
                            "moved",
                            ".cc-switch",
                            "appdata",
                            "Local",
                            "com.ccswitch.desktop"))
                ]
            };
            var link = new RecordingActivationLink();
            await link.SetTargetAsync(codexLink, @"E:\Old\.codex");
            await link.SetTargetAsync(ccLink, @"E:\Old\.cc-switch");
            var manager = CreateManager(
                root,
                new StubProcessProbe([]),
                new RecordingOperationJournal(),
                link: link);

            var preview = await manager.PreviewCoordinatedMigrationAsync(
                request);
            var result = await manager.ApplyCoordinatedMigrationAsync(
                preview);

            Assert.Equal(2, preview.JunctionsToRewire.Count);
            Assert.Equal(
                [codexLink, ccLink],
                result.RewiredJunctions);
            Assert.Equal(
                link.GetActivationTarget(
                    Path.Combine(root, "moved", "codex-home")),
                await link.GetTargetAsync(codexLink));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ApplyAsync_restores_junctions_when_rewiring_fails()
    {
        var root = CreateTempRoot();
        try
        {
            var firstLink = Path.Combine(root, "links", "Codex");
            var secondLink = Path.Combine(root, "links", "second");
            var request = CreateRequest(root) with
            {
                CompatibilityJunctions =
                [
                    new CoordinatedJunctionTarget(
                        firstLink,
                        Path.Combine(root, "moved", "codex-home")),
                    new CoordinatedJunctionTarget(
                        secondLink,
                        Path.Combine(root, "moved", ".cc-switch"))
                ]
            };
            var link = new RecordingActivationLink
            {
                ThrowBeforeSetNumber = 2
            };
            await link.SetTargetAsync(firstLink, @"E:\Old\.codex");
            var store = new RecordingUserEnvironmentVariableStore(
                new Dictionary<string, string?>
                {
                    ["CODEX_HOME"] = @"E:\Old\.codex"
                });
            var manager = CreateManager(
                root,
                new StubProcessProbe([]),
                new RecordingOperationJournal(),
                store,
                link);
            var preview = await manager.PreviewCoordinatedMigrationAsync(
                request);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => manager.ApplyCoordinatedMigrationAsync(preview));

            Assert.Contains("已恢复原状态", exception.Message);
            Assert.Equal(
                link.GetActivationTarget(@"E:\Old\.codex"),
                await link.GetTargetAsync(firstLink));
            Assert.Equal(@"E:\Old\.codex", await store.GetAsync("CODEX_HOME"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ApplyAsync_copies_verifies_and_rewrites_paths()
    {
        var root = CreateTempRoot();
        try
        {
            var request = CreateRequest(root);
            await File.WriteAllTextAsync(
                Path.Combine(request.CodexConfigSourcePath, "config.toml"),
                "model = \"gpt\"\n");
            Directory.CreateDirectory(
                Path.Combine(request.CcSwitchConfigSourcePath, "appdata"));
            await File.WriteAllTextAsync(
                Path.Combine(
                    request.CcSwitchConfigSourcePath,
                    "settings.json"),
                """
                {
                  // 注释保留
                  "codexConfigDir": "E:\\Old\\.codex",
                  "providerSecrets": "sk-x"
                }
                """);
            var store = new RecordingUserEnvironmentVariableStore(
                new Dictionary<string, string?>
                {
                    ["CODEX_HOME"] = @"E:\Old\.codex"
                });
            var journal = new RecordingOperationJournal();
            var probe = new StubProcessProbe([]);
            var manager = CreateManager(root, probe, journal, store);
            var preview = await manager.PreviewCoordinatedMigrationAsync(
                request);

            var result = await manager.ApplyCoordinatedMigrationAsync(
                preview);

            Assert.Equal(OperationState.Succeeded, result.Operation.State);
            Assert.True(result.SourcesRetained);
            Assert.Single(probe.StopHistory);
            Assert.True(Directory.Exists(request.CodexConfigSourcePath));
            Assert.True(Directory.Exists(
                Path.Combine(
                    request.CodexConfigDestinationPath,
                    "config.toml").Replace("config.toml", string.Empty)));
            Assert.Equal(
                request.CodexConfigDestinationPath,
                await store.GetAsync("CODEX_HOME"));
            var settings = await File.ReadAllTextAsync(Path.Combine(
                request.CcSwitchConfigDestinationPath,
                "settings.json"));
            Assert.Contains("注释保留", settings);
            Assert.Contains("sk-x", settings);
            Assert.Contains(
                request.CodexConfigDestinationPath.Replace("\\", "\\\\"),
                settings);
            Assert.Contains("CODEX_HOME", result.RewrittenPaths);
            Assert.Contains("codexConfigDir", result.RewrittenPaths);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ApplyAsync_restores_environment_and_destinations_on_failure()
    {
        var root = CreateTempRoot();
        try
        {
            var request = CreateRequest(root);
            var store = new RecordingUserEnvironmentVariableStore(
                new Dictionary<string, string?>
                {
                    ["CODEX_HOME"] = @"E:\Old\.codex"
                });
            var journal = new RecordingOperationJournal();
            var manager = CreateManager(
                root,
                new StubProcessProbe([]),
                journal,
                store);
            var preview = await manager.PreviewCoordinatedMigrationAsync(
                request);
            Directory.Delete(request.CcSwitchConfigSourcePath, recursive: true);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => manager.ApplyCoordinatedMigrationAsync(preview));

            Assert.Contains("已恢复原状态", exception.Message);
            Assert.Equal(@"E:\Old\.codex", await store.GetAsync("CODEX_HOME"));
            Assert.False(Directory.Exists(
                request.CodexConfigDestinationPath));
            Assert.False(Directory.Exists(
                request.CcSwitchConfigDestinationPath));
            var operation = journal.History
                .Where(record => record.Id == preview.OperationId)
                .GroupBy(record => record.Id)
                .Select(group => group.Last())
                .Single();
            Assert.Equal(OperationState.RolledBack, operation.State);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

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
        RecordingOperationJournal journal,
        RecordingUserEnvironmentVariableStore? userEnvironmentVariableStore
            = null,
        RecordingActivationLink? link = null)
    {
        return new EnvironmentManager(
            new StubEnvironmentProbe(new EnvironmentProbeResult([], [])),
            operationJournal: journal,
            userEnvironmentVariableStore: userEnvironmentVariableStore,
            activationLink: link,
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

        public List<IReadOnlyList<string>> StopHistory { get; } = [];

        public Task StopProcessesAsync(
            IReadOnlyList<string> processNames,
            CancellationToken cancellationToken = default)
        {
            StopHistory.Add(processNames.ToArray());
            return Task.CompletedTask;
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
