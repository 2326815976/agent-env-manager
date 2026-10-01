using AgentEnvManager.Core.Agents;

namespace AgentEnvManager.Core.Tests.Agents;

public sealed class CodexAgentAdapterTests
{
    [Fact]
    public async Task BindAsync_writes_binding_and_returns_health_result()
    {
        var root = CreateTempRoot();
        try
        {
            var codexHome = Path.Combine(root, "codex-home");
            var managedEntry = Path.Combine(root, "shims", "node");
            Directory.CreateDirectory(codexHome);
            Directory.CreateDirectory(managedEntry);
            var processRunner = new RecordingProcessRunner(
                new AgentProcessResult(
                    0,
                    "{\"type\":\"command_execution\",\"command\":\"node --version\",\"exit_code\":0,\"output\":\"24.1.0\"}",
                    ""));
            var backupStore = new FileAgentConfigurationBackupStore(
                Path.Combine(root, "backups"));
            var adapter = new CodexAgentAdapter(
                processRunner,
                backupStore);
            var plan = await adapter.CreatePlanAsync(
                new AgentBindingRequest(
                    codexHome,
                    "codex-test.cmd",
                    managedEntry,
                    "Node.js",
                    "24.1.0"));

            var binding = await new AgentBindingManager(adapter).BindAsync(plan);

            Assert.True(binding.IsHealthy);
            Assert.True(File.Exists(plan.BindingFilePath));
            Assert.Contains(
                managedEntry,
                processRunner.LastEnvironment!["PATH"]);
            Assert.Equal(codexHome, processRunner.LastEnvironment["CODEX_HOME"]);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task DiscoverAsync_reports_codex_home_and_executable()
    {
        var root = CreateTempRoot();
        try
        {
            var codexHome = Path.Combine(root, "codex-home");
            Directory.CreateDirectory(codexHome);
            var codexExecutable = Path.Combine(codexHome, "codex.cmd");
            File.WriteAllText(codexExecutable, "@echo off\r\n");
            var adapter = new CodexAgentAdapter(
                new RecordingProcessRunner(
                    new AgentProcessResult(0, "", "")),
                new FileAgentConfigurationBackupStore(
                    Path.Combine(root, "backups")));

            var discovery = await adapter.DiscoverAsync(
                new AgentDiscoveryRequest(
                    codexHome,
                    codexExecutable));

            Assert.True(discovery.IsInstalled);
            Assert.Equal(codexHome, discovery.Home);
            Assert.Equal(codexExecutable, discovery.Executable);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task DiscoverAsync_locates_codex_on_path_when_not_supplied()
    {
        var root = CreateTempRoot();
        try
        {
            var codexHome = Path.Combine(root, "codex-home");
            var codexExecutable = Path.Combine(root, "codex.cmd");
            Directory.CreateDirectory(codexHome);
            File.WriteAllText(codexExecutable, "@echo off\r\n");
            var adapter = new CodexAgentAdapter(
                new RecordingProcessRunner(
                    new AgentProcessResult(0, "", "")),
                new FileAgentConfigurationBackupStore(
                    Path.Combine(root, "backups")),
                new StubExecutableLocator(codexExecutable));

            var discovery = await adapter.DiscoverAsync(
                new AgentDiscoveryRequest(codexHome));

            Assert.True(discovery.IsInstalled);
            Assert.Equal(codexExecutable, discovery.Executable);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task BindAsync_invokes_tool_through_managed_entry()
    {
        var root = CreateTempRoot();
        try
        {
            var codexHome = Path.Combine(root, "codex-home");
            var managedEntry = Path.Combine(root, "shims", "node");
            Directory.CreateDirectory(codexHome);
            Directory.CreateDirectory(managedEntry);
            File.WriteAllText(
                Path.Combine(managedEntry, "node.cmd"),
                "@echo off\r\necho 24.1.0\r\n");
            var healthScript = Path.Combine(codexHome, "health-check.ps1");
            File.WriteAllText(
                healthScript,
                "$v = (node --version).Trim()\r\n" +
                "Write-Output ('{\"type\":\"command_execution\",\"command\":\"node --version\",\"exit_code\":0,\"output\":\"' + $v + '\"}')\r\n");
            var adapter = new CodexAgentAdapter(
                new SystemAgentProcessRunner(),
                new FileAgentConfigurationBackupStore(
                    Path.Combine(root, "backups")));
            var plan = await adapter.CreatePlanAsync(
                new AgentBindingRequest(
                    codexHome,
                    "pwsh.exe",
                    managedEntry,
                    "Node.js",
                    "24.1.0",
                    HealthArguments:
                    [
                        "-NoProfile",
                        "-File",
                        healthScript
                    ]));

            var binding = await new AgentBindingManager(adapter).BindAsync(plan);

            Assert.True(binding.IsHealthy);
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
            var codexHome = Path.Combine(root, "codex-home");
            var managedEntry = Path.Combine(root, "shims", "node");
            Directory.CreateDirectory(codexHome);
            Directory.CreateDirectory(managedEntry);
            var bindingPath = Path.Combine(
                codexHome,
                "agent-env-manager.binding.json");
            File.WriteAllText(bindingPath, "original-binding");
            var processRunner = new RecordingProcessRunner(
                new AgentProcessResult(1, "", "node not found"));
            var adapter = new CodexAgentAdapter(
                processRunner,
                new FileAgentConfigurationBackupStore(
                    Path.Combine(root, "backups")));
            var plan = await adapter.CreatePlanAsync(
                new AgentBindingRequest(
                    codexHome,
                    "codex-test.cmd",
                    managedEntry,
                    "Node.js",
                    "24.1.0"));

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
    public async Task BindAsync_requires_explicit_tool_success()
    {
        var root = CreateTempRoot();
        try
        {
            var codexHome = Path.Combine(root, "codex-home");
            var managedEntry = Path.Combine(root, "shims", "node");
            Directory.CreateDirectory(codexHome);
            Directory.CreateDirectory(managedEntry);
            var processRunner = new RecordingProcessRunner(
                new AgentProcessResult(
                    0,
                    "{\"type\":\"command_execution\",\"command\":\"node --version\",\"output\":\"24.1.0\"}",
                    string.Empty));
            var adapter = new CodexAgentAdapter(
                processRunner,
                new FileAgentConfigurationBackupStore(
                    Path.Combine(root, "backups")));
            var plan = await adapter.CreatePlanAsync(
                new AgentBindingRequest(
                    codexHome,
                    "codex-test.cmd",
                    managedEntry,
                    "Node.js",
                    "24.1.0"));

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => new AgentBindingManager(adapter).BindAsync(plan));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(
        "{\"type\":\"item.completed\",\"item\":{\"type\":\"tool_call\",\"status\":\"completed\",\"output\":\"24.1.0\"}}",
        true)]
    [InlineData(
        "{\"type\":\"item.completed\",\"item\":{\"type\":\"tool_call\",\"status\":\"success\",\"output\":\"24.1.0\"}}",
        true)]
    [InlineData(
        "{\"type\":\"item.completed\",\"item\":{\"type\":\"tool_call\",\"status\":\"failed\",\"output\":\"24.1.0\"}}",
        false)]
    [InlineData(
        "{\"type\":\"item.completed\",\"item\":{\"type\":\"tool_call\",\"exit_code\":0,\"output\":\"24.1.0\"}}",
        true)]
    [InlineData(
        "{\"type\":\"item.completed\",\"item\":{\"type\":\"tool_call\",\"exit_code\":1,\"output\":\"24.1.0\"}}",
        false)]
    public async Task CheckHealthAsync_requires_explicit_tool_success(
        string output,
        bool expected)
    {
        var root = CreateTempRoot();
        try
        {
            var codexHome = Path.Combine(root, "codex-home");
            var managedEntry = Path.Combine(root, "shims", "node");
            Directory.CreateDirectory(codexHome);
            Directory.CreateDirectory(managedEntry);
            var processRunner = new RecordingProcessRunner(
                new AgentProcessResult(0, output, string.Empty));
            var adapter = new CodexAgentAdapter(
                processRunner,
                new FileAgentConfigurationBackupStore(
                    Path.Combine(root, "backups")));
            var plan = await adapter.CreatePlanAsync(
                new AgentBindingRequest(
                    codexHome,
                    "codex-test.cmd",
                    managedEntry,
                    "Node.js",
                    "24.1.0"));
            await File.WriteAllTextAsync(
                plan.BindingFilePath,
                plan.BindingContent);
            var binding = new AgentBinding(
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
                new AgentConfigurationRecoveryPoint(
                    "recovery",
                    plan.AgentName,
                    [],
                    DateTimeOffset.UtcNow));

            var result = await adapter.CheckHealthAsync(binding);

            Assert.Equal(expected, result.IsHealthy);
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

    private sealed class RecordingProcessRunner(AgentProcessResult result)
        : IAgentProcessRunner
    {
        public IReadOnlyDictionary<string, string>? LastEnvironment
        {
            get;
            private set;
        }

        public Task<AgentProcessResult> RunAsync(
            string executable,
            IReadOnlyList<string> arguments,
            string workingDirectory,
            IReadOnlyDictionary<string, string> environment,
            CancellationToken cancellationToken = default)
        {
            LastEnvironment = environment;
            return Task.FromResult(result);
        }
    }

    private sealed class StubExecutableLocator(string executable)
        : IExecutableLocator
    {
        public string? FindExecutable(string command)
        {
            return command == "codex" ? executable : null;
        }
    }
}
