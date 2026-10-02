using AgentEnvManager.Core.Agents;
using AgentEnvManager.Core.Migrations;

namespace AgentEnvManager.Core.Tests.Migration;

public sealed class WindowsAppServerHealthProbeTests
{
    [Fact]
    public async Task CheckAsync_observes_app_server_without_reusing_launcher()
    {
        var runner = new RecordingAgentProcessRunner(
            new AgentProcessResult(0, string.Empty, string.Empty));
        var probe = new WindowsAppServerHealthProbe(runner);

        var result = await probe.CheckAsync(
            Path.GetTempPath());

        Assert.True(result.IsHealthy);
        var arguments = string.Join(' ', runner.LastArguments);
        Assert.Contains("app-server", arguments, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "launch.ps1",
            arguments,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CheckAsync_reports_unhealthy_when_app_server_missing()
    {
        var runner = new RecordingAgentProcessRunner(
            new AgentProcessResult(1, string.Empty, "not found"));
        var probe = new WindowsAppServerHealthProbe(runner);

        var result = await probe.CheckAsync(Path.GetTempPath());

        Assert.False(result.IsHealthy);
        Assert.Contains(
            "未观测到",
            result.Message,
            StringComparison.Ordinal);
    }

    private sealed class RecordingAgentProcessRunner(AgentProcessResult result)
        : IAgentProcessRunner
    {
        public IReadOnlyList<string> LastArguments { get; private set; } = [];

        public Task<AgentProcessResult> RunAsync(
            string executable,
            IReadOnlyList<string> arguments,
            string workingDirectory,
            IReadOnlyDictionary<string, string> environment,
            CancellationToken cancellationToken = default)
        {
            LastArguments = arguments.ToArray();
            return Task.FromResult(result);
        }
    }
}
