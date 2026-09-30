namespace AgentEnvManager.Core.Scanning;

public sealed class WindowsEnvironmentSnapshotSource(
    IWindowsEnvironmentAccessor? accessor = null)
    : IWindowsEnvironmentSnapshotSource
{
    private readonly IWindowsEnvironmentAccessor _accessor =
        accessor ?? new WindowsEnvironmentAccessor();

    public WindowsEnvironmentSnapshot Capture()
    {
        var pathEntries = PathEntryCollector.Collect(_accessor);
        var effectivePathEntries = PathEntryCollector.CollectEffective(_accessor);
        var executables = new Dictionary<string, ExecutableCandidate>(
            StringComparer.OrdinalIgnoreCase);
        var directories = new Dictionary<string, DirectoryCandidate>(
            StringComparer.OrdinalIgnoreCase);

        AddCandidates(
            executables,
            PathCommandCollector.Collect(_accessor, effectivePathEntries),
            candidate => candidate.Path,
            (candidate, path) => candidate with { Path = path });
        AddCandidates(
            executables,
            KnownInstallationCollector.Collect(_accessor),
            candidate => candidate.Path,
            (candidate, path) => candidate with { Path = path });
        AddCandidates(
            executables,
            RegistryAppPathCollector.Collect(_accessor),
            candidate => candidate.Path,
            (candidate, path) => candidate with { Path = path });
        AddCandidates(
            directories,
            AgentConfigurationCollector.Collect(_accessor),
            candidate => candidate.Path,
            (candidate, path) => candidate with { Path = path });

        return new WindowsEnvironmentSnapshot(
            executables.Values.ToArray(),
            directories.Values.ToArray(),
            pathEntries);
    }

    private static void AddCandidates<T>(
        IDictionary<string, T> target,
        IEnumerable<T> candidates,
        Func<T, string> getPath,
        Func<T, string, T> withPath)
    {
        foreach (var candidate in candidates)
        {
            var path = Path.GetFullPath(getPath(candidate));
            target.TryAdd(
                path,
                withPath(candidate, path));
        }
    }
}
