using AgentEnvManager.Core.EnvironmentVariables;
using AgentEnvManager.Wpf;

namespace AgentEnvManager.Wpf.Tests;

public sealed class EnvironmentVariablesViewModelTests
{
    [Fact]
    public async Task LoadAndApply_previews_then_uses_reviewed_plan()
    {
        var client = new StubClient();
        var viewModel = new EnvironmentVariablesViewModel(client);

        await viewModel.LoadAsync();
        Assert.Single(viewModel.PathEntries);
        Assert.Single(viewModel.Variables);

        viewModel.PathEntries[0].IsEnabled = true;
        viewModel.Variables[0].Value = "project";
        await viewModel.PreviewAsync();

        Assert.Contains("PATH", viewModel.PreviewImpact);
        Assert.Equal(
            "应用后创建恢复点",
            viewModel.PendingRecoveryPoint);

        await viewModel.ApplyAsync();

        Assert.True(client.ApplyCalls > 0);
        Assert.Equal(
            "recovery-variables",
            viewModel.PendingRecoveryPoint);
        Assert.Contains("已应用", viewModel.StatusMessage);
    }

    [Fact]
    public async Task PreviewAsync_sends_null_change_for_removed_variable()
    {
        var client = new StubClient();
        var viewModel = new EnvironmentVariablesViewModel(client);
        await viewModel.LoadAsync();
        viewModel.SelectedVariable = Assert.Single(viewModel.Variables);

        viewModel.RemoveSelectedVariable();
        await viewModel.PreviewAsync();

        Assert.Contains(
            client.LastVariableChanges!,
            change => change.Name == "AGENT_ENV_MANAGER_MODE"
                && change.Value is null);
    }

    private sealed class StubClient : StubEnvironmentManagerClient
    {
        public int ApplyCalls { get; private set; }

        public IReadOnlyList<EnvironmentVariableChange>? LastVariableChanges
        {
            get;
            private set;
        }

        public override Task<EnvironmentVariableEditorSnapshot>
            InspectEnvironmentVariableEditorAsync(
                CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                new EnvironmentVariableEditorSnapshot(
                    @"C:\Tools",
                    [
                        new EnvironmentVariableEditorPathEntry(
                            "Node.js",
                            "24.1.0",
                            @"D:\runtime-shims\node",
                            IsEnabled: false)
                    ],
                    [
                        new EnvironmentVariableEditorVariable(
                            "AGENT_ENV_MANAGER_MODE",
                            "system",
                            IsExpandable: false)
                    ]));
        }

        public override Task<EnvironmentVariableUpdatePreview>
            PreviewManagedEnvironmentUpdateAsync(
                IReadOnlyList<string>? managedEntries,
                IReadOnlyList<EnvironmentVariableChange>? variableChanges,
                CancellationToken cancellationToken = default)
        {
            LastVariableChanges = variableChanges?.ToArray();
            return Task.FromResult(new EnvironmentVariableUpdatePreview(
                new Dictionary<string, string?>
                {
                    ["Path"] = @"C:\Tools",
                    ["AGENT_ENV_MANAGER_MODE"] = "system"
                },
                new Dictionary<string, string?>
                {
                    ["Path"] = @"C:\Tools;D:\runtime-shims\node",
                    ["AGENT_ENV_MANAGER_MODE"] = "project"
                },
                [
                    new EnvironmentVariableChange(
                        "Path",
                        @"C:\Tools;D:\runtime-shims\node"),
                    new EnvironmentVariableChange(
                        "AGENT_ENV_MANAGER_MODE",
                        "project")
                ],
                "更新 PATH 和 1 个受管变量。",
                authorizationToken: "authorized"));
        }

        public override Task<EnvironmentVariableTransactionResult>
            ApplyEnvironmentVariableUpdateAsync(
                EnvironmentVariableUpdatePreview preview,
                CancellationToken cancellationToken = default)
        {
            ApplyCalls++;
            return Task.FromResult(new EnvironmentVariableTransactionResult(
                new Core.Operations.OperationRecord(
                    "variables-1",
                    Core.Operations.OperationType.EnvironmentVariables,
                    Core.Operations.OperationState.Succeeded,
                    DateTimeOffset.UnixEpoch,
                    DateTimeOffset.UnixEpoch,
                    "更新环境变量"),
                new EnvironmentVariableRecoveryPoint(
                    "recovery-variables",
                    preview.OriginalValues,
                    DateTimeOffset.UnixEpoch)));
        }
    }
}
