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
            .Where(IsManagedEntry)
            .Select(entry => Path.GetFullPath(
                Environment.ExpandEnvironmentVariables(entry))
                .TrimEnd('\\', '/'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private string ValidateDesiredEntry(string entry)
    {
        var expanded = Environment.ExpandEnvironmentVariables(entry);
        if (!Path.IsPathFullyQualified(expanded))
        {
            throw new InvalidOperationException(
                $"PATH 条目必须是完全限定路径: {entry}");
        }

        var fullPath = Path.GetFullPath(
            expanded)
            .TrimEnd('\\', '/');
        if (!IsUnderManagedRoot(fullPath))
        {
            throw new InvalidOperationException(
                $"PATH 条目不属于管理器 shims 根目录: {entry}");
        }

        return fullPath;
    }

    private bool IsManagedEntry(string entry)
    {
        if (string.IsNullOrWhiteSpace(entry))
        {
            return false;
        }

        try
        {
            var expanded = Environment.ExpandEnvironmentVariables(entry);
            if (!Path.IsPathFullyQualified(expanded))
            {
                return false;
            }

            return IsUnderManagedRoot(Path.GetFullPath(expanded)
                .TrimEnd('\\', '/'));
        }
        catch (Exception exception) when (
            exception is ArgumentException
            or NotSupportedException
            or PathTooLongException)
        {
            return false;
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
