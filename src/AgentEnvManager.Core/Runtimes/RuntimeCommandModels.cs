using System.Diagnostics;

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
                $"无法启动运行时命令: {executable}");
        var outputTask = process.StandardOutput.ReadToEndAsync(
            cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(
            cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return new RuntimeCommandResult(
            process.ExitCode,
            await outputTask,
            await errorTask);
    }
}
