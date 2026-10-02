namespace AgentEnvManager.Core.Migrations;

public sealed record MigrationProcessRequirement(
    string Name,
    string DisplayName,
    IReadOnlyList<string> ProcessNames);

public sealed record CoordinatedMigrationTarget(
    string Kind,
    string DisplayName,
    string SourcePath,
    string DestinationPath);

public sealed record CoordinatedMigrationBlocker(
    string Code,
    string Message,
    string? Path = null);

public sealed record CoordinatedJunctionTarget(
    string LinkPath,
    string TargetPath);

public sealed record CoordinatedStartupTargets(
    string CcSwitchExecutablePath,
    string ChatGptExecutablePath);

public sealed record CoordinatedMigrationRequest(
    string CodexConfigSourcePath,
    string CodexConfigDestinationPath,
    string CcSwitchConfigSourcePath,
    string CcSwitchConfigDestinationPath,
    IReadOnlyList<CoordinatedJunctionTarget>? CompatibilityJunctions = null,
    CoordinatedStartupTargets? StartupTargets = null);

public sealed record CoordinatedMigrationPreview(
    IReadOnlyList<CoordinatedMigrationTarget> Targets,
    IReadOnlyList<MigrationProcessRequirement> ProcessesToStop,
    IReadOnlyList<string> PlannedRewrites,
    IReadOnlyList<string> StartupOrder,
    IReadOnlyList<CoordinatedJunctionTarget> JunctionsToRewire,
    CoordinatedStartupTargets? StartupTargets,
    IReadOnlyList<CoordinatedMigrationBlocker> Blockers,
    string Impact,
    string? OperationId = null,
    string? RecoveryPointId = null)
{
    public bool CanApply => Blockers.Count == 0;
}

public interface IProcessControlProbe
{
    Task<IReadOnlyList<string>> FindRunningProcessesAsync(
        IReadOnlyList<string> processNames,
        CancellationToken cancellationToken = default);

    Task StopProcessesAsync(
        IReadOnlyList<string> processNames,
        CancellationToken cancellationToken = default);
}

public interface ICoordinatedStartupProbe
{
    Task StartAsync(
        string executablePath,
        CancellationToken cancellationToken = default);
}

public interface ICoordinatedHealthProbe
{
    Task<Agents.AgentHealthCheckResult> CheckAsync(
        CancellationToken cancellationToken = default);
}

public sealed record CoordinatedMigrationResult(
    Operations.OperationRecord Operation,
    IReadOnlyList<string> MigratedPaths,
    IReadOnlyList<string> RewrittenPaths,
    IReadOnlyList<string> RewiredJunctions,
    bool SourcesRetained);
