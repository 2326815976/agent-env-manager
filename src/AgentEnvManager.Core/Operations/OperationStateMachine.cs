namespace AgentEnvManager.Core.Operations;

internal static class OperationStateMachine
{
    public static OperationRecord Create(
        OperationType type,
        string summary,
        TimeProvider timeProvider,
        string? target = null,
        string? impact = null)
    {
        var now = timeProvider.GetUtcNow();
        return new OperationRecord(
            Guid.NewGuid().ToString("N"),
            type,
            OperationState.Draft,
            now,
            now,
            summary,
            Target: target,
            Impact: impact);
    }

    public static OperationRecord MarkValidated(
        OperationRecord operation,
        TimeProvider timeProvider)
    {
        return Transition(
            operation,
            OperationState.Validated,
            timeProvider);
    }

    public static OperationRecord MarkRecoveryReady(
        OperationRecord operation,
        string recoveryPointId,
        TimeProvider timeProvider)
    {
        return Transition(
            operation,
            OperationState.RecoveryReady,
            timeProvider) with
        {
            RecoveryPointId = recoveryPointId
        };
    }

    public static OperationRecord BeginExecution(
        OperationRecord operation,
        TimeProvider timeProvider)
    {
        return Transition(
            operation,
            OperationState.Executing,
            timeProvider);
    }

    public static OperationRecord BeginVerification(
        OperationRecord operation,
        TimeProvider timeProvider)
    {
        return Transition(
            operation,
            OperationState.Verifying,
            timeProvider);
    }

    public static OperationRecord Complete(
        OperationRecord operation,
        TimeProvider timeProvider)
    {
        return Transition(
            operation,
            OperationState.Succeeded,
            timeProvider);
    }

    public static OperationRecord Fail(
        OperationRecord operation,
        string reason,
        TimeProvider timeProvider)
    {
        return Transition(
            operation,
            OperationState.Failed,
            timeProvider) with
        {
            FailureReason = reason
        };
    }

    public static OperationRecord Rollback(
        OperationRecord operation,
        TimeProvider timeProvider)
    {
        return Transition(
            operation,
            OperationState.RolledBack,
            timeProvider);
    }

    public static OperationRecord Reject(
        OperationRecord operation,
        OperationState attemptedState,
        string reason,
        TimeProvider timeProvider)
    {
        return operation with
        {
            UpdatedAtUtc = timeProvider.GetUtcNow(),
            FailureReason = $"已拒绝 {operation.State} -> {attemptedState}: {reason}"
        };
    }

    private static OperationRecord Transition(
        OperationRecord operation,
        OperationState next,
        TimeProvider timeProvider)
    {
        var legal = operation.State switch
        {
            OperationState.Draft => next is OperationState.Validated
                or OperationState.Failed,
            OperationState.Validated => next is OperationState.RecoveryReady
                or OperationState.Failed,
            OperationState.RecoveryReady => next is OperationState.Executing
                or OperationState.Failed,
            OperationState.Executing => next is OperationState.Verifying
                or OperationState.Failed,
            OperationState.Verifying => next is OperationState.Succeeded
                or OperationState.Failed,
            OperationState.Failed => next == OperationState.RolledBack,
            _ => false
        };

        if (!legal)
        {
            throw InvalidTransition(operation.State, next);
        }

        return operation with
        {
            State = next,
            UpdatedAtUtc = timeProvider.GetUtcNow()
        };
    }

    private static InvalidOperationException InvalidTransition(
        OperationState current,
        OperationState next)
    {
        return new InvalidOperationException(
            $"非法操作状态转换: {current} -> {next}");
    }
}
