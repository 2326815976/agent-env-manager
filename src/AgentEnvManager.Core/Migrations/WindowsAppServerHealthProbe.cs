using AgentEnvManager.Core.Agents;

namespace AgentEnvManager.Core.Migrations;

/// 只观测由协调迁移自行启动的 ChatGPT 是否把 app-server 拉起来，
/// 不再通过启动脚本来验证，避免重复启动 ChatGPT。
public sealed class WindowsAppServerHealthProbe(
    IAgentProcessRunner processRunner) : ICoordinatedHealthProbe
{
    private const string ObservationScript = """
        $ErrorActionPreference = 'Stop'
        $appServer = Get-CimInstance Win32_Process -ErrorAction SilentlyContinue |
            Where-Object {
                $_.Name -eq 'codex.exe' -and
                $_.CommandLine -and
                $_.CommandLine -match 'app-server'
            } |
            Select-Object -First 1
        if ($null -eq $appServer) { exit 1 }
        exit 0
        """;

    public async Task<AgentHealthCheckResult> CheckAsync(
        string codexConfigDirectory,
        CancellationToken cancellationToken = default)
    {
        var result = await processRunner.RunAsync(
            "powershell.exe",
            [
                "-NoProfile",
                "-NonInteractive",
                "-Command",
                ObservationScript
            ],
            Directory.Exists(codexConfigDirectory)
                ? codexConfigDirectory
                : Path.GetTempPath(),
            new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase),
            cancellationToken);
        return result.ExitCode == 0
            ? new AgentHealthCheckResult(
                true,
                "迁移后已观测到 codex app-server 进程。")
            : new AgentHealthCheckResult(
                false,
                "迁移后未观测到 codex app-server 进程" +
                $"（exit={result.ExitCode}）: " +
                $"{result.StandardError.Trim()}");
    }
}
