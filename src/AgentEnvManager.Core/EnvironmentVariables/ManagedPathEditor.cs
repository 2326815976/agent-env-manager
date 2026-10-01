namespace AgentEnvManager.Core.EnvironmentVariables;

internal sealed class ManagedPathEditor(string managedPathRoot)
{
    private readonly string _managedPathRoot = Path.GetFullPath(
        Environment.ExpandEnvironmentVariables(managedPathRoot))
        .TrimEnd('\\', '/');

    public string Apply(
        string? currentPath,
        IReadOnlyList<string> desiredEntries)
    {
        var desired = desiredEntries
            .Select(ValidateDesiredEntry)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var currentEntries = string.IsNullOrWhiteSpace(currentPath)
            ? []
            : currentPath.Split(';');
        var preserved = new List<string>();
        var insertionIndex = -1;

        foreach (var entry in currentEntries)
        {
            if (IsManagedEntry(entry))
            {
                if (insertionIndex < 0)
                {
                    insertionIndex = preserved.Count;
                }

                continue;
            }

            preserved.Add(entry);
        }

        if (desired.Length == 0)
        {
            return string.Join(';', preserved);
        }

        preserved.InsertRange(
            insertionIndex < 0 ? preserved.Count : insertionIndex,
            desired);
        return string.Join(';', preserved);
    }

    public IReadOnlyList<string> ExtractManagedEntries(string? currentPath)
    {
        if (string.IsNullOrWhiteSpace(currentPath))
        {
            return [];
        }

        return currentPath
            .Split(';')
            .Select(Normalize)
            .Where(normalized =>
                normalized is not null
                && IsUnderManagedRoot(normalized))
            .Select(normalized => normalized!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private string ValidateDesiredEntry(string entry)
    {
        var fullPath = Normalize(entry);
        if (fullPath is null)
        {
            throw new InvalidOperationException(
                $"PATH 条目必须是完全限定路径: {entry}");
        }

        if (!IsUnderManagedRoot(fullPath))
        {
            throw new InvalidOperationException(
                $"PATH 条目不属于管理器 shims 根目录: {entry}");
        }

        return fullPath;
    }

    internal bool IsManagedEntry(string entry)
    {
        var normalized = Normalize(entry);
        return normalized is not null
            && IsUnderManagedRoot(normalized);
    }

    internal string? Normalize(string entry)
    {
        if (string.IsNullOrWhiteSpace(entry))
        {
            return null;
        }

        try
        {
            var expanded = Environment.ExpandEnvironmentVariables(entry);
            return Path.IsPathFullyQualified(expanded)
                ? Path.GetFullPath(expanded).TrimEnd('\\', '/')
                : null;
        }
        catch (Exception exception) when (
            exception is ArgumentException
            or NotSupportedException
            or PathTooLongException)
        {
            return null;
        }
    }

    private bool IsUnderManagedRoot(string path)
    {
        return string.Equals(
                path,
                _managedPathRoot,
                StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(
                $"{_managedPathRoot}\\",
                StringComparison.OrdinalIgnoreCase);
    }
}
