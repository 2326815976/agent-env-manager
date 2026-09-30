namespace AgentEnvManager.Core.Inspection;

public sealed class EnvironmentManager(
    IEnvironmentProbe probe,
    TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<InspectionReport> InspectAsync(CancellationToken cancellationToken = default)
    {
        var probeResult = await probe.ProbeAsync(cancellationToken);
        var observedEnvironments = probeResult.Assets
            .Select(asset => new ObservedEnvironment(
                asset,
                ManagementState.Observed,
                HealthState.Unknown))
            .ToArray();

        return new InspectionReport(
            _timeProvider.GetUtcNow(),
            observedEnvironments,
            FindPathConflicts(probeResult.PathEntries));
    }

    private static IReadOnlyList<PathConflict> FindPathConflicts(
        IEnumerable<PathEntry> pathEntries)
    {
        return pathEntries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Value))
            .GroupBy(entry => NormalizePath(entry.Value), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => new PathConflict(
                CleanPathForDisplay(group.First().Value),
                group.Count(),
                group.Select(entry => entry.Scope).Distinct().ToArray()))
            .OrderBy(conflict => conflict.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string NormalizePath(string path)
    {
        return CleanPathForDisplay(path)
            .Replace('/', '\\')
            .ToUpperInvariant();
    }

    private static string CleanPathForDisplay(string path)
    {
        var trimmed = path.Trim();
        if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"')
        {
            trimmed = trimmed[1..^1];
        }

        return trimmed.TrimEnd('\\', '/');
    }
}
