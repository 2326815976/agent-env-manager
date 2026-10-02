using System.Diagnostics;

namespace AgentEnvManager.Core.Migrations;

public sealed class WindowsProcessControlProbe : IProcessControlProbe
{
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
}
