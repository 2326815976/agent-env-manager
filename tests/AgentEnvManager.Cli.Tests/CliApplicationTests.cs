using AgentEnvManager.Cli;
using AgentEnvManager.Core.Adoption;
using AgentEnvManager.Core.Agents;
using AgentEnvManager.Core.EnvironmentVariables;
using AgentEnvManager.Core.Inspection;
using AgentEnvManager.Core.Migrations;
using AgentEnvManager.Core.Operations;
using AgentEnvManager.Core.Runtimes;

namespace AgentEnvManager.Cli.Tests;

public sealed class CliApplicationTests
{
    [Fact]
    public async Task EnvironmentVariables_render_owned_external_and_machine_entries()
    {
        var manager = new FakeCliEnvironmentManager
        {
            EnvironmentVariableSnapshot = new EnvironmentVariableEditorSnapshot(
                @"C:\Tools;C:\shims\node",
                [
                    new EnvironmentVariableEditorPathEntry(
                        "Node.js",
                        "24.1.0",
                        @"C:\shims\node",
                        IsEnabled: true,
                        IsManaged: true),
                    new EnvironmentVariableEditorPathEntry(
                        @"C:\Tools",
                        Version: null,
                        @"C:\Tools",
                        IsEnabled: true,
                        IsManaged: false)
                ],
                [
                    new EnvironmentVariableEditorVariable(
                        "AGENT_ENV_MANAGER_MODE",
                        "system",
                        IsExpandable: false,
                        IsManaged: true),
                    new EnvironmentVariableEditorVariable(
                        "PATHEXT",
                        ".COM;.EXE",
                        IsExpandable: false,
                        IsManaged: false,
                        IsHighRisk: true)
                ],
                [
                    new EnvironmentVariableEditorVariable(
                        "PROCESSOR_ARCHITECTURE",
                        "AMD64",
                        IsExpandable: false)
                ],
                "机器级环境变量在本版本中只读。")
        };
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await CliApplication.RunWithManagerAsync(
            ["env-vars"],
            () => manager,
            output,
            error);

        var text = output.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains(@"[受管] C:\shims\node", text);
        Assert.Contains(@"[外部/只读] C:\Tools", text);
        Assert.Contains("[受管] AGENT_ENV_MANAGER_MODE=******", text);
        Assert.Contains("[外部/只读] PATHEXT=******（高风险）", text);
        Assert.Contains("[只读] PROCESSOR_ARCHITECTURE=******", text);
        Assert.Contains("只读", text);
        Assert.DoesNotContain("system", text);
        Assert.DoesNotContain(".COM;.EXE", text);
        Assert.DoesNotContain("AMD64", text);
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public async Task EnvironmentVariables_show_secrets_renders_values()
    {
        var manager = new FakeCliEnvironmentManager
        {
            EnvironmentVariableSnapshot = new EnvironmentVariableEditorSnapshot(
                @"C:\shims\node",
                [],
                [
                    new EnvironmentVariableEditorVariable(
                        "AGENT_ENV_MANAGER_MODE",
                        "system",
                        IsExpandable: false,
                        IsManaged: true)
                ],
                [],
                "机器级环境变量在本版本中只读。")
        };
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await CliApplication.RunWithManagerAsync(
            ["env-vars", "--show-secrets"],
            () => manager,
            output,
            error);

        Assert.Equal(0, exitCode);
        Assert.Contains("[受管] AGENT_ENV_MANAGER_MODE=system", output.ToString());
    }

    [Fact]
    public async Task EnvironmentVariablesApply_without_confirmation_renders_preview_only()
    {
        var manager = new FakeCliEnvironmentManager
        {
            EnvironmentVariablePreview = CreateEnvironmentVariablePreview()
        };
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await CliApplication.RunWithManagerAsync(
            [
                "env-vars-apply",
                "--set",
                "AGENT_ENV_MANAGER_MODE=project"
            ],
            () => manager,
            output,
            error);

        Assert.Equal(2, exitCode);
        Assert.Equal(0, manager.VariableApplyCalls);
        Assert.Equal(
            "AGENT_ENV_MANAGER_MODE",
            Assert.Single(manager.LastVariableChanges!).Name);
        Assert.Contains("环境变量变更预览", output.ToString());
        Assert.Contains("影响范围:", output.ToString());
        Assert.Contains("--confirm", error.ToString());
    }

    [Fact]
    public async Task EnvironmentVariablesApply_with_confirmation_applies_preview()
    {
        var preview = CreateEnvironmentVariablePreview();
        var manager = new FakeCliEnvironmentManager
        {
            EnvironmentVariablePreview = preview,
            EnvironmentVariableResult = new EnvironmentVariableTransactionResult(
                new OperationRecord(
                    "variables-1",
                    OperationType.EnvironmentVariables,
                    OperationState.Succeeded,
                    DateTimeOffset.UnixEpoch,
                    DateTimeOffset.UnixEpoch,
                    "更新 1 个环境变量"),
                new EnvironmentVariableRecoveryPoint(
                    "recovery-variables",
                    preview.OriginalValues,
                    DateTimeOffset.UnixEpoch))
        };
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await CliApplication.RunWithManagerAsync(
            [
                "env-vars-apply",
                "--managed-path",
                @"C:\shims\node",
                "--confirm"
            ],
            () => manager,
            output,
            error);

        Assert.Equal(0, exitCode);
        Assert.Equal(1, manager.VariableApplyCalls);
        Assert.Equal(
            @"C:\shims\node",
            Assert.Single(manager.LastManagedEntries!));
        Assert.Contains("环境变量已应用", output.ToString());
        Assert.Contains("操作 ID: variables-1", output.ToString());
        Assert.Contains("恢复点: recovery-variables", output.ToString());
        Assert.Equal(string.Empty, error.ToString());
    }

    private static EnvironmentVariableUpdatePreview
        CreateEnvironmentVariablePreview()
    {
        return new EnvironmentVariableUpdatePreview(
            new Dictionary<string, string?>
            {
                ["AGENT_ENV_MANAGER_MODE"] = "system"
            },
            new Dictionary<string, string?>
            {
                ["AGENT_ENV_MANAGER_MODE"] = "project"
            },
            [
                new EnvironmentVariableChange(
                    "AGENT_ENV_MANAGER_MODE",
                    "project")
            ],
            "只修改以 AGENT_ENV_MANAGER_ 开头的受管变量。");
    }

    [Fact]
    public async Task RuntimeInstall_without_confirmation_renders_preview_only()
    {
        var preview = CreateRuntimeInstallPreview();
        var manager = new FakeCliEnvironmentManager
        {
            RuntimeInstallPreview = preview
        };
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await CliApplication.RunWithManagerAsync(
            [
                "runtime-install",
                "python",
                "3.13.7",
                "--mirror",
                "https://mirror.test/python",
                "--install-dir",
                @"D:\AgentRuntimes\python-3.13.7"
            ],
            () => manager,
            output,
            error);

        Assert.Equal(2, exitCode);
        Assert.Contains("Python 3.13.7", output.ToString());
        Assert.Contains("影响范围: 安装 Python 3.13.7", output.ToString());
        Assert.Contains("操作 ID: op-install", output.ToString());
        Assert.Contains("恢复点: recovery-install", output.ToString());
        Assert.Equal(0, manager.InstallCalls);
        Assert.Equal("python", manager.LastRuntimeProviderId);
        Assert.Equal("3.13.7", manager.LastRuntimeVersion);
        Assert.Equal(
            "https://mirror.test/python",
            manager.LastRuntimeMirrorUrl);
        Assert.Equal(
            @"D:\AgentRuntimes\python-3.13.7",
            manager.LastRuntimeInstallRoot);
        Assert.Contains("--confirm", error.ToString());
    }

    [Fact]
    public async Task RuntimeList_renders_providers_and_versions()
    {
        var manager = new FakeCliEnvironmentManager
        {
            RuntimeProviders =
            [
                new PythonRuntimeProvider().Descriptor
            ]
        };
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await CliApplication.RunWithManagerAsync(
            ["runtimes"],
            () => manager,
            output,
            error);

        Assert.Equal(0, exitCode);
        Assert.Contains("Python (python)", output.ToString());
        Assert.Contains("[可安装]", output.ToString());
        Assert.Contains("3.13.7", output.ToString());
    }

    [Fact]
    public async Task Adopt_without_confirmation_renders_preview_only()
    {
        var manager = new FakeCliEnvironmentManager
        {
            AdoptionPreview = new AdoptionPreview(
                new EnvironmentFingerprint("node-24"),
                new EnvironmentIdentity("node-24"),
                new EnvironmentAsset(
                    EnvironmentAssetKind.ToolRuntime,
                    "Node.js",
                    "24.1.0",
                    @"C:\runtimes\node",
                    IsSystemComponent: false,
                    DiscoverySourceInfo.PathCommand),
                "asset-hash",
                @"C:\activation\node\current",
                "纳管 Node.js 24.1.0。",
                IsAlreadyManaged: false,
                ExistingIdentity: null,
                ManagedEntryPath: @"C:\shims\node")
        };
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await CliApplication.RunWithManagerAsync(
            ["adopt", "node-24"],
            () => manager,
            output,
            error);

        Assert.Equal(2, exitCode);
        Assert.Equal(0, manager.AdoptCalls);
        Assert.Contains("纳管前预览", output.ToString());
        Assert.Contains("--confirm", error.ToString());
    }

    [Fact]
    public async Task RuntimeInstall_with_confirmation_installs_preview()
    {
        var preview = CreateRuntimeInstallPreview();
        var manager = new FakeCliEnvironmentManager
        {
            RuntimeInstallPreview = preview,
            InstalledRuntime = new InstalledRuntime(
                preview.Provider,
                preview.Artifact,
                new EnvironmentManifest(
                    preview.Identity,
                    preview.Fingerprint,
                    preview.Provider.Kind,
                    preview.Provider.Name,
                    preview.Artifact.Version,
                    preview.Provider.Source,
                    preview.Location,
                    preview.StableActivationPath,
                    preview.Artifact.Sha256,
                    preview.RecoveryPointId!,
                    preview.OperationId!,
                    IsSystemComponent: false,
                    DateTimeOffset.UnixEpoch,
                    preview.ActivationIdentity,
                    preview.ManagedEntryPath),
                preview.ExecutablePath,
                preview.ManagedEntryPath)
        };
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await CliApplication.RunWithManagerAsync(
            [
                "runtime-install",
                "python",
                "3.13.7",
                "--confirm"
            ],
            () => manager,
            output,
            error);

        Assert.Equal(0, exitCode);
        Assert.Equal(1, manager.InstallCalls);
        Assert.Contains(
            "安装完成: Python 3.13.7",
            output.ToString());
        Assert.Contains(
            @"可执行文件: C:\data\runtimes\python\3.13.7\python\python.exe",
            output.ToString());
        Assert.Contains(
            @"受管入口: C:\state\shims\python",
            output.ToString());
        Assert.Contains("操作 ID: op-install", output.ToString());
        Assert.Contains("恢复点: recovery-install", output.ToString());
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public async Task RuntimeImport_without_confirmation_does_not_import()
    {
        var manager = new FakeCliEnvironmentManager();
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await CliApplication.RunWithManagerAsync(
            [
                "runtime-import",
                "python",
                "3.13.7",
                @"C:\offline\python.tar.gz"
            ],
            () => manager,
            output,
            error);

        Assert.Equal(2, exitCode);
        Assert.Equal(0, manager.ImportCalls);
        Assert.Contains("--confirm", error.ToString());
    }

    [Fact]
    public async Task RuntimeImport_with_confirmation_renders_reusable_cache_entry()
    {
        var manager = new FakeCliEnvironmentManager
        {
            ImportedArtifact = new RuntimeArtifactCacheEntry(
                @"C:\cache\python.tar.gz",
                "official-sha256",
                RuntimeArtifactSource.Imported,
                @"C:\offline\python.tar.gz",
                null,
                "离线导入制品通过官方 SHA-256 校验。",
                "op-import"),
            Operations =
            [
                new OperationRecord(
                    "op-import",
                    OperationType.ArtifactImport,
                    OperationState.Succeeded,
                    DateTimeOffset.UnixEpoch,
                    DateTimeOffset.UnixEpoch,
                    "导入 Python 3.13.7 制品",
                    RecoveryPointId: "artifact-import-op-import",
                    Target: @"C:\offline\python.tar.gz")
            ]
        };
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await CliApplication.RunWithManagerAsync(
            [
                "runtime-import",
                "python",
                "3.13.7",
                @"C:\offline\python.tar.gz",
                "--confirm"
            ],
            () => manager,
            output,
            error);

        Assert.Equal(0, exitCode);
        Assert.Equal(1, manager.ImportCalls);
        Assert.Contains(@"缓存路径: C:\cache\python.tar.gz", output.ToString());
        Assert.Contains("SHA-256: official-sha256", output.ToString());
        Assert.Contains("操作 ID: op-import", output.ToString());
        Assert.Contains(
            "恢复点: artifact-import-op-import",
            output.ToString());
        Assert.Contains("复用该缓存", output.ToString());
    }

    [Fact]
    public async Task MigrationPreview_renders_plan_without_executing()
    {
        var manager = new FakeCliEnvironmentManager
        {
            MigrationPreview = CreateMigrationPreview()
        };
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await CliApplication.RunWithManagerAsync(
            [
                "migrate-preview",
                "node-runtime-fingerprint",
                @"C:\moved\node"
            ],
            () => manager,
            output,
            error);

        Assert.Equal(0, exitCode);
        Assert.Equal(0, manager.MigrateCalls);
        Assert.Contains(@"源路径: C:\runtimes\node", output.ToString());
        Assert.Contains(@"目标路径: C:\moved\node", output.ToString());
        Assert.Contains("影响范围: 将环境迁移到新路径", output.ToString());
        Assert.Contains("操作 ID: op-migrate", output.ToString());
        Assert.Contains("恢复点: recovery-migrate", output.ToString());
    }

    [Fact]
    public async Task Migrate_without_confirmation_renders_preview_only()
    {
        var manager = new FakeCliEnvironmentManager
        {
            MigrationPreview = CreateMigrationPreview()
        };
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await CliApplication.RunWithManagerAsync(
            [
                "migrate",
                "node-runtime-fingerprint",
                @"C:\moved\node"
            ],
            () => manager,
            output,
            error);

        Assert.Equal(2, exitCode);
        Assert.Equal(0, manager.MigrateCalls);
        Assert.Contains("操作 ID: op-migrate", output.ToString());
        Assert.Contains("--confirm", error.ToString());
    }

    [Fact]
    public async Task Migrate_with_confirmation_executes_previewed_plan()
    {
        var manager = new FakeCliEnvironmentManager
        {
            MigrationPreview = CreateMigrationPreview(),
            MigratedOperation = new OperationRecord(
                "op-migrate",
                OperationType.Migrate,
                OperationState.Succeeded,
                DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch,
                "迁移 Node.js 24.1.0",
                RecoveryPointId: "recovery-migrate",
                Target: @"C:\moved\node",
                Impact: "将环境迁移到新路径。")
        };
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await CliApplication.RunWithManagerAsync(
            [
                "migrate",
                "node-runtime-fingerprint",
                @"C:\moved\node",
                "--confirm"
            ],
            () => manager,
            output,
            error);

        Assert.Equal(0, exitCode);
        Assert.Equal(1, manager.MigrateCalls);
        Assert.Contains("迁移完成: op-migrate", output.ToString());
        Assert.Contains("状态: Succeeded", output.ToString());
        Assert.Contains("恢复点: recovery-migrate", output.ToString());
    }

    [Fact]
    public async Task RollbackPreview_renders_recovery_plan()
    {
        var manager = new FakeCliEnvironmentManager
        {
            RollbackPlan = new OperationRollbackPlan(
                "op-migrate",
                @"C:\runtimes\node",
                "将 C:\\moved\\node 移回 C:\\runtimes\\node。",
                "recovery-migrate",
                "原目录和激活目标恢复完成。")
        };
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await CliApplication.RunWithManagerAsync(
            ["rollback-preview", "op-migrate"],
            () => manager,
            output,
            error);

        Assert.Equal(0, exitCode);
        Assert.Equal(0, manager.RollbackCalls);
        Assert.Contains(@"目标: C:\runtimes\node", output.ToString());
        Assert.Contains("恢复点: recovery-migrate", output.ToString());
        Assert.Contains("影响范围:", output.ToString());
    }

    [Fact]
    public async Task Rollback_without_confirmation_renders_plan_only()
    {
        var manager = new FakeCliEnvironmentManager
        {
            RollbackPlan = new OperationRollbackPlan(
                "op-migrate",
                @"C:\runtimes\node",
                "将 C:\\moved\\node 移回 C:\\runtimes\\node。",
                "recovery-migrate",
                "原目录和激活目标恢复完成。")
        };
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await CliApplication.RunWithManagerAsync(
            ["rollback", "op-migrate"],
            () => manager,
            output,
            error);

        Assert.Equal(2, exitCode);
        Assert.Equal(0, manager.RollbackCalls);
        Assert.Contains("恢复点: recovery-migrate", output.ToString());
        Assert.Contains("--confirm", error.ToString());
    }

    [Fact]
    public async Task Rollback_with_confirmation_executes_plan()
    {
        var manager = new FakeCliEnvironmentManager
        {
            RollbackPlan = new OperationRollbackPlan(
                "op-migrate",
                @"C:\runtimes\node",
                "将 C:\\moved\\node 移回 C:\\runtimes\\node。",
                "recovery-migrate",
                "原目录和激活目标恢复完成。"),
            RolledBackOperation = new OperationRecord(
                "op-migrate",
                OperationType.Migrate,
                OperationState.RolledBack,
                DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch,
                "迁移 Node.js 24.1.0",
                RecoveryPointId: "recovery-migrate",
                FailureReason: null,
                Target: @"C:\runtimes\node")
        };
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await CliApplication.RunWithManagerAsync(
            ["rollback", "op-migrate", "--confirm"],
            () => manager,
            output,
            error);

        Assert.Equal(0, exitCode);
        Assert.Equal(1, manager.RollbackCalls);
        Assert.Contains("操作已回滚: op-migrate", output.ToString());
        Assert.Contains("状态: RolledBack", output.ToString());
    }

    [Fact]
    public async Task AgentDiscover_renders_discovery_result()
    {
        var manager = new FakeCliEnvironmentManager
        {
            AgentDiscovery = new AgentDiscoveryResult(
                IsInstalled: true,
                Home: @"C:\Users\tester\.codex",
                Executable: @"C:\Apps\Codex\codex.exe",
                Message: "已发现 Codex。")
        };
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await CliApplication.RunWithManagerAsync(
            [
                "agent-discover",
                "codex",
                "--config-dir",
                @"C:\Users\tester\.codex",
                "--executable",
                @"C:\Apps\Codex\codex.exe"
            ],
            () => manager,
            output,
            error);

        Assert.Equal(0, exitCode);
        Assert.Equal("codex", manager.LastDiscoveryAgent);
        Assert.Equal(
            @"C:\Users\tester\.codex",
            manager.LastDiscoveryRequest?.ConfigurationDirectory);
        Assert.Equal(
            @"C:\Apps\Codex\codex.exe",
            manager.LastDiscoveryRequest?.Executable);
        Assert.Contains("Agent: codex", output.ToString());
        Assert.Contains("已安装: 是", output.ToString());
        Assert.Contains("已发现 Codex。", output.ToString());
    }

    [Fact]
    public async Task AgentBindPreview_renders_plan_without_binding()
    {
        var manager = new FakeCliEnvironmentManager
        {
            AgentBindingPlan = CreateAgentBindingPlan()
        };
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await CliApplication.RunWithManagerAsync(
            [
                "agent-bind-preview",
                "codex",
                "--config-dir",
                @"C:\Users\tester\.codex",
                "--executable",
                @"C:\Apps\Codex\codex.exe",
                "--managed-entry",
                @"C:\state\shims\node",
                "--runtime",
                "node",
                "--runtime-version",
                "24.1.0",
                "--workspace",
                @"C:\workspace"
            ],
            () => manager,
            output,
            error);

        Assert.Equal(0, exitCode);
        Assert.Equal(0, manager.BindCalls);
        Assert.Equal("codex", manager.LastBindingAgent);
        Assert.Equal("node", manager.LastBindingRequest?.RuntimeName);
        Assert.Equal(
            @"C:\state\shims\node",
            manager.LastBindingRequest?.ManagedEntryPath);
        Assert.Contains("Agent 绑定预览", output.ToString());
        Assert.Contains(
            @"绑定文件: C:\Users\tester\.codex\agent-env-manager.binding.json",
            output.ToString());
        Assert.Contains("影响范围: 仅写入上述 Agent 绑定文件。", output.ToString());
    }

    [Fact]
    public async Task AgentBind_without_confirmation_renders_plan_only()
    {
        var manager = new FakeCliEnvironmentManager
        {
            AgentBindingPlan = CreateAgentBindingPlan()
        };
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await CliApplication.RunWithManagerAsync(
            [
                "agent-bind",
                "codex",
                "--config-dir",
                @"C:\Users\tester\.codex",
                "--executable",
                @"C:\Apps\Codex\codex.exe",
                "--managed-entry",
                @"C:\state\shims\node",
                "--runtime",
                "node",
                "--runtime-version",
                "24.1.0"
            ],
            () => manager,
            output,
            error);

        Assert.Equal(2, exitCode);
        Assert.Equal(0, manager.BindCalls);
        Assert.Contains("Agent 绑定预览", output.ToString());
        Assert.Contains("--confirm", error.ToString());
    }

    [Fact]
    public async Task AgentBind_with_confirmation_renders_binding_operation()
    {
        var plan = CreateAgentBindingPlan();
        var recoveryPoint = new AgentConfigurationRecoveryPoint(
            "recovery-bind",
            plan.AgentName,
            [],
            DateTimeOffset.UnixEpoch);
        var manager = new FakeCliEnvironmentManager
        {
            AgentBindingPlan = plan,
            AgentBinding = new AgentBinding(
                plan.AgentName,
                plan.ConfigurationDirectory,
                plan.Executable,
                plan.ManagedEntryPath,
                plan.RuntimeName,
                plan.RuntimeVersion,
                plan.WorkspacePath,
                plan.RuntimeCommand,
                plan.HealthArguments,
                plan.BindingFilePath,
                recoveryPoint,
                IsHealthy: true),
            Operations =
            [
                new OperationRecord(
                    "op-bind",
                    OperationType.AgentBinding,
                    OperationState.Succeeded,
                    DateTimeOffset.UnixEpoch,
                    DateTimeOffset.UnixEpoch,
                    "绑定 Codex 到 node",
                    RecoveryPointId: "recovery-bind",
                    Target: plan.ConfigurationDirectory)
            ]
        };
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await CliApplication.RunWithManagerAsync(
            [
                "agent-bind",
                "codex",
                "--config-dir",
                plan.ConfigurationDirectory,
                "--executable",
                plan.Executable,
                "--managed-entry",
                plan.ManagedEntryPath,
                "--runtime",
                plan.RuntimeName,
                "--runtime-version",
                plan.RuntimeVersion,
                "--confirm"
            ],
            () => manager,
            output,
            error);

        Assert.Equal(0, exitCode);
        Assert.Equal(1, manager.BindCalls);
        Assert.Contains("Agent 绑定完成: Codex", output.ToString());
        Assert.Contains("健康状态: 健康", output.ToString());
        Assert.Contains("操作 ID: op-bind", output.ToString());
        Assert.Contains("恢复点: recovery-bind", output.ToString());
    }

    [Fact]
    public async Task AgentHealth_checks_binding_without_writing_configuration()
    {
        var root = CreateTempDirectory();
        try
        {
            var plan = CreateAgentBindingPlan(root);
            await File.WriteAllTextAsync(plan.BindingFilePath, "{}");
            var manager = new FakeCliEnvironmentManager
            {
                AgentBindingPlan = plan,
                AgentHealth = new AgentHealthCheckResult(
                    IsHealthy: true,
                    Message: "Codex 已调用 node 24.1.0。")
            };
            var output = new StringWriter();
            var error = new StringWriter();

            var exitCode = await CliApplication.RunWithManagerAsync(
                [
                    "agent-health",
                    "codex",
                    "--config-dir",
                    plan.ConfigurationDirectory,
                    "--executable",
                    plan.Executable,
                    "--managed-entry",
                    plan.ManagedEntryPath,
                    "--runtime",
                    plan.RuntimeName,
                    "--runtime-version",
                    plan.RuntimeVersion
                ],
                () => manager,
                output,
                error);

            Assert.Equal(0, exitCode);
            Assert.Equal(0, manager.BindCalls);
            Assert.Equal(1, manager.HealthCalls);
            Assert.Equal("codex", manager.LastHealthAgent);
            Assert.Equal(
                plan.ManagedEntryPath,
                manager.LastHealthBinding?.ManagedEntryPath);
            Assert.Contains("Agent 健康检查: codex", output.ToString());
            Assert.Contains("健康状态: 健康", output.ToString());
            Assert.Contains("Codex 已调用 node 24.1.0。", output.ToString());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task AgentHealth_without_existing_binding_fails_before_check()
    {
        var root = CreateTempDirectory();
        try
        {
            var manager = new FakeCliEnvironmentManager
            {
                AgentBindingPlan = CreateAgentBindingPlan(root)
            };
            var output = new StringWriter();
            var error = new StringWriter();

            var exitCode = await CliApplication.RunWithManagerAsync(
                [
                    "agent-health",
                    "codex",
                    "--config-dir",
                    root,
                    "--executable",
                    @"C:\Apps\Codex\codex.exe",
                    "--managed-entry",
                    @"C:\state\shims\node",
                    "--runtime",
                    "node",
                    "--runtime-version",
                    "24.1.0"
                ],
                () => manager,
                output,
                error);

            Assert.Equal(1, exitCode);
            Assert.Equal(0, manager.HealthCalls);
            Assert.Contains("先执行 agent-bind", error.ToString());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static RuntimeInstallPreview CreateRuntimeInstallPreview()
    {
        var provider = new PythonRuntimeProvider().Descriptor;
        var artifact = provider.Artifacts.Single(
            candidate => candidate.Version == "3.13.7");
        return new RuntimeInstallPreview(
            provider,
            artifact,
            new EnvironmentIdentity("runtime-python-3.13.7-win-x64"),
            new EnvironmentFingerprint("runtime-python-3.13.7-win-x64"),
            @"C:\data\runtimes\python\3.13.7",
            @"C:\data\runtimes\python\3.13.7\python",
            @"C:\data\runtimes\python\3.13.7\python\python.exe",
            "python-activation",
            @"C:\state\activation\python\current",
            @"C:\state\shims\python",
            "安装 Python 3.13.7 到 C:\\data\\runtimes\\python\\3.13.7。",
            IsAlreadyInstalled: false,
            OperationId: "op-install",
            RecoveryPointId: "recovery-install");
    }

    private static MigrationPreview CreateMigrationPreview()
    {
        var manifest = new EnvironmentManifest(
            new EnvironmentIdentity("node-runtime"),
            new EnvironmentFingerprint("node-runtime-fingerprint"),
            EnvironmentAssetKind.ToolRuntime,
            "Node.js",
            "24.1.0",
            DiscoverySourceInfo.PathCommand,
            @"C:\runtimes\node",
            @"C:\activation\node\current",
            "asset-hash",
            "recovery-adopt",
            "op-adopt",
            IsSystemComponent: false,
            DateTimeOffset.UnixEpoch,
            "node-activation",
            @"C:\shims\node");
        return new MigrationPreview(
            manifest.Fingerprint,
            manifest,
            manifest.Location,
            @"C:\moved\node",
            manifest.StableActivationPath,
            manifest.ManagedEntryPath!,
            manifest.Location,
            WasActive: true,
            MigrationStrategy.AtomicRename,
            new MigrationPathStatistics(120, 4096, 1_000_000),
            "将环境迁移到新路径。",
            "迁移后健康检查通过。",
            "op-migrate",
            "recovery-migrate");
    }

    private static AgentBindingPlan CreateAgentBindingPlan(
        string configurationDirectory = @"C:\Users\tester\.codex")
    {
        var bindingFilePath = Path.Combine(
            configurationDirectory,
            "agent-env-manager.binding.json");
        return new AgentBindingPlan(
            "Codex",
            configurationDirectory,
            @"C:\Apps\Codex\codex.exe",
            @"C:\state\shims\node",
            "node",
            "24.1.0",
            @"C:\workspace",
            "node",
            ["exec", "--json"],
            bindingFilePath,
            """{"runtime":"node","version":"24.1.0"}""");
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"agent-env-manager-cli-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
