namespace AgentEnvManager.Core.Operations;

internal sealed class InMemoryOperationJournal : IOperationJournal
{
    private readonly List<OperationRecord> _operations = [];

    public Task SaveAsync(
        OperationRecord operation,
        CancellationToken cancellationToken = default)
    {
        lock (_operations)
        {
            _operations.RemoveAll(item => item.Id == operation.Id);
            _operations.Add(operation);
        }

        return Task.CompletedTask;
    }

    public Task<OperationRecord?> GetAsync(
        string operationId,
        CancellationToken cancellationToken = default)
    {
        lock (_operations)
        {
            return Task.FromResult(_operations.LastOrDefault(
                operation => operation.Id == operationId));
        }
    }

}
