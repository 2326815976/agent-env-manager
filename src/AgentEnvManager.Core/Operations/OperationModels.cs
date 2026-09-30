namespace AgentEnvManager.Core.Operations;

public enum OperationType
{
    Adopt,
    Switch
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
    string? FailureReason = null);

public interface IOperationJournal
{
    Task SaveAsync(
        OperationRecord operation,
        CancellationToken cancellationToken = default);

    Task<OperationRecord?> GetAsync(
        string operationId,
        CancellationToken cancellationToken = default);
}
