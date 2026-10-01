using AgentEnvManager.Core.Agents;
using AgentEnvManager.Core.Inspection;
using AgentEnvManager.Core.Runtimes;
using AgentEnvManager.Wpf;

namespace AgentEnvManager.Wpf.Tests;

public sealed class AgentBindingViewModelTests
{
    [Fact]
    public async Task DiscoverAndPreview_displays_agent_and_recovery_point_information()
    {
        var client = new StubAgentClient();
        var viewModel = new AgentBindingViewModel(client, client, client)
        {
            ConfigurationDirectory = @"C:\Users\tester\.codex",
            Executable = @"C:\Apps\Codex\codex.exe",
            ManagedEntryPath = @"D:\shims\node",
            RuntimeName = "Node.js",
            RuntimeVersion = "24.1.0",
            WorkspacePath = @"D:\workspace"
        };

        await viewModel.DiscoverAgentAsync();
        await viewModel.PreviewBindingAsync();

        Assert.Equal("Codex", viewModel.SelectedAgent);
        Assert.Contains("已发现 Codex", viewModel.DiscoveryResult);
        Assert.Equal(
            @"C:\Users\tester\.codex\agent-env-manager.binding.json",
            viewModel.BindingFilePath);
        Assert.Contains(
            @"C:\Users\tester\.codex\agent-env-manager.binding.json",
            viewModel.RecoveryPointInformation);
        Assert.Contains("Node.js 24.1.0", viewModel.BindingSummary);
    }

    [Fact]
    public async Task DiscoverAgentAsync_backfills_configuration_and_executable()
    {
        var client = new StubAgentClient
        {
            DiscoveryHome = @"C:\Users\tester\.codex",
            DiscoveryExecutable = @"C:\Apps\Codex\codex.exe"
        };
        var viewModel = new AgentBindingViewModel(client, client, client);

        await viewModel.DiscoverAgentAsync();

        Assert.Equal(
            @"C:\Users\tester\.codex",
            viewModel.ConfigurationDirectory);
        Assert.Equal(
            @"C:\Apps\Codex\codex.exe",
            viewModel.Executable);
    }

    [Fact]
    public void Selecting_runtime_option_updates_runtime_fields()
    {
        var client = new StubAgentClient();
        var viewModel = new AgentBindingViewModel(client, client, client);

        viewModel.SelectedRuntimeOption =
            Assert.Single(viewModel.AvailableRuntimeOptions);

        Assert.Equal("Node.js", viewModel.RuntimeName);
        Assert.Equal("24.1.0", viewModel.RuntimeVersion);
    }

    [Fact]
    public async Task FolderPickers_fill_configuration_and_workspace()
    {
        var client = new StubAgentClient();
        var picker = new StubFileSystemPicker
        {
            Folder = @"D:\selected-folder"
        };
        var viewModel = new AgentBindingViewModel(
            client,
            client,
            client,
            picker);

        await viewModel.SelectConfigurationDirectoryAsync();
        await viewModel.SelectWorkspaceAsync();

        Assert.Equal(@"D:\selected-folder", viewModel.ConfigurationDirectory);
        Assert.Equal(@"D:\selected-folder", viewModel.WorkspacePath);
    }

    [Fact]
    public async Task BindAndHealthCheck_displays_recovery_and_health_results()
    {
        var client = new StubAgentClient();
        var viewModel = CreateConfiguredViewModel(client);
        await viewModel.PreviewBindingAsync();

        await viewModel.BindAsync();
        await viewModel.HealthCheckAsync();

        Assert.Equal("recovery-agent", viewModel.RecoveryResult);
        Assert.Contains("健康", viewModel.HealthResult);
        Assert.Equal(1, client.BindCalls);
        Assert.Equal(1, client.HealthCalls);
    }

    [Fact]
    public async Task BindAsync_displays_recovery_when_health_check_rolls_back()
    {
        var client = new StubAgentClient
        {
            BindFailure = new InvalidOperationException(
                "Agent 绑定失败，已恢复原绑定: 工具调用失败")
        };
        var viewModel = CreateConfiguredViewModel(client);
        await viewModel.PreviewBindingAsync();

        await viewModel.BindAsync();

        Assert.Contains("已恢复原绑定", viewModel.RecoveryResult);
        Assert.Contains("失败", viewModel.StatusMessage);
    }

