using System.Text.Json;
using System.Text.Json.Serialization;
using AgentEnvManager.Core.Inspection;
using AgentEnvManager.Core.Scanning;

namespace AgentEnvManager.Cli;

public static class CliApplication
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters =
        {
            new JsonStringEnumConverter()
        }
    };

    public static async Task<int> RunAsync(
        string[] args,
        CancellationToken cancellationToken = default)
    {
        if (args.Length == 0 || args[0] is "inspect" or "inventory")
        {
            var json = args.Contains("--json", StringComparer.OrdinalIgnoreCase);
            return await InspectAsync(json, cancellationToken);
        }

        if (args[0] is "--help" or "-h" or "help")
        {
            Console.WriteLine("用法: agent-env-manager inspect [--json]");
            return 0;
        }

        Console.Error.WriteLine($"未知命令: {args[0]}");
        Console.Error.WriteLine("用法: agent-env-manager inspect [--json]");
        return 2;
    }

    private static async Task<int> InspectAsync(
        bool json,
        CancellationToken cancellationToken)
    {
        var manager = new EnvironmentManager(
            new WindowsEnvironmentProbe(
                new WindowsEnvironmentSnapshotSource()));
        var report = await manager.InspectAsync(cancellationToken);

        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(report, JsonOptions));
            return 0;
        }

        WriteHumanReadableReport(report);
        return 0;
    }

    private static void WriteHumanReadableReport(InspectionReport report)
    {
        Console.WriteLine("Agent 环境体检");
        Console.WriteLine($"生成时间: {report.GeneratedAtUtc:O}");
        Console.WriteLine(
            $"仅观测资产: {report.Environments.Count(environment => environment.ManagementState == ManagementState.Observed)}");
        Console.WriteLine();

        foreach (var environment in report.Environments)
        {
            var version = string.IsNullOrWhiteSpace(environment.Asset.Version)
                ? string.Empty
                : $" {environment.Asset.Version}";
            var system = environment.Asset.IsSystemComponent ? " [系统组件]" : string.Empty;
            Console.WriteLine(
                $"- [{FormatKind(environment.Asset.Kind)}] {environment.Asset.Name}{version}{system}");
            Console.WriteLine($"  位置: {environment.Asset.Location}");
            Console.WriteLine($"  来源: {environment.Asset.Source}");
        }

        Console.WriteLine();
        Console.WriteLine($"PATH 冲突: {report.PathConflicts.Count}");
        foreach (var conflict in report.PathConflicts)
        {
            Console.WriteLine(
                $"- {conflict.Path} ({conflict.Occurrences} 次; {string.Join(", ", conflict.Scopes)})");
        }
    }

    private static string FormatKind(EnvironmentAssetKind kind)
    {
        return kind switch
        {
            EnvironmentAssetKind.ToolRuntime => "工具运行时",
            EnvironmentAssetKind.Shell => "Shell",
            EnvironmentAssetKind.AgentConfiguration => "Agent 配置环境",
            EnvironmentAssetKind.PackageManager => "包管理器",
            _ => kind.ToString()
        };
    }
}
