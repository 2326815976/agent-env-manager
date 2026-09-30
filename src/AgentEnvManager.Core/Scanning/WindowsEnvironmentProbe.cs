using AgentEnvManager.Core.Inspection;

namespace AgentEnvManager.Core.Scanning;

public sealed class WindowsEnvironmentProbe(
    IWindowsEnvironmentSnapshotSource snapshotSource)
    : IEnvironmentProbe
{
    public Task<EnvironmentProbeResult> ProbeAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = snapshotSource.Capture();
        var assets = snapshot.Executables
            .Select(candidate => new EnvironmentAsset(
                candidate.Kind,
                candidate.Name,
                candidate.Version,
                candidate.Path,
                candidate.IsSystemComponent,
                candidate.Source))
            .Concat(snapshot.Directories.Select(candidate => new EnvironmentAsset(
                EnvironmentAssetKind.AgentConfiguration,
                candidate.Name,
                Version: null,
                candidate.Path,
                IsSystemComponent: false,
                candidate.Source)))
            .ToArray();

        return Task.FromResult(new EnvironmentProbeResult(
            assets,
            snapshot.PathEntries));
    }
}
