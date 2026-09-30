using AgentEnvManager.Core.Operations;
using System.Collections.ObjectModel;

namespace AgentEnvManager.Core.EnvironmentVariables;

public sealed record EnvironmentVariableChange(
    string Name,
    string? Value);

public sealed record EnvironmentVariableUpdatePreview
{
    public EnvironmentVariableUpdatePreview(
        IReadOnlyDictionary<string, string?> originalValues,
        IReadOnlyDictionary<string, string?> desiredValues,
        IReadOnlyList<EnvironmentVariableChange> changes,
        string impact)
    {
        OriginalValues = new ReadOnlyDictionary<string, string?>(
            new Dictionary<string, string?>(
                originalValues,
                StringComparer.OrdinalIgnoreCase));
        DesiredValues = new ReadOnlyDictionary<string, string?>(
            new Dictionary<string, string?>(
                desiredValues,
                StringComparer.OrdinalIgnoreCase));
        Changes = Array.AsReadOnly(changes.ToArray());
        Impact = impact;
    }

    public IReadOnlyDictionary<string, string?> OriginalValues { get; }

    public IReadOnlyDictionary<string, string?> DesiredValues { get; }

    public IReadOnlyList<EnvironmentVariableChange> Changes { get; }

    public string Impact { get; }
}

public sealed record EnvironmentVariableRecoveryPoint(
    string Id,
    IReadOnlyDictionary<string, string?> OriginalValues,
    DateTimeOffset CreatedAtUtc);

public sealed record EnvironmentVariableTransactionResult(
    OperationRecord Operation,
    EnvironmentVariableRecoveryPoint RecoveryPoint);

public interface IEnvironmentVariableRecoveryPointStore
{
    Task<EnvironmentVariableRecoveryPoint> CreateAsync(
        string operationId,
        IReadOnlyDictionary<string, string?> originalValues,
        CancellationToken cancellationToken = default);

    Task<EnvironmentVariableRecoveryPoint?> GetAsync(
        string recoveryPointId,
        CancellationToken cancellationToken = default);
}

public interface IUserEnvironmentVariableStore
{
    Task<string?> GetAsync(
        string name,
        CancellationToken cancellationToken = default);

    Task SetAsync(
        string name,
        string? value,
        CancellationToken cancellationToken = default);

    Task BroadcastAsync(
        CancellationToken cancellationToken = default);
}
