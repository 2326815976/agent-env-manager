using System.Diagnostics;

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
        var startInfo = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach (var variable in environment)
        {
            startInfo.Environment[variable.Key] = variable.Value;
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException(
                $"无法启动 Agent 进程: {executable}");
        var outputTask = process.StandardOutput.ReadToEndAsync(
            cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(
            cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return new AgentProcessResult(
            process.ExitCode,
            await outputTask,
            await errorTask);
    }
}
