namespace AgentEnvManager.Core.Agents;

public sealed record AgentProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError);

public interface IAgentProcessRunner
{
    Task<AgentProcessResult> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string> environment,
        CancellationToken cancellationToken = default);
}

public sealed record AgentConfigurationBackupEntry(
    string SourcePath,
    string BackupPath,
    bool Existed);

public sealed record AgentConfigurationRecoveryPoint(
    string Id,
    string AgentName,
    IReadOnlyList<AgentConfigurationBackupEntry> Entries,
    DateTimeOffset CreatedAtUtc);

public interface IAgentConfigurationBackupStore
{
    Task<AgentConfigurationRecoveryPoint> CreateAsync(
        string agentName,
        IReadOnlyList<string> sourcePaths,
        CancellationToken cancellationToken = default);

    Task RestoreAsync(
        AgentConfigurationRecoveryPoint recoveryPoint,
        CancellationToken cancellationToken = default);
}

public sealed record AgentBindingRequest(
    string ConfigurationDirectory,
    string Executable,
    string ManagedEntryPath,
    string RuntimeName,
    string RuntimeVersion,
    IReadOnlyList<string>? HealthArguments = null);

public sealed record AgentDiscoveryRequest(
    string? ConfigurationDirectory = null,
    string? Executable = null);

public sealed record AgentDiscoveryResult(
    bool IsInstalled,
    string? Home,
    string? Executable,
    string Message);

public sealed record AgentBindingPlan(
    string AgentName,
    string ConfigurationDirectory,
    string Executable,
    string ManagedEntryPath,
    string RuntimeName,
    string RuntimeVersion,
    IReadOnlyList<string> HealthArguments,
    string BindingFilePath,
    string BindingContent);

public sealed record AgentBinding(
    string AgentName,
    string ConfigurationDirectory,
    string Executable,
    string ManagedEntryPath,
    string RuntimeName,
    string RuntimeVersion,
    IReadOnlyList<string> HealthArguments,
    string BindingFilePath,
    AgentConfigurationRecoveryPoint RecoveryPoint,
    bool IsHealthy = false);

public sealed record AgentHealthCheckResult(
    bool IsHealthy,
    string Message);

public interface IAgentAdapter
{
    string Name { get; }

    Task<AgentDiscoveryResult> DiscoverAsync(
        AgentDiscoveryRequest request,
        CancellationToken cancellationToken = default);

    Task<AgentBindingPlan> CreatePlanAsync(
        AgentBindingRequest request,
        CancellationToken cancellationToken = default);

    Task<AgentConfigurationRecoveryPoint> CreateRecoveryPointAsync(
        AgentBindingPlan plan,
        CancellationToken cancellationToken = default);

    Task<AgentBinding> ActivateAsync(
        AgentBindingPlan plan,
        AgentConfigurationRecoveryPoint recoveryPoint,
        CancellationToken cancellationToken = default);

    Task<AgentProcessResult> LaunchAsync(
        AgentBinding binding,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default);

    Task<AgentHealthCheckResult> CheckHealthAsync(
        AgentBinding binding,
        CancellationToken cancellationToken = default);

    Task RollbackAsync(
        AgentBinding binding,
        CancellationToken cancellationToken = default);
}
