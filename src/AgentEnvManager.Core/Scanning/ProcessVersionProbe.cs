using System.Diagnostics;
using System.Text.RegularExpressions;

namespace AgentEnvManager.Core.Scanning;

public interface IVersionProbe
{
    string? TryReadVersion(string command, string executablePath);
}

/// 只对白名单内的包管理器执行 `<tool> --version`，2 秒超时，
/// 只保留版本号本身，不读取也不记录其余输出。
internal sealed partial class ProcessVersionProbe : IVersionProbe
{
    private const int TimeoutMilliseconds = 2000;

    public static ProcessVersionProbe Instance { get; } = new();

    private static readonly HashSet<string> AllowedCommands =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "npm",
            "npx",
            "pnpm",
            "uv",
            "conda"
        };

    public string? TryReadVersion(string command, string executablePath)
    {
        if (!AllowedCommands.Contains(command)
            || string.IsNullOrWhiteSpace(executablePath)
            || !File.Exists(executablePath))
        {
            return null;
        }

        try
        {
            var extension = Path.GetExtension(executablePath);
            var isScript = string.Equals(
                    extension,
                    ".cmd",
                    StringComparison.OrdinalIgnoreCase)
                || string.Equals(
                    extension,
                    ".bat",
                    StringComparison.OrdinalIgnoreCase);
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = isScript ? "cmd.exe" : executablePath,
                Arguments = isScript
                    ? $"/c \"\"{executablePath}\" --version\""
                    : "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (process is null)
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(TimeoutMilliseconds))
            {
                TryKill(process);
                return null;
            }

            if (process.ExitCode != 0)
            {
                return null;
            }

            var match = VersionPattern().Match(output);
            return match.Success ? match.Value : null;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
            or System.ComponentModel.Win32Exception
            or IOException
            or ArgumentException
            or NotSupportedException)
        {
            return null;
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
            or NotSupportedException
            or System.ComponentModel.Win32Exception)
        {
        }
    }

    [GeneratedRegex(
        @"\d+\.\d+(\.\d+)?",
        RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();
}
