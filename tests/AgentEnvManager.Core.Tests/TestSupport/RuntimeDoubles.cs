using AgentEnvManager.Core.Activation;
using AgentEnvManager.Core.Adoption;
using AgentEnvManager.Core.Runtimes;

namespace AgentEnvManager.Core.Tests.TestSupport;

internal sealed record RuntimeCommandInvocation(
    string Executable,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory);

internal sealed class RecordingRuntimeCommandRunner
    : IRuntimeCommandRunner
{
    private readonly Func<
        RuntimeCommandInvocation,
        Task<RuntimeCommandResult>> _respond;

    public RecordingRuntimeCommandRunner(
        Func<RuntimeCommandInvocation, RuntimeCommandResult> respond)
    {
        _respond = invocation => Task.FromResult(respond(invocation));
    }

    public RecordingRuntimeCommandRunner(
        Func<RuntimeCommandInvocation, Task<RuntimeCommandResult>> respond)
    {
        _respond = respond;
    }

    public List<RuntimeCommandInvocation> Invocations { get; } = [];

    public Task<RuntimeCommandResult> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string> environment,
        CancellationToken cancellationToken = default)
    {
        var invocation = new RuntimeCommandInvocation(
            executable,
            arguments,
            workingDirectory);
        Invocations.Add(invocation);
        return _respond(invocation);
    }
}

internal sealed class QueuedRuntimeHealthCheck(
    params bool[] results)
    : IRuntimeHealthCheck
{
    private readonly Queue<bool> _results = new(results);

    public Task<RuntimeHealthCheckResult> CheckAsync(
        EnvironmentManifest manifest,
        string managedEntryPath,
        CancellationToken cancellationToken = default)
    {
        var healthy = _results.Count > 0
            ? _results.Dequeue()
            : false;
        return Task.FromResult(new RuntimeHealthCheckResult(
            healthy,
            healthy ? "健康" : "目标版本健康检查失败"));
    }
}
