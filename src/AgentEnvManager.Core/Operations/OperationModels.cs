namespace AgentEnvManager.Core.Operations;

public enum OperationType
{
    Adopt,
    Install,
    Switch,
    Migrate,
    Delete,
    Purge,
    EnvironmentVariables,
    AgentBinding,
    GitConfigurationBackup,
    ArtifactImport
}

public enum OperationState
{
    Draft,
    Validated,
    RecoveryReady,
    Executing,
    Verifying,
    Succeeded,
    Failed,
    RolledBack
}

public sealed record OperationRecord(
    string Id,
    OperationType Type,
    OperationState State,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    string Summary,
    string? RecoveryPointId = null,
    string? FailureReason = null,
    string? Target = null,
    string? Impact = null,
    string? PreviousTarget = null,
    string? ExpectedResult = null,
    string? TargetIdentity = null,
    string? SourceTarget = null,
    string? StableActivationPath = null,
    string? MigrationStrategy = null,
    string? ArtifactSource = null,
    string? ArtifactCachePath = null,
    string? ArtifactSha256 = null,
    string? MirrorUrl = null,
    string? VerificationResult = null);

public sealed record OperationRollbackPlan(
    string OperationId,
    string Target,
    string Impact,
    string RecoveryPointId,
    string ExpectedResult);

public interface IOperationJournal
{
    Task<IReadOnlyList<OperationRecord>> ReadAllAsync(
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        OperationRecord operation,
        CancellationToken cancellationToken = default);

    Task<OperationRecord?> GetAsync(
        string operationId,
        CancellationToken cancellationToken = default);
}
