namespace AgentEnvManager.Core.EnvironmentVariables;

internal sealed class InMemoryEnvironmentVariableRecoveryPointStore
    : IEnvironmentVariableRecoveryPointStore
{
    private readonly Dictionary<string, EnvironmentVariableRecoveryPoint>
        _points = [];

    public Task<EnvironmentVariableRecoveryPoint> CreateAsync(
        string operationId,
        IReadOnlyDictionary<string, string?> originalValues,
        IReadOnlyDictionary<string, bool>? expandableValues = null,
        CancellationToken cancellationToken = default)
    {
        var point = new EnvironmentVariableRecoveryPoint(
            Guid.NewGuid().ToString("N"),
            new Dictionary<string, string?>(
                originalValues,
                StringComparer.OrdinalIgnoreCase),
            DateTimeOffset.UtcNow,
            expandableValues is null
                ? null
                : new Dictionary<string, bool>(
                    expandableValues,
                    StringComparer.OrdinalIgnoreCase));
        _points[point.Id] = point;
        return Task.FromResult(point);
    }

    public Task<EnvironmentVariableRecoveryPoint?> GetAsync(
        string recoveryPointId,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_points.GetValueOrDefault(recoveryPointId));
    }
}
