using AgentEnvManager.Core.Runtimes;

namespace AgentEnvManager.Core.Tests.Runtimes;

public sealed class RuntimeCommandRunnerTests
{
    [Fact]
    public async Task RunAsync_captures_exit_code_and_output()
    {
        var runner = new SystemRuntimeCommandRunner();

        var result = await runner.RunAsync(
            "cmd.exe",
            ["/d", "/s", "/c", "echo ok"],
            Environment.CurrentDirectory,
            new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("ok", result.StandardOutput);
    }
}
