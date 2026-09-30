using AgentEnvManager.Core.EnvironmentVariables;

namespace AgentEnvManager.Core.Tests.TestSupport;

internal sealed class RecordingUserEnvironmentVariableStore(
    IReadOnlyDictionary<string, string?>? values = null)
    : IUserEnvironmentVariableStore
{
    private readonly Dictionary<string, string?> _values =
        values is null
            ? new Dictionary<string, string?>(
                StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string?>(
                values,
                StringComparer.OrdinalIgnoreCase);

    public int BroadcastCount { get; private set; }

    public List<(string Name, string? Value)> SetHistory { get; } = [];

    public Func<string, string?, Exception?>? FailOnSet { get; set; }

    public bool FailOnBroadcastOnce { get; set; }

    public Task<string?> GetAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_values.GetValueOrDefault(name));
    }

    public Task SetAsync(
        string name,
        string? value,
        CancellationToken cancellationToken = default)
    {
        var failure = FailOnSet?.Invoke(name, value);
        if (failure is not null)
        {
            throw failure;
        }

        SetHistory.Add((name, value));
        if (value is null)
        {
            _values.Remove(name);
        }
        else
        {
            _values[name] = value;
        }

        return Task.CompletedTask;
    }

    public Task BroadcastAsync(
        CancellationToken cancellationToken = default)
    {
        if (FailOnBroadcastOnce)
        {
            FailOnBroadcastOnce = false;
            throw new InvalidOperationException("模拟广播失败。");
        }

        BroadcastCount++;
        return Task.CompletedTask;
    }
}

internal sealed class RecordingEnvironmentVariableRecoveryPointStore
    : IEnvironmentVariableRecoveryPointStore
{
    private readonly Dictionary<string, EnvironmentVariableRecoveryPoint>
        _points = [];

    public Task<EnvironmentVariableRecoveryPoint> CreateAsync(
        string operationId,
        IReadOnlyDictionary<string, string?> originalValues,
        CancellationToken cancellationToken = default)
    {
        var point = new EnvironmentVariableRecoveryPoint(
            $"environment-variables-{_points.Count + 1}",
            new Dictionary<string, string?>(
                originalValues,
                StringComparer.OrdinalIgnoreCase),
            DateTimeOffset.UnixEpoch);
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
