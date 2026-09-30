using AgentEnvManager.Core.Inspection;

namespace AgentEnvManager.Core.Scanning;

public sealed record ExecutableCandidate(
    EnvironmentAssetKind Kind,
    string Name,
    string Path,
    bool IsSystemComponent,
    string Source,
    string? Version = null);

public sealed record DirectoryCandidate(
    string Name,
    string Path,
    string Source);

public sealed record WindowsEnvironmentSnapshot(
    IReadOnlyList<ExecutableCandidate> Executables,
    IReadOnlyList<DirectoryCandidate> Directories,
    IReadOnlyList<PathEntry> PathEntries);

public interface IWindowsEnvironmentSnapshotSource
{
    WindowsEnvironmentSnapshot Capture();
}
