namespace AgentEnvManager.Core.EnvironmentVariables;

public enum VersionResolutionSource
{
    LaunchOverride,
    Project,
    AgentBinding,
    UserDefault,
    System
}

public sealed record VersionResolutionRequest(
    string RuntimeName,
    string? LaunchOverride = null,
    string? Project = null,
    string? AgentBinding = null,
    string? UserDefault = null,
    string? System = null);

public sealed record VersionResolutionCandidate(
    VersionResolutionSource Source,
    string? Version,
    string Explanation);

public sealed record VersionResolutionResult(
    string RuntimeName,
    string? Version,
    VersionResolutionSource? Source,
    string Explanation,
    IReadOnlyList<VersionResolutionCandidate> Candidates);

internal static class VersionResolver
{
    public static VersionResolutionResult Resolve(VersionResolutionRequest request)
    {
        var candidates = new[]
        {
            CreateCandidate(
                VersionResolutionSource.LaunchOverride,
                "单次启动覆盖",
                request.LaunchOverride),
            CreateCandidate(
                VersionResolutionSource.Project,
                "项目要求",
                request.Project),
            CreateCandidate(
                VersionResolutionSource.AgentBinding,
                "Agent 绑定",
                request.AgentBinding),
            CreateCandidate(
                VersionResolutionSource.UserDefault,
                "用户默认值",
                request.UserDefault),
            CreateCandidate(
                VersionResolutionSource.System,
                "系统当前值",
                request.System)
        };
        var selected = candidates.FirstOrDefault(candidate =>
            !string.IsNullOrWhiteSpace(candidate.Version));

        return selected is null
            ? new VersionResolutionResult(
                request.RuntimeName,
                Version: null,
                Source: null,
                $"未为 {request.RuntimeName} 找到可用版本。",
                candidates)
            : new VersionResolutionResult(
                request.RuntimeName,
                selected.Version,
                selected.Source,
                $"{string.Join(
                    "；",
                    candidates.Select(candidate => candidate.Explanation))}；选择 {selected.Explanation}。",
                candidates);
    }

    private static VersionResolutionCandidate CreateCandidate(
        VersionResolutionSource source,
        string explanation,
        string? version)
    {
        return new VersionResolutionCandidate(
            source,
            string.IsNullOrWhiteSpace(version) ? null : version,
            string.IsNullOrWhiteSpace(version)
                ? $"{explanation} 未提供"
                : $"{explanation} 提供 {version}");
    }
}
