using AgentEnvManager.Core.Adoption;

namespace AgentEnvManager.Core.Migrations;

public sealed record MigrationBlocker(
    string Code,
    string Message,
    string? Path = null);

public enum MigrationStrategy
{
    AtomicRename,
    CopyAndVerify
}

public sealed record MigrationPathStatistics(
    long FileCount,
    long TotalBytes,
    long AvailableBytes);

public sealed record MigrationPreview(
    EnvironmentFingerprint TargetFingerprint,
    EnvironmentManifest Target,
    string SourcePath,
    string DestinationPath,
    string StableActivationPath,
    string ManagedEntryPath,
    string PreviousActivationTarget,
    bool WasActive,
    MigrationStrategy Strategy,
    MigrationPathStatistics Statistics,
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
    MigrationStrategy GetStrategy(
        string sourcePath,
        string destinationPath);

    Task<MigrationPathStatistics> InspectAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken = default);

    Task MoveAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken = default);

    Task CopyAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        string path,
        CancellationToken cancellationToken = default);

    Task MoveBackAsync(
        string currentPath,
        string originalPath,
        CancellationToken cancellationToken = default);
}
