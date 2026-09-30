using System.Diagnostics;
using AgentEnvManager.Core.Adoption;

namespace AgentEnvManager.Core.Activation;

public sealed class ProcessRuntimeHealthCheck : IRuntimeHealthCheck
{
    public async Task<RuntimeHealthCheckResult> CheckAsync(
        EnvironmentManifest manifest,
        string activationPath,
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
            WorkingDirectory = activationPath
        };
        startInfo.ArgumentList.Add("/d");
        startInfo.ArgumentList.Add("/s");
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add(command);
        var currentPath = startInfo.Environment["PATH"] ?? string.Empty;
        startInfo.Environment["PATH"] =
            $"{activationPath};{currentPath}";

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

        return process.ExitCode == 0
            ? new RuntimeHealthCheckResult(
                true,
                string.IsNullOrWhiteSpace(detail) ? "健康检查通过。" : detail)
            : new RuntimeHealthCheckResult(
                false,
                string.IsNullOrWhiteSpace(detail)
                    ? $"健康检查退出码 {process.ExitCode}。"
                    : detail);
    }

    private static string? GetHealthCommand(EnvironmentManifest manifest)
    {
        return manifest.Name switch
        {
            "Node.js" => "node --version",
            "Python" => "python --version",
            "Git" => "git --version",
            "npm" => "npm --version",
            "PowerShell 7" =>
                "pwsh -NoLogo -NoProfile -NonInteractive -Command \"$PSVersionTable.PSVersion.ToString()\"",
            _ => null
        };
    }
}
