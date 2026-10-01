using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Text;
using AgentEnvManager.Core.Adoption;

namespace AgentEnvManager.Core.Activation;

public sealed class ProcessRuntimeHealthCheck : IRuntimeHealthCheck
{
    private static readonly string PowerShellVersionCommand =
        "pwsh -NoLogo -NoProfile -NonInteractive -EncodedCommand " +
        Convert.ToBase64String(
            Encoding.Unicode.GetBytes(
                "$PSVersionTable.PSVersion.ToString()"));

    public async Task<RuntimeHealthCheckResult> CheckAsync(
        EnvironmentManifest manifest,
        string managedEntryPath,
        CancellationToken cancellationToken = default)
    {
        var command = GetHealthCommand(manifest);
        if (command is null)
        {
            return new RuntimeHealthCheckResult(
                false,
                $"未定义 {manifest.Name} 的健康检查命令。");
        }

        var startInfo = new ProcessStartInfo(
            Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = managedEntryPath
        };
        startInfo.ArgumentList.Add("/d");
        startInfo.ArgumentList.Add("/s");
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add(command);
        startInfo.Environment["PATH"] = managedEntryPath;

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            return new RuntimeHealthCheckResult(
                false,
                "无法启动健康检查子进程。");
        }

        var outputTask = process.StandardOutput.ReadToEndAsync(
            cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(
            cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var output = (await outputTask).Trim();
        var error = (await errorTask).Trim();
        var detail = string.IsNullOrWhiteSpace(output) ? error : output;

        if (process.ExitCode != 0)
        {
            return new RuntimeHealthCheckResult(
                false,
                string.IsNullOrWhiteSpace(detail)
                    ? $"健康检查退出码 {process.ExitCode}。"
                    : detail);
        }

        if (!string.IsNullOrWhiteSpace(manifest.Version)
            && !VersionMatches(detail, manifest.Version))
        {
            return new RuntimeHealthCheckResult(
                false,
                $"健康检查输出未包含目标版本 {manifest.Version}: {detail}");
        }

        return new RuntimeHealthCheckResult(
            true,
            string.IsNullOrWhiteSpace(detail) ? "健康检查通过。" : detail);
    }

    private static string? GetHealthCommand(EnvironmentManifest manifest)
    {
        return manifest.Name switch
        {
            "Node.js" => "node --version",
            "Python" => "python --version",
            "Git" => "git --version",
            "npm" => "npm --version",
            "PowerShell 7" => PowerShellVersionCommand,
            _ => null
        };
    }

    private static bool VersionMatches(string output, string version)
    {
        return Regex.IsMatch(
            output,
            $@"(?<![A-Za-z0-9_.-])v?{Regex.Escape(version)}(?:\.windows\.\d+)?(?![A-Za-z0-9_.-])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
