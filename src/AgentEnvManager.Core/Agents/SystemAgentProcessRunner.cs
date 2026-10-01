using AgentEnvManager.Core.Processes;

namespace AgentEnvManager.Core.Agents;

public sealed class SystemAgentProcessRunner : IAgentProcessRunner
{
    public async Task<AgentProcessResult> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string> environment,
        CancellationToken cancellationToken = default)
    {
        var result = await SystemProcessRunner.RunAsync(
            executable,
            arguments,
            workingDirectory,
            environment,
            cancellationToken);
        return new AgentProcessResult(
            result.ExitCode,
            result.StandardOutput,
            result.StandardError);
    }
}
