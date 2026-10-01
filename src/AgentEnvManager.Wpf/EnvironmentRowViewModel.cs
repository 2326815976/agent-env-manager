using AgentEnvManager.Core.Inspection;

namespace AgentEnvManager.Wpf;

public sealed class EnvironmentRowViewModel(ObservedEnvironment environment)
{
    public AgentEnvManager.Core.Adoption.EnvironmentFingerprint Fingerprint
    {
        get;
    } = environment.Fingerprint;

    public string TypeLabel { get; } =
        EnvironmentLabelFormatter.FormatKind(environment.Asset.Kind);

    public string Name { get; } = environment.Asset.Name;

    public string Version { get; } = environment.Asset.Version ?? "未知";

    public string Location { get; } = environment.Asset.Location;

    public string ManagementStateLabel { get; } =
        environment.ManagementState == ManagementState.Managed
            ? "已纳管"
            : "仅观测";

    public bool IsManaged { get; } =
        environment.ManagementState == ManagementState.Managed;

    public string HealthStateLabel { get; } =
        EnvironmentLabelFormatter.FormatHealth(environment.HealthState);
}

public sealed record PathConflictViewModel(
    string Label,
    string ScopesLabel)
{
    public static PathConflictViewModel From(PathConflict conflict)
    {
        var scopes = string.Join(
            "、",
            conflict.Scopes.Select(FormatScope));
        return new PathConflictViewModel(
            $"{conflict.Path}（{conflict.Occurrences} 次）",
            scopes);
    }

    private static string FormatScope(PathScope scope)
    {
        return scope switch
        {
            PathScope.Process => "进程",
            PathScope.User => "用户",
            PathScope.Machine => "机器",
            _ => scope.ToString()
        };
    }
}

public sealed record CommandPathConflictViewModel(
    string Name,
    string Summary)
{
    public static CommandPathConflictViewModel From(
        CommandPathConflict conflict)
    {
        var summary = string.Join(
            "；",
            conflict.Candidates.Select(candidate =>
                $"{candidate.Path}（顺序 {candidate.Order + 1}，" +
                $"{(candidate.Effective ? "有效" : "被遮蔽")}）"));
        return new CommandPathConflictViewModel(conflict.Name, summary);
    }
}
