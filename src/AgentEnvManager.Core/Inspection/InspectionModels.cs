using AgentEnvManager.Core.Adoption;

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
    Managed
}

public enum HealthState
{
    Unknown
}

public enum PathScope
{
    Process,
    User,
    Machine
}

public enum DiscoverySource
{
    Unknown,
    Path,
    SystemPath,
    KnownInstallation,
    AppPathsRegistry,
    AgentConfiguration,
    UvRuntime
}

public sealed record DiscoverySourceInfo(
    DiscoverySource Kind,
    string Description)
{
    public static DiscoverySourceInfo PathCommand { get; } =
        new(DiscoverySource.Path, "PATH");

    public static DiscoverySourceInfo SystemPath { get; } =
        new(DiscoverySource.SystemPath, "系统路径");

    public static DiscoverySourceInfo KnownInstallation { get; } =
        new(DiscoverySource.KnownInstallation, "常见安装目录");

    public static DiscoverySourceInfo AppPathsRegistry { get; } =
        new(DiscoverySource.AppPathsRegistry, "App Paths 注册表");

    public static DiscoverySourceInfo AgentConfiguration { get; } =
        new(DiscoverySource.AgentConfiguration, "Agent 配置目录");

    public static DiscoverySourceInfo UvRuntime { get; } =
        new(DiscoverySource.UvRuntime, "uv 运行时目录");
}

public sealed record EnvironmentAsset(
    EnvironmentAssetKind Kind,
    string Name,
    string? Version,
    string Location,
    bool IsSystemComponent,
    DiscoverySourceInfo Source,
    int? ResolutionOrder = null,
    IReadOnlyList<PathScope>? Scopes = null);

public sealed record ObservedEnvironment(
    EnvironmentAsset Asset,
    EnvironmentFingerprint Fingerprint,
    ManagementState ManagementState,
    HealthState HealthState,
    EnvironmentIdentity? Identity = null);

public sealed record PathEntry(
    string Value,
    PathScope Scope);

public sealed record PathConflict(
    string Path,
    int Occurrences,
    IReadOnlyList<PathScope> Scopes);

public sealed record CommandPathConflict(
    string Name,
    IReadOnlyList<CommandPathCandidate> Candidates);

public sealed record CommandPathCandidate(
    string Path,
    int Order,
    bool Effective,
    IReadOnlyList<PathScope> Scopes);

public sealed record EnvironmentProbeResult(
    IReadOnlyList<EnvironmentAsset> Assets,
    IReadOnlyList<PathEntry> PathEntries);

public sealed record InspectionReport(
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<ObservedEnvironment> Environments,
    IReadOnlyList<PathConflict> PathConflicts,
    IReadOnlyList<CommandPathConflict> CommandPathConflicts);

public interface IEnvironmentProbe
{
    Task<EnvironmentProbeResult> ProbeAsync(CancellationToken cancellationToken = default);
}
