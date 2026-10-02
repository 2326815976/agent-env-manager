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

public sealed record CoordinatedMigrationRequest(
    string CodexConfigSourcePath,
    string CodexConfigDestinationPath,
    string CcSwitchConfigSourcePath,
    string CcSwitchConfigDestinationPath);

public sealed record CoordinatedMigrationPreview(
    IReadOnlyList<CoordinatedMigrationTarget> Targets,
    IReadOnlyList<MigrationProcessRequirement> ProcessesToStop,
    IReadOnlyList<string> PlannedRewrites,
    IReadOnlyList<string> StartupOrder,
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
}
