using AgentEnvManager.Core.Migrations;
using AgentEnvManager.Core.Operations;
using AgentEnvManager.Wpf;

namespace AgentEnvManager.Wpf.Tests;

public sealed class CoordinatedMigrationViewModelTests
{
    [Fact]
    public async Task PreviewThenApply_reports_blockers_and_result()
    {
        var client = new StubClient();
        var refreshCount = 0;
        var viewModel = new CoordinatedMigrationViewModel(
            client,
            () =>
            {
                refreshCount++;
                return Task.CompletedTask;
            })
        {
            CodexSource = @"E:\Codex\.codex",
            CodexDestination = @"E:\Moved\.codex",
            CcSwitchSource = @"E:\Codex\.cc-switch",
            CcSwitchDestination = @"E:\Moved\.cc-switch"
        };
        Assert.False(viewModel.ApplyCommand.CanExecute(null));

        await viewModel.PreviewAsync();

        Assert.Contains("无阻断项", viewModel.BlockersLabel);
        Assert.Contains("原路径保留", viewModel.Impact);
        Assert.Contains("CODEX_HOME", viewModel.PlannedRewritesLabel);
        Assert.Contains("CC Switch", viewModel.StartupOrderLabel);
        Assert.Equal("operation-migrate", viewModel.PendingOperation);
        Assert.Equal("执行阶段创建", viewModel.PendingRecoveryPoint);
        Assert.True(viewModel.ApplyCommand.CanExecute(null));

        await viewModel.ApplyAsync();

        Assert.Equal(1, client.ApplyCalls);
        Assert.Equal(1, refreshCount);
        Assert.Contains("操作 ID: operation-migrate", viewModel.Result);
        Assert.Contains("源目录已隔离", viewModel.Result);
    }

    [Fact]
    public async Task PreviewAsync_disables_apply_when_blockers_exist()
    {
        var client = new StubClient
        {
            Blockers =
            [
                new CoordinatedMigrationBlocker(
                    "process-running",
                    "迁移前必须停止相关进程: ChatGPT。")
            ]
        };
        var viewModel = new CoordinatedMigrationViewModel(client)
        {
            CodexSource = @"E:\Codex\.codex",
            CodexDestination = @"E:\Moved\.codex",
            CcSwitchSource = @"E:\Codex\.cc-switch",
            CcSwitchDestination = @"E:\Moved\.cc-switch"
        };

        await viewModel.PreviewAsync();

        Assert.Contains("[process-running]", viewModel.BlockersLabel);
        Assert.False(viewModel.ApplyCommand.CanExecute(null));
    }

    private sealed class StubClient : ICoordinatedMigrationClient
    {
        public int ApplyCalls { get; private set; }

        public IReadOnlyList<CoordinatedMigrationBlocker> Blockers { get; init; } =
            [];

        public Task<CoordinatedMigrationPreview>
            PreviewCoordinatedMigrationAsync(
                CoordinatedMigrationRequest request,
                CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new CoordinatedMigrationPreview(
                [
                    new CoordinatedMigrationTarget(
                        "codex-config",
                        "Codex 配置环境",
                        request.CodexConfigSourcePath,
                        request.CodexConfigDestinationPath),
                    new CoordinatedMigrationTarget(
                        "cc-switch-config",
                        "CC Switch 配置环境",
                        request.CcSwitchConfigSourcePath,
                        request.CcSwitchConfigDestinationPath)
                ],
                [
                    new MigrationProcessRequirement(
                        "chatgpt",
                        "ChatGPT",
                        ["ChatGPT"])
                ],
                ["用户环境变量 CODEX_HOME", "ChatGPT 开始菜单快捷方式"],
                [
                    "先启动 CC Switch，等待其配置写入完成",
                    "再启动 ChatGPT，并执行真实工具调用健康检查"
                ],
                [],
                null,
                null,
                Blockers,
                "默认复制并校验，原路径保留到健康检查通过。",
                "operation-migrate",
                null));
        }

        public Task<CoordinatedMigrationResult> ApplyCoordinatedMigrationAsync(
            CoordinatedMigrationPreview preview,
            CancellationToken cancellationToken = default)
        {
            ApplyCalls++;
            return Task.FromResult(new CoordinatedMigrationResult(
                new OperationRecord(
                    "operation-migrate",
                    OperationType.Migrate,
                    OperationState.Succeeded,
                    DateTimeOffset.UnixEpoch,
                    DateTimeOffset.UnixEpoch,
                    "协同迁移"),
                preview.Targets
                    .Select(target => target.DestinationPath)
                    .ToArray(),
                ["CODEX_HOME", "codexConfigDir"],
                [],
                ["quarantine-1", "quarantine-2"],
                SourcesRetained: false));
        }
    }
}
