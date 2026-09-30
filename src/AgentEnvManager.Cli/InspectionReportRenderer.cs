using AgentEnvManager.Core.Inspection;

namespace AgentEnvManager.Cli;

internal static class InspectionReportRenderer
{
    public static void Write(
        InspectionReport report,
        TextWriter output)
    {
        output.WriteLine("Agent 环境体检");
        output.WriteLine($"生成时间: {report.GeneratedAtUtc:O}");
        output.WriteLine(
            $"仅观测资产: {report.Environments.Count(environment => environment.ManagementState == ManagementState.Observed)}");
        output.WriteLine();

        foreach (var environment in report.Environments)
        {
            var version = string.IsNullOrWhiteSpace(environment.Asset.Version)
                ? string.Empty
                : $" {environment.Asset.Version}";
            var system = environment.Asset.IsSystemComponent ? " [系统组件]" : string.Empty;
            var state = environment.ManagementState == ManagementState.Managed
                ? " [已纳管]"
                : string.Empty;
            output.WriteLine(
                $"- [{FormatKind(environment.Asset.Kind)}] {environment.Asset.Name}{version}{system}{state}");
            output.WriteLine($"  指纹: {environment.Fingerprint.Value}");
            output.WriteLine($"  位置: {environment.Asset.Location}");
            output.WriteLine($"  来源: {environment.Asset.Source.Description}");
        }

        output.WriteLine();
        output.WriteLine($"PATH 冲突: {report.PathConflicts.Count}");
        foreach (var conflict in report.PathConflicts)
        {
            output.WriteLine(
                $"- {conflict.Path} ({conflict.Occurrences} 次; {string.Join(", ", conflict.Scopes)})");
        }

        output.WriteLine();
        output.WriteLine($"PATH 命令遮蔽: {report.CommandPathConflicts.Count}");
        foreach (var conflict in report.CommandPathConflicts)
        {
            output.WriteLine($"- {conflict.Name}");
            foreach (var candidate in conflict.Candidates)
            {
                var state = candidate.Effective ? "生效" : "被遮蔽";
                var scopes = candidate.Scopes.Count == 0
                    ? "未知作用域"
                    : string.Join(", ", candidate.Scopes);
                output.WriteLine(
                    $"  {candidate.Order + 1}. [{state}] {candidate.Path} ({scopes})");
            }
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
