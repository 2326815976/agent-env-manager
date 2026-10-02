using System.Diagnostics;

namespace AgentEnvManager.Core.Migrations;

public sealed class WindowsProcessStartupProbe : ICoordinatedStartupProbe
{
    public Task StartAsync(
        string executablePath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(executablePath)
            || !File.Exists(executablePath))
        {
            throw new FileNotFoundException(
                "启动目标不存在。",
                executablePath);
        }

        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = Path.GetFullPath(executablePath),
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(
                Path.GetFullPath(executablePath))!
        })
            ?? throw new InvalidOperationException(
                $"无法启动 {executablePath}。");
        return Task.CompletedTask;
    }
}
