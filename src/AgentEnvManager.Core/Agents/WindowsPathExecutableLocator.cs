namespace AgentEnvManager.Core.Agents;

public sealed class WindowsPathExecutableLocator(
    Func<string, string?>? readEnvironmentVariable = null)
    : IExecutableLocator
{
    private readonly Func<string, string?> _readEnvironmentVariable =
        readEnvironmentVariable ?? Environment.GetEnvironmentVariable;

    public string? FindExecutable(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return null;
        }

        if (Path.IsPathFullyQualified(command))
        {
            return File.Exists(command)
                ? Path.GetFullPath(command)
                : null;
        }

        var path = _readEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var extensions = GetExecutableExtensions(
            _readEnvironmentVariable("PATHEXT"));
        var fileNames = Path.HasExtension(command)
            ? [command]
            : extensions.Select(extension => command + extension).ToArray();
        foreach (var directory in path.Split(
            ';',
            StringSplitOptions.RemoveEmptyEntries
            | StringSplitOptions.TrimEntries))
        {
            var expandedDirectory = Environment.ExpandEnvironmentVariables(
                directory.Trim('"'));
            if (string.IsNullOrWhiteSpace(expandedDirectory))
            {
                continue;
            }

            foreach (var fileName in fileNames)
            {
                var candidate = Path.Combine(
                    expandedDirectory,
                    fileName);
                if (File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }
        }

        return null;
    }

    private static IReadOnlyList<string> GetExecutableExtensions(
        string? value)
    {
        value = string.IsNullOrWhiteSpace(value)
            ? ".COM;.EXE;.BAT;.CMD"
            : value;
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
