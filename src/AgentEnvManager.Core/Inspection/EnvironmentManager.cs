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
            FindPathConflicts(probeResult.PathEntries),
            FindCommandPathConflicts(probeResult.Assets));
    }

    private static IReadOnlyList<PathConflict> FindPathConflicts(
        IEnumerable<PathEntry> pathEntries)
    {
        return pathEntries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Value))
            .GroupBy(
                entry => WindowsPathNormalizer.NormalizeForComparison(entry.Value),
                StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => new PathConflict(
                WindowsPathNormalizer.CleanForDisplay(group.First().Value),
                group.Count(),
                group.Select(entry => entry.Scope).Distinct().ToArray()))
            .OrderBy(conflict => conflict.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<CommandPathConflict> FindCommandPathConflicts(
        IEnumerable<EnvironmentAsset> assets)
    {
        return assets
            .Where(asset =>
                asset.Kind != EnvironmentAssetKind.AgentConfiguration
                && asset.Source.Kind == DiscoverySource.Path)
            .GroupBy(
                asset => (asset.Kind, asset.Name),
                EqualityComparer<(EnvironmentAssetKind, string)>.Default)
            .Where(group => group
                .Select(asset => WindowsPathNormalizer.NormalizeForComparison(asset.Location))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() > 1)
            .Select(group =>
            {
                var ordered = group
                    .Select((asset, index) => (Asset: asset, Index: index))
                    .OrderBy(item => item.Asset.ResolutionOrder ?? item.Index)
                    .ToArray();

                return new CommandPathConflict(
                    group.Key.Item2,
                    ordered
                        .Select((item, index) => new CommandPathCandidate(
                            item.Asset.Location,
                            index,
                            Effective: index == 0,
                            item.Asset.Scopes ?? []))
                        .ToArray());
            })
            .OrderBy(conflict => conflict.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
