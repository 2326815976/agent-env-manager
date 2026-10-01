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
        string impact,
        IReadOnlyDictionary<string, bool>? expandableValues = null,
        string authorizationToken = "")
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
        ExpandableValues = new ReadOnlyDictionary<string, bool>(
            expandableValues is null
                ? new Dictionary<string, bool>(
                    StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, bool>(
                    expandableValues,
                    StringComparer.OrdinalIgnoreCase));
        AuthorizationToken = authorizationToken;
    }

    public IReadOnlyDictionary<string, string?> OriginalValues { get; }

    public IReadOnlyDictionary<string, string?> DesiredValues { get; }

    public IReadOnlyList<EnvironmentVariableChange> Changes { get; }

    public string Impact { get; }

    public IReadOnlyDictionary<string, bool> ExpandableValues { get; }

    public string AuthorizationToken { get; }
}

public sealed record EnvironmentVariableRecoveryPoint(
    string Id,
    IReadOnlyDictionary<string, string?> OriginalValues,
    DateTimeOffset CreatedAtUtc,
    IReadOnlyDictionary<string, bool>? ExpandableValues = null);

public sealed record EnvironmentVariableTransactionResult(
    OperationRecord Operation,
    EnvironmentVariableRecoveryPoint RecoveryPoint);

public sealed record EnvironmentVariableEditorPathEntry(
    string Name,
    string? Version,
    string ManagedEntryPath,
    bool IsEnabled,
    bool IsManaged = true);

public sealed record EnvironmentVariableEditorVariable(
    string Name,
    string? Value,
    bool IsExpandable,
    bool IsManaged = false,
    bool IsHighRisk = false);

public sealed record EnvironmentVariableEditorSnapshot(
    string? Path,
    IReadOnlyList<EnvironmentVariableEditorPathEntry> PathEntries,
    IReadOnlyList<EnvironmentVariableEditorVariable> Variables,
    IReadOnlyList<EnvironmentVariableEditorVariable> MachineVariables,
    string MachineScopeDescription)
{
    public EnvironmentVariableEditorSnapshot(
        string? path,
        IReadOnlyList<EnvironmentVariableEditorPathEntry> pathEntries,
        IReadOnlyList<EnvironmentVariableEditorVariable> variables)
        : this(path, pathEntries, variables, [], string.Empty)
    {
    }
}

public interface IMachineEnvironmentVariableReader
{
    Task<IReadOnlyDictionary<string, string?>> ReadAllAsync(
        CancellationToken cancellationToken = default);
}

public interface IEnvironmentVariableRecoveryPointStore
{
    Task<IReadOnlyList<EnvironmentVariableRecoveryPoint>> ReadAllAsync(
        CancellationToken cancellationToken = default);

    Task<EnvironmentVariableRecoveryPoint> CreateAsync(
        string operationId,
        IReadOnlyDictionary<string, string?> originalValues,
        IReadOnlyDictionary<string, bool>? expandableValues = null,
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

    Task<bool> IsExpandableAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(false);
    }

    Task<IReadOnlyList<string>> ListAsync(
        string namePrefix,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<string>>([]);
    }

    Task<IReadOnlyList<string>> ListAllAsync(
        CancellationToken cancellationToken = default);

    Task SetAsync(
        string name,
        string? value,
        bool isExpandable = false,
        CancellationToken cancellationToken = default);

    Task BroadcastAsync(
        CancellationToken cancellationToken = default);
}
