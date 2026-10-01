using AgentEnvManager.Core.Agents;

namespace AgentEnvManager.Cli;

internal static class AgentReportRenderer
{
    public static void WriteDiscovery(
        string agentName,
        AgentDiscoveryResult discovery,
        TextWriter output)
    {
        output.WriteLine($"Agent: {agentName}");
        output.WriteLine(
            $"已安装: {(discovery.IsInstalled ? "是" : "否")}");
        output.WriteLine(
            $"Agent 配置环境: {discovery.Home ?? "未发现"}");
        output.WriteLine(
            $"配置来源: {discovery.HomeSource ?? "未知"}");
        output.WriteLine(
            $"可执行文件: {discovery.Executable ?? "未发现"}");
        output.WriteLine(
            $"内置 Codex: {discovery.BundledCodexPath ?? "未发现"}");
        output.WriteLine(
            $"app-server 入口: {discovery.BundledCodexPath ?? "未发现"}");
        output.WriteLine(
            $"兼容 Junction: {FormatJunctions(discovery)}");
        output.WriteLine(
            $"绑定状态: {(discovery.IsBound ? "已绑定" : "未绑定")}");
        output.WriteLine(
            $"绑定文件: {discovery.BindingFilePath ?? "未记录"}");
        output.WriteLine($"结果: {discovery.Message}");
    }

    private static string FormatJunctions(AgentDiscoveryResult discovery)
    {
        return discovery.CompatibilityJunctionPaths is { Count: > 0 }
            ? string.Join("、", discovery.CompatibilityJunctionPaths)
            : "未发现";
    }

    public static void WritePlan(
        AgentBindingPlan plan,
        TextWriter output)
    {
        output.WriteLine("Agent 绑定预览");
        output.WriteLine($"Agent: {plan.AgentName}");
        output.WriteLine(
            $"Agent 配置环境: {plan.ConfigurationDirectory}");
        output.WriteLine($"可执行文件: {plan.Executable}");
        output.WriteLine($"受管入口: {plan.ManagedEntryPath}");
        output.WriteLine(
            $"工具运行时: {plan.RuntimeName} {plan.RuntimeVersion}");
        output.WriteLine($"工作区: {plan.WorkspacePath}");
        output.WriteLine($"运行时命令: {plan.RuntimeCommand}");
        output.WriteLine($"绑定文件: {plan.BindingFilePath}");
        output.WriteLine("影响范围: 仅写入上述 Agent 绑定文件。");
        output.WriteLine("绑定内容:");
        output.WriteLine(plan.BindingContent);
        output.WriteLine();
    }

    public static void WriteBinding(
        AgentBinding binding,
        string? operationId,
        TextWriter output)
    {
        output.WriteLine(
            $"Agent 绑定完成: {binding.AgentName}");
        output.WriteLine($"绑定文件: {binding.BindingFilePath}");
        output.WriteLine($"受管入口: {binding.ManagedEntryPath}");
        output.WriteLine(
            $"工具运行时: {binding.RuntimeName} {binding.RuntimeVersion}");
        output.WriteLine(
            $"健康状态: {(binding.IsHealthy ? "健康" : "未知")}");
        output.WriteLine($"操作 ID: {operationId ?? "未记录"}");
        output.WriteLine($"恢复点: {binding.RecoveryPoint.Id}");
    }

    public static void WriteHealth(
        string agentName,
        AgentHealthCheckResult health,
        TextWriter output)
    {
        output.WriteLine($"Agent 健康检查: {agentName}");
        output.WriteLine(
            $"健康状态: {(health.IsHealthy ? "健康" : "不健康")}");
        output.WriteLine($"结果: {health.Message}");
    }
}
