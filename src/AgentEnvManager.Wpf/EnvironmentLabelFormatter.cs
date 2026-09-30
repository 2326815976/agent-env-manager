using AgentEnvManager.Core.Inspection;

namespace AgentEnvManager.Wpf;

public static class EnvironmentLabelFormatter
{
    public static string FormatKind(EnvironmentAssetKind kind)
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

    public static string FormatHealth(HealthState state)
    {
        return state switch
        {
            HealthState.Unknown => "未知",
            _ => state.ToString()
        };
    }
}
