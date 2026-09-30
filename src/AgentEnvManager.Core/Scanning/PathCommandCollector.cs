using AgentEnvManager.Core.Inspection;

namespace AgentEnvManager.Core.Scanning;

internal static class PathCommandCollector
{
    public static IReadOnlyList<ExecutableCandidate> Collect(
        IWindowsEnvironmentAccessor accessor,
        IReadOnlyList<EffectivePathEntry> pathEntries)
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
                        pathEntry.Scopes));
                }
            }
        }

        return candidates;
    }

    internal static ExecutableCandidate CreateCandidate(
        IWindowsEnvironmentAccessor accessor,
        CommandDefinition command,
        string path,
        DiscoverySourceInfo source,
        int? resolutionOrder = null,
        IReadOnlyList<PathScope>? scopes = null)
    {
        return new ExecutableCandidate(
            command.Kind,
            command.Name,
            path,
            SystemExecutableClassifier.IsCanonicalSystemShell(
                command,
                path,
                accessor),
            source,
            accessor.ReadFileVersion(path),
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
