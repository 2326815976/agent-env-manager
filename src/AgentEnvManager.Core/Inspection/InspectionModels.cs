namespace AgentEnvManager.Core.Inspection;

public enum EnvironmentAssetKind
{
    ToolRuntime,
    Shell,
    AgentConfiguration,
    PackageManager
}

public enum ManagementState
{
    Observed,
    Managed,
    Quarantined
}

public enum HealthState
{
    Unknown,
    Healthy,
    Degraded,
    Unhealthy
}

public enum PathScope
{
    Process,
    User,
    Machine
}

public sealed record EnvironmentAsset(
    EnvironmentAssetKind Kind,
    string Name,
    string? Version,
    string Location,
    bool IsSystemComponent,
    string Source);

public sealed record ObservedEnvironment(
    EnvironmentAsset Asset,
    ManagementState ManagementState,
    HealthState HealthState);

public sealed record PathEntry(
    string Value,
    PathScope Scope);

public sealed record PathConflict(
    string Path,
    int Occurrences,
    IReadOnlyList<PathScope> Scopes);

public sealed record EnvironmentProbeResult(
    IReadOnlyList<EnvironmentAsset> Assets,
    IReadOnlyList<PathEntry> PathEntries);

public sealed record InspectionReport(
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<ObservedEnvironment> Environments,
    IReadOnlyList<PathConflict> PathConflicts);

public interface IEnvironmentProbe
{
    Task<EnvironmentProbeResult> ProbeAsync(CancellationToken cancellationToken = default);
}
