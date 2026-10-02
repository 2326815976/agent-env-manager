using AgentEnvManager.Core.Agents;
using AgentEnvManager.Core.Operations;
using AgentEnvManager.Wpf;

namespace AgentEnvManager.Wpf.Tests;

public sealed class CcSwitchViewModelTests
{
    [Fact]
    public async Task LoadAsync_displays_discovery_and_webview_junctions()
    {
        var client = new StubClient();
        var viewModel = new CcSwitchViewModel(client);

        await viewModel.LoadAsync();

        Assert.Equal(
            @"E:\Codex\.cc-switch",
            viewModel.ConfigRoot);
        Assert.Equal(
            @"D:\Software\CCSwitch\cc-switch.exe",
            viewModel.ExecutableLabel);
        Assert.Equal("3.20.4", viewModel.VersionLabel);
        Assert.Equal(
            @"E:\Codex\.codex",
            viewModel.CodexConfigDirectoryLabel);
        Assert.Contains("com.ccswitch.desktop", viewModel.JunctionsLabel);
        Assert.Equal(
            @"E:\Codex\.codex",
            viewModel.TargetCodexHome);
    }

    [Fact]
    public async Task PreviewThenApply_writes_target_and_refreshes_operations()
    {
        var client = new StubClient();
        var refreshCount = 0;
        var viewModel = new CcSwitchViewModel(
            client,
            () =>
            {
                refreshCount++;
                return Task.CompletedTask;
            });
        await viewModel.LoadAsync();
        viewModel.TargetCodexHome = @"E:\Moved\.codex";
        Assert.False(viewModel.ApplyCommand.CanExecute(null));

        await viewModel.PreviewAsync();

        Assert.Equal(0, client.ApplyCalls);
        Assert.Contains("codexConfigDir", viewModel.PreviewImpact);
        Assert.Contains("provider", viewModel.PreservedContentLabel);
        Assert.Contains(
            "com.ccswitch.desktop",
            viewModel.JunctionsLabel);
        Assert.Equal("recovery-1", viewModel.PendingRecoveryPoint);
        Assert.True(viewModel.ApplyCommand.CanExecute(null));

        await viewModel.ApplyAsync();

        Assert.Equal(1, client.ApplyCalls);
        Assert.Equal(1, refreshCount);
        Assert.Contains("操作 ID: operation-1", viewModel.BindingResult);
        Assert.Contains("恢复点: recovery-1", viewModel.BindingResult);
        Assert.Equal(@"E:\Moved\.codex", viewModel.CodexConfigDirectoryLabel);
    }

    private sealed class StubClient : ICcSwitchClient
    {
        public int ApplyCalls { get; private set; }

        public Task<CcSwitchDiscovery> DiscoverCcSwitchAsync(
            CcSwitchDiscoveryRequest? request = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new CcSwitchDiscovery(
                IsInstalled: true,
                Executable: @"D:\Software\CCSwitch\cc-switch.exe",
                Version: "3.20.4",
                ConfigRoot: @"E:\Codex\.cc-switch",
                SettingsFilePath: @"E:\Codex\.cc-switch\settings.json",
                CodexConfigDirectory: @"E:\Codex\.codex",
                CompatibilityJunctions:
                    [@"C:\Users\tester\AppData\Local\com.ccswitch.desktop"],
                Message: "已发现 CC Switch。"));
        }

        public Task<CcSwitchBindingPreview>
            PreviewCcSwitchCodexConfigDirAsync(
                string configRoot,
                string targetCodexConfigDirectory,
                CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new CcSwitchBindingPreview(
                @"E:\Codex\.cc-switch\settings.json",
                "codexConfigDir",
                @"E:\Codex\.codex",
                targetCodexConfigDirectory,
                "只改写 settings.json 的 codexConfigDir 字段。",
                ["未知字段", "注释与格式", "provider 密钥与配置内容"],
                [@"C:\Users\tester\AppData\Local\com.ccswitch.desktop"],
                "operation-1",
                "recovery-1",
                IsAlreadyBound: false));
        }

        public Task<CcSwitchBindingResult> ApplyCcSwitchCodexConfigDirAsync(
            CcSwitchBindingPreview preview,
            CancellationToken cancellationToken = default)
        {
            ApplyCalls++;
            return Task.FromResult(new CcSwitchBindingResult(
                new OperationRecord(
                    "operation-1",
                    OperationType.AgentBinding,
                    OperationState.Succeeded,
                    DateTimeOffset.UnixEpoch,
                    DateTimeOffset.UnixEpoch,
                    "绑定 CC Switch 的 Codex 配置环境"),
                new AgentConfigurationRecoveryPoint(
                    "recovery-1",
                    "CC Switch",
                    [],
                    DateTimeOffset.UnixEpoch),
                new CcSwitchDiscovery(
                    IsInstalled: true,
                    Executable: @"D:\Software\CCSwitch\cc-switch.exe",
                    Version: "3.20.4",
                    ConfigRoot: @"E:\Codex\.cc-switch",
                    SettingsFilePath: @"E:\Codex\.cc-switch\settings.json",
                    CodexConfigDirectory: preview.TargetValue,
                    CompatibilityJunctions: [],
                    Message: "已发现 CC Switch。")));
        }
    }
}
