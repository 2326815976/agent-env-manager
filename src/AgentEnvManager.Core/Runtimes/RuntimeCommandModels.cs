using AgentEnvManager.Core.Processes;

namespace AgentEnvManager.Core.Runtimes;

public sealed record RuntimeCommandResult(
    int ExitCode,
    string StandardOutput,
    string StandardError);

public interface IRuntimeCommandRunner
{
    Task<RuntimeCommandResult> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string> environment,
        CancellationToken cancellationToken = default);
}

public sealed class SystemRuntimeCommandRunner : IRuntimeCommandRunner
{
    public async Task<RuntimeCommandResult> RunAsync(
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
        return new RuntimeCommandResult(
            result.ExitCode,
            result.StandardOutput,
            result.StandardError);
    }
}
