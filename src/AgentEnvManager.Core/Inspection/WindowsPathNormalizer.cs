namespace AgentEnvManager.Core.Inspection;

internal static class WindowsPathNormalizer
{
    public static string CleanForDisplay(string path)
    {
        var trimmed = path.Trim();
        if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"')
        {
            trimmed = trimmed[1..^1];
        }

        var normalizedSeparators = trimmed.Replace('/', '\\');
        var root = Path.GetPathRoot(normalizedSeparators);
        var withoutTrailingSeparators = normalizedSeparators.TrimEnd('\\');
        if (!string.IsNullOrWhiteSpace(root)
            && string.Equals(
                withoutTrailingSeparators,
                root.TrimEnd('\\'),
                StringComparison.OrdinalIgnoreCase))
        {
            return root;
        }

        return withoutTrailingSeparators;
    }

    public static string NormalizeForComparison(string path)
    {
        return CleanForDisplay(path).ToUpperInvariant();
    }
}
