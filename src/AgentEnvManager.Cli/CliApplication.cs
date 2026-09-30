using AgentEnvManager.Core.Inspection;
using AgentEnvManager.Core.Scanning;

namespace AgentEnvManager.Cli;

public static class CliApplication
{
    public static async Task<int> RunAsync(
        string[] args,
        CancellationToken cancellationToken = default)
    {
        var request = CliArguments.Parse(args);

        if (request.Action == CliAction.Help)
        {
            InspectionReportRenderer.WriteUsage(Console.Out);
            return 0;
        }

        if (request.Action == CliAction.Unknown)
        {
            Console.Error.WriteLine($"未知命令: {request.UnknownCommand}");
            InspectionReportRenderer.WriteUsage(Console.Error);
            return 2;
        }

        var manager = CreateEnvironmentManager();
        var report = await manager.InspectAsync(cancellationToken);
        InspectionReportRenderer.Write(report, Console.Out);
        return 0;
    }

    private static EnvironmentManager CreateEnvironmentManager()
    {
        return new EnvironmentManager(
            new WindowsEnvironmentProbe(
                new WindowsEnvironmentSnapshotSource()));
    }
}
