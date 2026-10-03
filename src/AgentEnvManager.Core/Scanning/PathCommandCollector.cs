using AgentEnvManager.Core.Inspection;

namespace AgentEnvManager.Core.Scanning;

internal static class PathCommandCollector
{
    public static IReadOnlyList<ExecutableCandidate> Collect(
        IWindowsEnvironmentAccessor accessor,
        IReadOnlyList<EffectivePathEntry> pathEntries,
        IVersionProbe? versionProbe = null)
    {
        var extensions = ReadExecutableExtensions(accessor);
        var candidates = new List<ExecutableCandidate>();

        foreach (var command in CommandCatalog.Commands)
        {
            foreach (var pathEntry in pathEntries)
            {
                var path = default(string);
                foreach (var extension in extensions)
                {
                    var candidatePath = Path.Combine(
                        pathEntry.Value,
                        command.Command + extension);
                    if (accessor.FileExists(candidatePath))
                    {
                        path = candidatePath;
                        break;
                    }
                }

                if (path is not null)
                {
                    candidates.Add(CreateCandidate(
                        accessor,
                        command,
                        path,
                        DiscoverySourceInfo.PathCommand,
                        pathEntry.Order,
                        pathEntry.Scopes,
                        versionProbe));
                }
            }
        }

        // 同一安装目录下的多个入口（如 conda.exe 与 Library\bin\conda.bat、
        // Git 的 bash.exe 与 usr\bin\bash.exe）合并为一条，保留 PATH 中
        // 优先级最高的一条。
        return candidates
            .GroupBy(candidate => (
                candidate.Kind,
                candidate.Name,
                Root: InstallRootHeuristics.GetInstallRoot(
                    candidate.Path)))
            .Select(group => group.First())
            .ToArray();
    }

    internal static ExecutableCandidate CreateCandidate(
        IWindowsEnvironmentAccessor accessor,
        CommandDefinition command,
        string path,
        DiscoverySourceInfo source,
        int? resolutionOrder = null,
        IReadOnlyList<PathScope>? scopes = null,
        IVersionProbe? versionProbe = null)
    {
        return new ExecutableCandidate(
            command.Kind,
            command.Name,
            path,
            SystemExecutableClassifier.IsCanonicalSystemShell(
                command,
                path,
                accessor)
                || SystemExecutableClassifier.IsProtectedSystemPath(
                    path,
                    accessor),
            source,
            accessor.ReadFileVersion(path)
                ?? PackageManagerVersionReader.TryReadVersion(
                    command.Command,
                    path,
                    accessor)
                ?? versionProbe?.TryReadVersion(command.Command, path),
            resolutionOrder,
            scopes);
    }

    private static IReadOnlyList<string> ReadExecutableExtensions(
        IWindowsEnvironmentAccessor accessor)
    {
        var value = accessor.GetEnvironmentVariable("PATHEXT")
            ?? ".COM;.EXE;.BAT;.CMD";

        return value
            .Split(
                ';',
                StringSplitOptions.RemoveEmptyEntries
                | StringSplitOptions.TrimEntries)
            .Select(extension => extension.StartsWith('.')
                ? extension
                : $".{extension}")
            .ToArray();
    }
}
