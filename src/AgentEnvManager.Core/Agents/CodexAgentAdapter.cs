using System.Text.Json;
using System.Text.RegularExpressions;
using AgentEnvManager.Core.Storage;

namespace AgentEnvManager.Core.Agents;

public sealed class CodexAgentAdapter(
    IAgentProcessRunner processRunner,
    IAgentConfigurationBackupStore backupStore)
    : IAgentAdapter
{
    public string Name => "Codex";

    public Task<AgentDiscoveryResult> DiscoverAsync(
        AgentDiscoveryRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var home = request.ConfigurationDirectory;
        if (string.IsNullOrWhiteSpace(home))
        {
            home = Environment.GetEnvironmentVariable("CODEX_HOME");
        }

        if (string.IsNullOrWhiteSpace(home))
        {
            home = Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.UserProfile),
                ".codex");
        }

        home = Path.GetFullPath(home);
        var executable = request.Executable;
        var installed = Directory.Exists(home)
            && !string.IsNullOrWhiteSpace(executable)
            && File.Exists(executable);
        return Task.FromResult(new AgentDiscoveryResult(
            installed,
            home,
            executable,
            installed
                ? "已发现 Codex。"
                : "未完整发现 Codex 配置环境或可执行文件。"));
    }

    public Task<AgentBindingPlan> CreatePlanAsync(
        AgentBindingRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var bindingFilePath = Path.Combine(
            request.ConfigurationDirectory,
            "agent-env-manager.binding.json");
        var content = JsonSerializer.Serialize(
            new
            {
                runtime = request.RuntimeName,
                version = request.RuntimeVersion,
                managedEntryPath = request.ManagedEntryPath
            },
            ManagerJson.Options);
        var healthArguments = request.HealthArguments ??
        [
            "exec",
            "--json",
            "--skip-git-repo-check",
            "--color",
            "never",
            $"Run {request.RuntimeName} --version"
        ];
        return Task.FromResult(new AgentBindingPlan(
            Name,
            request.ConfigurationDirectory,
            request.Executable,
            request.ManagedEntryPath,
            request.RuntimeName,
            request.RuntimeVersion,
            healthArguments,
            bindingFilePath,
            content));
    }

    public Task<AgentConfigurationRecoveryPoint> CreateRecoveryPointAsync(
        AgentBindingPlan plan,
        CancellationToken cancellationToken = default)
    {
        return backupStore.CreateAsync(
            Name,
            [plan.BindingFilePath],
            cancellationToken);
    }

    public async Task<AgentBinding> ActivateAsync(
        AgentBindingPlan plan,
        AgentConfigurationRecoveryPoint recoveryPoint,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(plan.ConfigurationDirectory);
        await File.WriteAllTextAsync(
            plan.BindingFilePath,
            plan.BindingContent,
            cancellationToken);

        return new AgentBinding(
            plan.AgentName,
            plan.ConfigurationDirectory,
            plan.Executable,
            plan.ManagedEntryPath,
            plan.RuntimeName,
            plan.RuntimeVersion,
            plan.HealthArguments,
            plan.BindingFilePath,
            recoveryPoint);
    }

    public Task<AgentProcessResult> LaunchAsync(
        AgentBinding binding,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default)
    {
        var bindingContent = File.ReadAllText(binding.BindingFilePath);
        using var document = JsonDocument.Parse(bindingContent);
        var managedEntryPath = document.RootElement
            .GetProperty("managedEntryPath")
            .GetString()
            ?? throw new InvalidDataException("Codex 绑定文件缺少受管入口。");
        return processRunner.RunAsync(
            binding.Executable,
            arguments,
            binding.ConfigurationDirectory,
            CreateEnvironment(binding, managedEntryPath),
            cancellationToken);
    }

    public async Task<AgentHealthCheckResult> CheckHealthAsync(
        AgentBinding binding,
        CancellationToken cancellationToken = default)
    {
        var result = await LaunchAsync(
            binding,
            binding.HealthArguments,
            cancellationToken);
        var output = $"{result.StandardOutput}\n{result.StandardError}";
        var healthy = result.ExitCode == 0
            && Regex.IsMatch(
                output,
                $@"(?<![A-Za-z0-9_.-]){Regex.Escape(binding.RuntimeVersion)}(?![A-Za-z0-9_.-])",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return new AgentHealthCheckResult(
            healthy,
            healthy
                ? $"{binding.RuntimeName} 调用成功。"
                : $"exit={result.ExitCode}; stdout={result.StandardOutput.Trim()}; stderr={result.StandardError.Trim()}");
    }

    public Task RollbackAsync(
        AgentBinding binding,
        CancellationToken cancellationToken = default)
    {
        return backupStore.RestoreAsync(
            binding.RecoveryPoint,
            cancellationToken);
    }

    private static IReadOnlyDictionary<string, string> CreateEnvironment(
        AgentBinding binding,
        string managedEntryPath)
    {
        return new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["CODEX_HOME"] = binding.ConfigurationDirectory,
            ["PATH"] =
                $"{managedEntryPath};" +
                $"{Environment.GetEnvironmentVariable("PATH")}"
        };
    }
}
