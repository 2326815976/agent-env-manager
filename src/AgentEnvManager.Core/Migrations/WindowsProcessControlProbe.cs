using System.Diagnostics;

namespace AgentEnvManager.Core.Migrations;

public sealed class WindowsProcessControlProbe : IProcessControlProbe
{
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(30);

    public Task<IReadOnlyList<string>> FindRunningProcessesAsync(
        IReadOnlyList<string> processNames,
        CancellationToken cancellationToken = default)
    {
        var running = new List<string>();
        foreach (var name in processNames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Process[] processes;
            try
            {
                processes = Process.GetProcessesByName(
                    Path.GetFileNameWithoutExtension(name));
            }
            catch (Exception exception) when (
                exception is InvalidOperationException
                or PlatformNotSupportedException)
            {
                continue;
            }

            try
            {
                if (processes.Length > 0)
                {
                    running.Add(name);
                }
            }
            finally
            {
                foreach (var process in processes)
                {
                    process.Dispose();
                }
            }
        }

        return Task.FromResult<IReadOnlyList<string>>(
            running
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }

    public async Task StopProcessesAsync(
        IReadOnlyList<string> processNames,
        CancellationToken cancellationToken = default)
    {
        foreach (var name in processNames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var processName = Path.GetFileNameWithoutExtension(name);
            foreach (var process in Process.GetProcessesByName(processName))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (Exception exception) when (
                    exception is InvalidOperationException
                    or NotSupportedException
                    or System.ComponentModel.Win32Exception)
                {
                    continue;
                }
                finally
                {
                    process.Dispose();
                }
            }
        }

        var deadline = DateTimeOffset.UtcNow + StopTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            var remaining = await FindRunningProcessesAsync(
                processNames,
                cancellationToken);
            if (remaining.Count == 0)
            {
                return;
            }

            await Task.Delay(200, cancellationToken);
        }

        var stillRunning = await FindRunningProcessesAsync(
            processNames,
            cancellationToken);
        if (stillRunning.Count > 0)
        {
            throw new InvalidOperationException(
                $"无法停止相关进程: {string.Join("、", stillRunning)}。");
        }
    }
}
