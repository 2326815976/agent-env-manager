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
            .Select(candidate => candidate.ToEnvironmentAsset())
            .Concat(snapshot.Directories.Select(
                candidate => candidate.ToEnvironmentAsset()))
            .ToArray();

        return Task.FromResult(new EnvironmentProbeResult(
            assets,
            snapshot.PathEntries));
    }
}
