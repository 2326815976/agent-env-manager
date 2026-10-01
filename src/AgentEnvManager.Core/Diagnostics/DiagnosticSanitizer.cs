using System.Text.RegularExpressions;
using AgentEnvManager.Core.Runtimes;

namespace AgentEnvManager.Core.Diagnostics;

internal sealed record DiagnosticSanitizationContext(
    string UserProfile,
    string StateRoot,
    string DataRoot);

internal static class DiagnosticSanitizer
{
    private static readonly Regex SecretPattern = new(
        @"(?im)\b(password|passwd|token|secret|api[_-]?key|authorization|oauth|extraheader)\b\s*[:=]\s*[^\r\n]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex CredentialUrlPattern = new(
        @"://[^/\s:@]+:[^/\s@]+@",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex SensitiveNamePattern = new(
        @"(?i)(\.ssh|\.git-credentials|\.netrc|_netrc|id_rsa|id_ed25519)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex AbsolutePathPattern = new(
        @"(?<![A-Za-z0-9])(?:[A-Za-z]:\\|\\\\)[^\s,;""']+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string SanitizePath(
        string? path,
        DiagnosticSanitizationContext context)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        if (GitSensitiveDataPolicy.IsProtectedPath(path))
        {
            return "[sensitive-path]";
        }

        return SanitizeText(path, context);
    }

    public static string SanitizeText(
        string? text,
        DiagnosticSanitizationContext context)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var sanitized = text
            .Replace(
                context.UserProfile,
                "%USERPROFILE%",
                StringComparison.OrdinalIgnoreCase)
            .Replace(
                context.StateRoot,
                "%STATE_ROOT%",
                StringComparison.OrdinalIgnoreCase)
            .Replace(
                context.DataRoot,
                "%DATA_ROOT%",
                StringComparison.OrdinalIgnoreCase);
        sanitized = SecretPattern.Replace(
            sanitized,
            match => $"{match.Groups[1].Value}=<redacted>");
        sanitized = CredentialUrlPattern.Replace(
            sanitized,
            "://<redacted>@");
        sanitized = SensitiveNamePattern.Replace(
            sanitized,
            "[sensitive-path]");
        sanitized = AbsolutePathPattern.Replace(
            sanitized,
            "[path]");
        return sanitized;
    }
}
