using AgentEnvManager.Core.Adoption;

namespace AgentEnvManager.Core.Migrations;

public sealed record MigrationBlocker(
    string Code,
    string Message,
    string? Path = null);

public sealed record MigrationPreview(
    EnvironmentFingerprint TargetFingerprint,
    EnvironmentManifest Target,
    string SourcePath,
    string DestinationPath,
    string StableActivationPath,
    string ManagedEntryPath,
    string PreviousActivationTarget,
    bool WasActive,
    string Impact,
    string ExpectedResult,
    string? OperationId = null,
    string? RecoveryPointId = null);

public interface IMigrationOccupancyProbe
{
    Task<IReadOnlyList<MigrationBlocker>> FindBlockersAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken = default);
}

public interface IEnvironmentPathMover
{
    void ValidateSameVolume(
        string sourcePath,
        string destinationPath);

    Task MoveAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken = default);

    Task MoveBackAsync(
        string currentPath,
        string originalPath,
        CancellationToken cancellationToken = default);
}
