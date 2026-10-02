using AgentEnvManager.Core.Agents;

namespace AgentEnvManager.Core.Migrations;

public sealed class WindowsLauncherScriptHealthProbe(
    IAgentProcessRunner processRunner) : ICoordinatedHealthProbe
{
    private const string LauncherFileName =
        "agent-env-manager.launch.ps1";

    public async Task<AgentHealthCheckResult> CheckAsync(
        string codexConfigDirectory,
        CancellationToken cancellationToken = default)
    {
        var launcherPath = Path.Combine(
            codexConfigDirectory,
            LauncherFileName);
        if (!File.Exists(launcherPath))
        {
            return new AgentHealthCheckResult(
                false,
                $"未找到迁移后的启动脚本: {launcherPath}");
        }

        var result = await processRunner.RunAsync(
            "powershell.exe",
            [
                "-NoProfile",
                "-ExecutionPolicy",
                "Bypass",
                "-File",
                launcherPath
            ],
            codexConfigDirectory,
            new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase),
            cancellationToken);
        return result.ExitCode == 0
            ? new AgentHealthCheckResult(
                true,
                "迁移后的启动链已确认 app-server 就绪。")
            : new AgentHealthCheckResult(
                false,
                $"启动链健康检查失败: exit={result.ExitCode}; " +
                $"stderr={result.StandardError.Trim()}");
    }
}
