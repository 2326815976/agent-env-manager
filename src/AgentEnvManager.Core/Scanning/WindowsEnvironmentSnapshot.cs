using AgentEnvManager.Core.Inspection;

namespace AgentEnvManager.Core.Scanning;

public sealed record ExecutableCandidate(
    EnvironmentAssetKind Kind,
    string Name,
    string Path,
    bool IsSystemComponent,
    DiscoverySourceInfo Source,
    string? Version = null,
    int? ResolutionOrder = null,
    IReadOnlyList<PathScope>? Scopes = null)
{
    public EnvironmentAsset ToEnvironmentAsset()
    {
        return new EnvironmentAsset(
            Kind,
            Name,
            Version,
            Path,
            IsSystemComponent,
            Source,
            ResolutionOrder,
            Scopes);
    }
}

public sealed record DirectoryCandidate(
    string Name,
    string Path,
    DiscoverySourceInfo Source)
{
    public EnvironmentAsset ToEnvironmentAsset()
    {
        return new EnvironmentAsset(
            EnvironmentAssetKind.AgentConfiguration,
            Name,
            Version: null,
            Path,
            IsSystemComponent: false,
            Source);
    }
}

public sealed record WindowsEnvironmentSnapshot(
    IReadOnlyList<ExecutableCandidate> Executables,
    IReadOnlyList<DirectoryCandidate> Directories,
    IReadOnlyList<PathEntry> PathEntries);

public interface IWindowsEnvironmentSnapshotSource
{
    WindowsEnvironmentSnapshot Capture();
}
