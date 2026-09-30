using AgentEnvManager.Core.Adoption;

namespace AgentEnvManager.Core.Inspection;

internal sealed class EnvironmentInspector(
    IEnvironmentProbe probe,
    IEnvironmentManifestStore manifestStore,
    TimeProvider timeProvider)
{
    public async Task<InspectionReport> InspectAsync(
        CancellationToken cancellationToken = default)
    {
        var probeResult = await probe.ProbeAsync(cancellationToken);
        var manifests = await manifestStore.ReadAllAsync(cancellationToken);
        var manifestsByFingerprint = manifests.ToDictionary(
            manifest => manifest.Fingerprint);
        var observedEnvironments = probeResult.Assets
            .Select(asset => InspectAsset(asset, manifestsByFingerprint))
            .ToArray();

        return new InspectionReport(
            timeProvider.GetUtcNow(),
            observedEnvironments,
            FindPathConflicts(probeResult.PathEntries),
            FindCommandPathConflicts(probeResult.Assets));
    }

    internal static EnvironmentFingerprint CreateFingerprint(
        EnvironmentAsset asset)
    {
        var value = string.Join(
            '\n',
            asset.Kind,
            asset.Name,
            asset.Version,
            asset.Source.Kind,
            WindowsPathNormalizer.NormalizeForComparison(asset.Location));
        return new EnvironmentFingerprint(Hash(value));
    }

    private static ObservedEnvironment InspectAsset(
        EnvironmentAsset asset,
        IReadOnlyDictionary<EnvironmentFingerprint, EnvironmentManifest> manifests)
    {
        var fingerprint = CreateFingerprint(asset);
        manifests.TryGetValue(fingerprint, out var manifest);
        var managementState = manifest is null
            ? ManagementState.Observed
            : ManagementState.Managed;

        return new ObservedEnvironment(
            asset,
            fingerprint,
            managementState,
            HealthState.Unknown,
            manifest?.Identity);
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

    private static string Hash(string value)
    {
        return Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(value)));
    }
}
