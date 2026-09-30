using AgentEnvManager.Core.Inspection;

namespace AgentEnvManager.Core.Scanning;

internal sealed record EffectivePathEntry(
    string Value,
    int Order,
    IReadOnlyList<PathScope> Scopes);

internal static class PathEntryCollector
{
    public static IReadOnlyList<PathEntry> Collect(
        IWindowsEnvironmentAccessor accessor)
    {
        var userEntries = Read(accessor, PathScope.User);
        var machineEntries = Read(accessor, PathScope.Machine);
        var knownEntries = userEntries
            .Concat(machineEntries)
            .Select(entry => WindowsPathNormalizer.NormalizeForComparison(entry.Value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var processOnlyEntries = Read(accessor, PathScope.Process)
            .Where(entry => !knownEntries.Contains(
                WindowsPathNormalizer.NormalizeForComparison(entry.Value)));

        return userEntries
            .Concat(machineEntries)
            .Concat(processOnlyEntries)
            .ToArray();
    }

    public static IReadOnlyList<EffectivePathEntry> CollectEffective(
        IWindowsEnvironmentAccessor accessor)
    {
        var userEntries = ReadAbsolute(accessor, PathScope.User)
            .Select(WindowsPathNormalizer.NormalizeForComparison)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var machineEntries = ReadAbsolute(accessor, PathScope.Machine)
            .Select(WindowsPathNormalizer.NormalizeForComparison)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return ReadAbsolute(accessor, PathScope.Process)
            .Select((value, order) =>
            {
                var normalized = WindowsPathNormalizer.NormalizeForComparison(value);
                var scopes = new List<PathScope>();

                if (userEntries.Contains(normalized))
                {
                    scopes.Add(PathScope.User);
                }

                if (machineEntries.Contains(normalized))
                {
                    scopes.Add(PathScope.Machine);
                }

                if (scopes.Count == 0)
                {
                    scopes.Add(PathScope.Process);
                }

                return new EffectivePathEntry(value, order, scopes);
            })
            .ToArray();
    }

    private static IReadOnlyList<PathEntry> Read(
        IWindowsEnvironmentAccessor accessor,
        PathScope scope)
    {
        return ReadAbsolute(accessor, scope)
            .Select(value => new PathEntry(value, scope))
            .ToArray();
    }

    private static IReadOnlyList<string> ReadAbsolute(
        IWindowsEnvironmentAccessor accessor,
        PathScope scope)
    {
        return accessor.ReadPath(scope)
            .Select(value => ToAbsolutePath(accessor, value))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
    }

    private static string ToAbsolutePath(
        IWindowsEnvironmentAccessor accessor,
        string value)
    {
        var expanded = accessor.ExpandEnvironmentVariables(
            value.Trim().Trim('"'));

        try
        {
            return Path.GetFullPath(expanded);
        }
        catch (ArgumentException)
        {
            return expanded;
        }
        catch (NotSupportedException)
        {
            return expanded;
        }
    }
}
