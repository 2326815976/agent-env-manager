using AgentEnvManager.Core.Agents;
using AgentEnvManager.Core.Storage;

namespace AgentEnvManager.Core.Tests.Agents;

public sealed class ChatGptAgentAdapterTests
{
    [Fact]
    public async Task DiscoverAsync_reports_not_installed_for_root_executable()
    {
        var root = CreateTempRoot();
        try
        {
            var adapter = new ChatGptAgentAdapter(
                new RecordingProcessRunner(
                    new Dictionary<string, AgentProcessResult>()),
                new FileAgentConfigurationBackupStore(
                    Path.Combine(root, "backups")),
                getFolderPath: _ => Path.Combine(root, "folders"),
                isReparsePoint: _ => false);

            var discovery = await adapter.DiscoverAsync(
                new AgentDiscoveryRequest(
                    Path.Combine(root, "chatgpt-home"),
                    @"C:\"));

            Assert.False(discovery.IsInstalled);
            Assert.Null(discovery.BundledCodexPath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task DiscoverAsync_reports_launch_chain_junction_and_binding_state()
    {
        var root = CreateTempRoot();
        try
        {
            var configurationDirectory = Path.Combine(root, "chatgpt-home");
            var applicationDirectory = Path.Combine(root, "app");
            var executable = Path.Combine(
                applicationDirectory,
                "ChatGPT.exe");
            var bundledCodex = Path.Combine(
                applicationDirectory,
                "resources",
                "codex.exe");
            var roamingAppData = Path.Combine(root, "roaming");
            var localAppData = Path.Combine(root, "local");
            var compatibilityJunction = Path.Combine(
                roamingAppData,
                "Codex");
            Directory.CreateDirectory(configurationDirectory);
            Directory.CreateDirectory(
                Path.GetDirectoryName(bundledCodex)!);
            File.WriteAllText(executable, string.Empty);
            File.WriteAllText(bundledCodex, string.Empty);
            var bindingFilePath = Path.Combine(
                configurationDirectory,
                "agent-env-manager.launch.ps1");
            File.WriteAllText(bindingFilePath, string.Empty);
            var adapter = new ChatGptAgentAdapter(
                new RecordingProcessRunner(
                    new Dictionary<string, AgentProcessResult>()),
                new FileAgentConfigurationBackupStore(
                    Path.Combine(root, "backups")),
                getFolderPath: folder => folder switch
                {
                    Environment.SpecialFolder.ApplicationData =>
                        roamingAppData,
                    Environment.SpecialFolder.LocalApplicationData =>
                        localAppData,
                    _ => root
                },
                isReparsePoint: path => string.Equals(
                    path,
                    compatibilityJunction,
                    StringComparison.OrdinalIgnoreCase),
                resolveLinkTarget: _ => configurationDirectory);

            var discovery = await adapter.DiscoverAsync(
                new AgentDiscoveryRequest(
                    configurationDirectory,
                    executable));

            Assert.True(discovery.IsInstalled);
            Assert.Equal(
                [compatibilityJunction],
                discovery.CompatibilityJunctionPaths);
            Assert.Equal(bundledCodex, discovery.BundledCodexPath);
            Assert.True(discovery.IsBound);
            Assert.Equal(bindingFilePath, discovery.BindingFilePath);

            // Junction 指向别处时不应被认定为兼容链接。
            var mismatched = new ChatGptAgentAdapter(
                new RecordingProcessRunner(
                    new Dictionary<string, AgentProcessResult>()),
                new FileAgentConfigurationBackupStore(
                    Path.Combine(root, "backups")),
                getFolderPath: folder => folder switch
                {
                    Environment.SpecialFolder.ApplicationData =>
                        roamingAppData,
                    Environment.SpecialFolder.LocalApplicationData =>
                        localAppData,
                    _ => root
                },
                isReparsePoint: path => string.Equals(
                    path,
                    compatibilityJunction,
                    StringComparison.OrdinalIgnoreCase),
                resolveLinkTarget: _ => Path.Combine(root, "other-home"));
            var mismatchedDiscovery = await mismatched.DiscoverAsync(
                new AgentDiscoveryRequest(
                    configurationDirectory,
                    executable));

            Assert.Empty(mismatchedDiscovery.CompatibilityJunctionPaths!);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task DiscoverAsync_reports_codex_home_source()
    {
        var root = CreateTempRoot();
        try
        {
            var configurationDirectory = Path.Combine(root, "codex-home");
            var applicationDirectory = Path.Combine(root, "app");
            var executable = Path.Combine(
                applicationDirectory,
                "ChatGPT.exe");
            var bundledCodex = Path.Combine(
                applicationDirectory,
                "resources",
                "codex.exe");
            Directory.CreateDirectory(configurationDirectory);
            Directory.CreateDirectory(
                Path.GetDirectoryName(bundledCodex)!);
            File.WriteAllText(executable, string.Empty);
            File.WriteAllText(bundledCodex, string.Empty);
            var adapter = new ChatGptAgentAdapter(
                new RecordingProcessRunner(
                    new Dictionary<string, AgentProcessResult>()),
                new FileAgentConfigurationBackupStore(
                    Path.Combine(root, "backups")),
                getFolderPath: _ => Path.Combine(root, "folders"),
                isReparsePoint: _ => false,
                readEnvironmentVariable: name =>
                    string.Equals(
                        name,
                        "CODEX_HOME",
                        StringComparison.OrdinalIgnoreCase)
                        ? configurationDirectory
                        : null);

            var discovery = await adapter.DiscoverAsync(
                new AgentDiscoveryRequest(Executable: executable));

            Assert.Equal(configurationDirectory, discovery.Home);
            Assert.Equal("CODEX_HOME", discovery.HomeSource);
            Assert.False(discovery.IsBound);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task BindAsync_launches_chatgpt_and_runs_tool_health_check()
    {
        var root = CreateTempRoot();
        try
        {
            var configurationDirectory = Path.Combine(root, "chatgpt-home");
            var workspacePath = Path.Combine(root, "workspace");
            var managedEntry = Path.Combine(root, "shims", "node");
            var applicationDirectory = Path.Combine(root, "app");
            var executable = Path.Combine(
                applicationDirectory,
                "ChatGPT.exe");
            var bundledCodex = Path.Combine(
                applicationDirectory,
                "resources",
                "codex.exe");
            Directory.CreateDirectory(configurationDirectory);
            Directory.CreateDirectory(workspacePath);
            Directory.CreateDirectory(managedEntry);
            Directory.CreateDirectory(Path.GetDirectoryName(bundledCodex)!);
            File.WriteAllText(executable, string.Empty);
            File.WriteAllText(bundledCodex, string.Empty);

            var processRunner = new RecordingProcessRunner(
                new Dictionary<string, AgentProcessResult>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    ["powershell.exe"] = new(0, string.Empty, string.Empty),
                    [bundledCodex] = new(
                        0,
                        "{\"type\":\"item.completed\",\"item\":{\"type\":\"command_execution\",\"exit_code\":0,\"output\":\"24.1.0\"}}",
                        string.Empty)
                });
            var adapter = new ChatGptAgentAdapter(
                processRunner,
                new FileAgentConfigurationBackupStore(
                    Path.Combine(root, "backups")));
            var plan = await adapter.CreatePlanAsync(
                new AgentBindingRequest(
                    configurationDirectory,
                    executable,
                    managedEntry,
                    "Node.js",
                    "24.1.0",
                    WorkspacePath: workspacePath));

            var binding = await new AgentBindingManager(adapter).BindAsync(plan);

            Assert.True(binding.IsHealthy);
            Assert.True(File.Exists(plan.BindingFilePath));
            Assert.Contains(
                "--open-project",
                await File.ReadAllTextAsync(plan.BindingFilePath));
            Assert.Contains(
                workspacePath,
                await File.ReadAllTextAsync(plan.BindingFilePath));
            Assert.Contains(
                "$args",
                await File.ReadAllTextAsync(plan.BindingFilePath));
            Assert.Contains(
                "app-server",
                await File.ReadAllTextAsync(plan.BindingFilePath));

            var launch = Assert.Single(
                processRunner.Invocations,
                invocation => string.Equals(
                    invocation.Executable,
                    "powershell.exe",
                    StringComparison.OrdinalIgnoreCase));
            Assert.Contains(
                "agent-env-manager.launch.ps1",
                launch.Arguments[^1]);

            var health = Assert.Single(
                processRunner.Invocations,
                invocation => string.Equals(
                    invocation.Executable,
                    bundledCodex,
                    StringComparison.OrdinalIgnoreCase));
            Assert.Equal(workspacePath, health.WorkingDirectory);
            Assert.Contains(managedEntry, health.Environment["PATH"]);
            Assert.Equal(
                configurationDirectory,
                health.Environment["CODEX_HOME"]);
            Assert.Contains("--cd", health.Arguments);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task BindAsync_restores_original_binding_when_health_fails()
    {
        var root = CreateTempRoot();
        try
        {
            var configurationDirectory = Path.Combine(root, "chatgpt-home");
            var workspacePath = Path.Combine(root, "workspace");
            var managedEntry = Path.Combine(root, "shims", "node");
            var applicationDirectory = Path.Combine(root, "app");
            var executable = Path.Combine(
                applicationDirectory,
                "ChatGPT.exe");
            var bundledCodex = Path.Combine(
                applicationDirectory,
                "resources",
                "codex.exe");
            Directory.CreateDirectory(configurationDirectory);
            Directory.CreateDirectory(workspacePath);
            Directory.CreateDirectory(managedEntry);
            Directory.CreateDirectory(Path.GetDirectoryName(bundledCodex)!);
            File.WriteAllText(executable, string.Empty);
            File.WriteAllText(bundledCodex, string.Empty);

            var bindingPath = Path.Combine(
                configurationDirectory,
                "agent-env-manager.launch.ps1");
            File.WriteAllText(bindingPath, "original-binding");
            var processRunner = new RecordingProcessRunner(
                new Dictionary<string, AgentProcessResult>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    ["powershell.exe"] = new(0, string.Empty, string.Empty),
                    [bundledCodex] = new(1, string.Empty, "tool failed")
                });
            var adapter = new ChatGptAgentAdapter(
                processRunner,
                new FileAgentConfigurationBackupStore(
                    Path.Combine(root, "backups")));
            var plan = await adapter.CreatePlanAsync(
                new AgentBindingRequest(
                    configurationDirectory,
                    executable,
                    managedEntry,
                    "Node.js",
                    "24.1.0",
                    WorkspacePath: workspacePath));

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => new AgentBindingManager(adapter).BindAsync(plan));

            Assert.Equal(
                "original-binding",
                await File.ReadAllTextAsync(bindingPath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task DiscoverAsync_requires_an_explicit_chatgpt_executable()
    {
        var root = CreateTempRoot();
        try
        {
            var configurationDirectory = Path.Combine(root, "chatgpt-home");
            var applicationDirectory = Path.Combine(root, "app");
            var bundledCodex = Path.Combine(
                applicationDirectory,
                "resources",
                "codex.exe");
            var executable = Path.Combine(
                applicationDirectory,
                "ChatGPT.exe");
            var incompleteApplicationDirectory = Path.Combine(
                root,
                "incomplete-app");
            var incompleteExecutable = Path.Combine(
                incompleteApplicationDirectory,
                "ChatGPT.exe");
            Directory.CreateDirectory(configurationDirectory);
            Directory.CreateDirectory(applicationDirectory);
            Directory.CreateDirectory(Path.GetDirectoryName(bundledCodex)!);
            Directory.CreateDirectory(incompleteApplicationDirectory);
            File.WriteAllText(executable, string.Empty);
            File.WriteAllText(bundledCodex, string.Empty);
            File.WriteAllText(incompleteExecutable, string.Empty);
            var adapter = new ChatGptAgentAdapter(
                new RecordingProcessRunner(
                    new Dictionary<string, AgentProcessResult>()),
                new FileAgentConfigurationBackupStore(
                    Path.Combine(root, "backups")),
                new StubExecutableLocator(null));

            var missingExecutable = await adapter.DiscoverAsync(
                new AgentDiscoveryRequest(configurationDirectory));
            var incomplete = await adapter.DiscoverAsync(
                new AgentDiscoveryRequest(
                    configurationDirectory,
                    incompleteExecutable));
            var discovered = await adapter.DiscoverAsync(
                new AgentDiscoveryRequest(
                    configurationDirectory,
                    executable));

            Assert.False(missingExecutable.IsInstalled);
            Assert.False(incomplete.IsInstalled);
            Assert.True(discovered.IsInstalled);
            Assert.Equal(executable, discovered.Executable);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task DiscoverAsync_locates_chatgpt_when_not_supplied()
    {
        var root = CreateTempRoot();
        try
        {
            var configurationDirectory = Path.Combine(root, "codex-home");
            var applicationDirectory = Path.Combine(root, "app");
            var bundledCodex = Path.Combine(
                applicationDirectory,
                "resources",
                "codex.exe");
            var executable = Path.Combine(
                applicationDirectory,
                "ChatGPT.exe");
            Directory.CreateDirectory(configurationDirectory);
            Directory.CreateDirectory(Path.GetDirectoryName(bundledCodex)!);
            File.WriteAllText(executable, string.Empty);
            File.WriteAllText(bundledCodex, string.Empty);
            var adapter = new ChatGptAgentAdapter(
                new RecordingProcessRunner(
                    new Dictionary<string, AgentProcessResult>()),
                new FileAgentConfigurationBackupStore(
                    Path.Combine(root, "backups")),
                new StubExecutableLocator(executable));

            var discovery = await adapter.DiscoverAsync(
                new AgentDiscoveryRequest(configurationDirectory));

            Assert.True(discovery.IsInstalled);
            Assert.Equal(executable, discovery.Executable);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Environment_manager_registers_chatgpt_adapter()
    {
        var root = CreateTempRoot();
        try
        {
            var configurationDirectory = Path.Combine(root, "chatgpt-home");
            var workspacePath = Path.Combine(root, "workspace");
            var managedEntry = Path.Combine(root, "shims", "node");
            var applicationDirectory = Path.Combine(root, "app");
            var executable = Path.Combine(
                applicationDirectory,
                "ChatGPT.exe");
            var bundledCodex = Path.Combine(
                applicationDirectory,
                "resources",
                "codex.exe");
            Directory.CreateDirectory(configurationDirectory);
            Directory.CreateDirectory(workspacePath);
            Directory.CreateDirectory(managedEntry);
            Directory.CreateDirectory(Path.GetDirectoryName(bundledCodex)!);
            File.WriteAllText(executable, string.Empty);
            File.WriteAllText(bundledCodex, string.Empty);
            var manager = EnvironmentManagerFactory.Create(
                Path.Combine(root, "manager-state"));

            var plan = await manager.CreateAgentBindingPlanAsync(
                "ChatGPT",
                new AgentBindingRequest(
                    configurationDirectory,
                    executable,
                    managedEntry,
                    "Node.js",
                    "24.1.0",
                    WorkspacePath: workspacePath));

            Assert.Equal("ChatGPT", plan.AgentName);
            Assert.Equal(
                Path.Combine(
                    configurationDirectory,
                    "agent-env-manager.launch.ps1"),
                plan.BindingFilePath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "AgentEnvManager.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private sealed record ProcessInvocation(
        string Executable,
        IReadOnlyList<string> Arguments,
        string WorkingDirectory,
        IReadOnlyDictionary<string, string> Environment);

    private sealed class RecordingProcessRunner(
        IReadOnlyDictionary<string, AgentProcessResult> results)
        : IAgentProcessRunner
    {
        public IReadOnlyList<ProcessInvocation> Invocations { get; } =
            new List<ProcessInvocation>();

        public Task<AgentProcessResult> RunAsync(
            string executable,
            IReadOnlyList<string> arguments,
            string workingDirectory,
            IReadOnlyDictionary<string, string> environment,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ((List<ProcessInvocation>)Invocations).Add(
                new ProcessInvocation(
                    executable,
                    arguments.ToArray(),
                    workingDirectory,
                    new Dictionary<string, string>(
                        environment,
                        StringComparer.OrdinalIgnoreCase)));
            return Task.FromResult(
                results.TryGetValue(executable, out var result)
                    ? result
                    : throw new InvalidOperationException(
                        $"未配置进程结果: {executable}"));
        }
    }

    private sealed class StubExecutableLocator(string? executable)
        : IExecutableLocator
    {
        public string? FindExecutable(string command)
        {
            return command == "ChatGPT" ? executable : null;
        }
    }
}
