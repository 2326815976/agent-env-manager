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
    private readonly HashSet<string> _expandableNames =
        new(StringComparer.OrdinalIgnoreCase);

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

    public Task<bool> IsExpandableAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_expandableNames.Contains(name));
    }

    public Task<IReadOnlyList<string>> ListAsync(
        string namePrefix,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<string>>(
            _values.Keys
                .Where(name => name.StartsWith(
                    namePrefix,
                    StringComparison.OrdinalIgnoreCase))
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }

    public Task SetAsync(
        string name,
        string? value,
        bool isExpandable = false,
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
            _expandableNames.Remove(name);
        }
        else
        {
            _values[name] = value;
            if (isExpandable)
            {
                _expandableNames.Add(name);
            }
            else
            {
                _expandableNames.Remove(name);
            }
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

    public Task<IReadOnlyList<EnvironmentVariableRecoveryPoint>>
        ReadAllAsync(
            CancellationToken cancellationToken = default)
    {
        return Task.FromResult<
            IReadOnlyList<EnvironmentVariableRecoveryPoint>>(
            _points.Values.ToArray());
    }

    public Task<EnvironmentVariableRecoveryPoint> CreateAsync(
        string operationId,
        IReadOnlyDictionary<string, string?> originalValues,
        IReadOnlyDictionary<string, bool>? expandableValues = null,
        CancellationToken cancellationToken = default)
    {
        var point = new EnvironmentVariableRecoveryPoint(
            $"environment-variables-{_points.Count + 1}",
            new Dictionary<string, string?>(
                originalValues,
                StringComparer.OrdinalIgnoreCase),
            DateTimeOffset.UnixEpoch,
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
