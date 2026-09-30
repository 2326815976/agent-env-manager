using AgentEnvManager.Core.Operations;

namespace AgentEnvManager.Core.Agents;

public sealed class AgentBindingManager(
    IAgentAdapter adapter,
    IOperationJournal? operationJournal = null,
    TimeProvider? timeProvider = null)
{
    private readonly IOperationJournal _operationJournal =
        operationJournal ?? new InMemoryOperationJournal();
    private readonly TimeProvider _timeProvider =
        timeProvider ?? TimeProvider.System;

    public async Task<AgentBinding> BindAsync(
        AgentBindingPlan plan,
        CancellationToken cancellationToken = default)
    {
        var operation = OperationStateMachine.Create(
            OperationType.AgentBinding,
            $"绑定 {plan.AgentName} 到 {plan.RuntimeName}",
            _timeProvider,
            target: plan.ConfigurationDirectory,
            impact: $"通过受管入口 {plan.ManagedEntryPath} 绑定 {plan.AgentName}。");
        await _operationJournal.SaveAsync(operation, cancellationToken);
        AgentBinding? binding = null;
        AgentConfigurationRecoveryPoint? recoveryPoint = null;
        try
        {
            operation = await SaveTransitionAsync(
                OperationStateMachine.MarkValidated(
                    operation,
                    _timeProvider),
                cancellationToken);
            recoveryPoint = await adapter.CreateRecoveryPointAsync(
                plan,
                cancellationToken);
            operation = await SaveTransitionAsync(
                OperationStateMachine.MarkRecoveryReady(
                    operation,
                    recoveryPoint.Id,
                    _timeProvider),
                cancellationToken);
            operation = await SaveTransitionAsync(
                OperationStateMachine.BeginExecution(
                    operation,
                    _timeProvider),
                cancellationToken);
            binding = await adapter.ActivateAsync(
                plan,
                recoveryPoint,
                cancellationToken);
            operation = await SaveTransitionAsync(
                OperationStateMachine.BeginVerification(
                    operation,
                    _timeProvider),
                cancellationToken);
            var health = await adapter.CheckHealthAsync(
                binding,
                cancellationToken);
            if (!health.IsHealthy)
            {
                throw new InvalidOperationException(health.Message);
            }

            operation = OperationStateMachine.Complete(operation, _timeProvider);
            await _operationJournal.SaveAsync(operation, cancellationToken);
            return binding with { IsHealthy = true };
        }
        catch (Exception exception)
        {
            var rollbackSucceeded = true;
            if (recoveryPoint is not null)
            {
                try
                {
                    await adapter.RollbackAsync(
                        plan,
                        recoveryPoint,
                        CancellationToken.None);
                }
                catch
                {
                    rollbackSucceeded = false;
                }
            }

            operation = OperationStateMachine.Fail(
                operation,
                exception.Message,
                _timeProvider);
            await _operationJournal.SaveAsync(operation, CancellationToken.None);
            if (rollbackSucceeded)
            {
                operation = OperationStateMachine.Rollback(
                    operation,
                    _timeProvider);
                await _operationJournal.SaveAsync(
                    operation,
                    CancellationToken.None);
            }

            throw new InvalidOperationException(
                rollbackSucceeded
                    ? $"Agent 绑定失败，已恢复原绑定: {exception.Message}"
                    : $"Agent 绑定失败，恢复原绑定失败: {exception.Message}",
                exception);
        }
    }

    private async Task<OperationRecord> SaveTransitionAsync(
        OperationRecord operation,
        CancellationToken cancellationToken)
    {
        await _operationJournal.SaveAsync(operation, cancellationToken);
        return operation;
    }
}
