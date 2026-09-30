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
        try
        {
            operation = await SaveTransitionAsync(
                OperationStateMachine.MarkValidated(
                    operation,
                    _timeProvider),
                cancellationToken);
            var recoveryPoint = await adapter.CreateRecoveryPointAsync(
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
            if (binding is not null)
            {
                await adapter.RollbackAsync(binding, CancellationToken.None);
            }

            operation = OperationStateMachine.Fail(
                operation,
                exception.Message,
                _timeProvider);
            await _operationJournal.SaveAsync(operation, CancellationToken.None);
            operation = OperationStateMachine.Rollback(operation, _timeProvider);
            await _operationJournal.SaveAsync(operation, CancellationToken.None);
            throw new InvalidOperationException(
                $"Agent 健康检查失败，已恢复原绑定: {exception.Message}",
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