    [Fact]
    public async Task Changing_binding_inputs_disables_stale_health_check()
    {
        var client = new StubAgentClient();
        var viewModel = CreateConfiguredViewModel(client);
        await viewModel.PreviewBindingAsync();
        await viewModel.BindAsync();
        Assert.True(viewModel.HealthCheckCommand.CanExecute(null));

        viewModel.SelectedAgent = "ChatGPT";

        Assert.False(viewModel.HealthCheckCommand.CanExecute(null));
    }

    private static AgentBindingViewModel CreateConfiguredViewModel(
        StubAgentClient client)
    {
        return new AgentBindingViewModel(client, client, client)
        {
            ConfigurationDirectory = @"C:\Users\tester\.codex",
            Executable = @"C:\Apps\Codex\codex.exe",
            ManagedEntryPath = @"D:\shims\node",
            RuntimeName = "Node.js",
            RuntimeVersion = "24.1.0"
        };
    }

    private sealed class StubAgentClient
        : IAgentDiscoveryClient,
          IAgentBindingClient,
          IRuntimeCatalogClient
    {
        public int BindCalls { get; private set; }

        public int HealthCalls { get; private set; }

        public Exception? BindFailure { get; init; }

        public string? DiscoveryHome { get; init; }

        public string? DiscoveryExecutable { get; init; }

        public IReadOnlyList<string> DescribeAgentAdapters()
        {
            return ["Codex", "ChatGPT"];
        }

        public Task<AgentDiscoveryResult> DiscoverAgentAsync(
            string agentName,
            AgentDiscoveryRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new AgentDiscoveryResult(
                IsInstalled: true,
                DiscoveryHome ?? request.ConfigurationDirectory,
                DiscoveryExecutable ?? request.Executable,
                "已发现 Codex。"));
        }

        public Task<AgentBindingPlan> CreateAgentBindingPlanAsync(
            string agentName,
            AgentBindingRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new AgentBindingPlan(
                agentName,
                request.ConfigurationDirectory,
                request.Executable,
                request.ManagedEntryPath,
                request.RuntimeName,
                request.RuntimeVersion,
                request.WorkspacePath ?? request.ConfigurationDirectory,
                request.RuntimeCommand ?? request.RuntimeName,
                ["exec", "--json"],
                Path.Combine(
                    request.ConfigurationDirectory,
                    "agent-env-manager.binding.json"),
                """{"runtime":"Node.js","version":"24.1.0"}"""));
        }

        public Task<AgentBinding> BindAgentAsync(
            string agentName,
            AgentBindingPlan plan,
            CancellationToken cancellationToken = default)
        {
            BindCalls++;
            if (BindFailure is not null)
            {
                throw BindFailure;
            }

            var recoveryPoint = new AgentConfigurationRecoveryPoint(
                "recovery-agent",
                agentName,
                [
                    new AgentConfigurationBackupEntry(
                        plan.BindingFilePath,
                        @"C:\backups\binding.json",
                        Existed: false)
                ],
                DateTimeOffset.UnixEpoch);
            return Task.FromResult(new AgentBinding(
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
                IsHealthy: true));
        }

        public Task<AgentHealthCheckResult> CheckAgentHealthAsync(
            string agentName,
            AgentBinding binding,
            CancellationToken cancellationToken = default)
        {
            HealthCalls++;
            return Task.FromResult(new AgentHealthCheckResult(
                IsHealthy: true,
                "Agent 工具调用健康。"));
        }

        public IReadOnlyList<RuntimeProviderDescriptor>
            DescribeRuntimeProviders()
        {
            return [new NodeRuntimeProvider().Descriptor];
        }
    }

    private sealed class StubFileSystemPicker : IFileSystemPicker
    {
        public string? Folder { get; init; }

        public string? File { get; init; }

        public string? SelectFolder(
            string title,
            string? initialDirectory = null)
        {
            return Folder;
        }

        public string? SelectFile(
            string title,
            string filter,
            string? initialDirectory = null)
        {
            return File;
        }
    }
}
