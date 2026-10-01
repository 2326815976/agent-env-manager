using System.Text.RegularExpressions;

namespace AgentEnvManager.Core.Runtimes;

public static class GitSensitiveDataPolicy
{
    private static readonly Regex PotentialSecretPattern = new(
        @"(?im)(?:\b(password|passwd|token|secret|api[_-]?key|authorization|oauth)\b|extraheader\s*=|://[^/\s:@]+:[^/\s@]+@)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static IReadOnlyList<string> GetProtectedPaths(
        string userProfile)
    {
        return
        [
            Path.Combine(userProfile, ".ssh"),
            Path.Combine(userProfile, ".git-credentials"),
            Path.Combine(userProfile, ".netrc"),
            Path.Combine(userProfile, "_netrc")
        ];
    }

    public static bool IsProtectedPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var fileName = Path.GetFileName(fullPath);
        return fullPath
                   .Split(
                       [Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar],
                       StringSplitOptions.RemoveEmptyEntries)
                   .Any(segment => string.Equals(
                       segment,
                       ".ssh",
                       StringComparison.OrdinalIgnoreCase))
            || string.Equals(
                fileName,
                ".git-credentials",
                StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                fileName,
                ".netrc",
                StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                fileName,
                "_netrc",
                StringComparison.OrdinalIgnoreCase);
    }

    public static bool ContainsPotentialSecret(string content)
    {
        return PotentialSecretPattern.IsMatch(content);
    }
}
