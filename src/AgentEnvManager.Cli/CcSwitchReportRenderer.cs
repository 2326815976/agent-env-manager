using AgentEnvManager.Core.Agents;

namespace AgentEnvManager.Cli;

internal static class CcSwitchReportRenderer
{
    public static void WriteDiscovery(
        CcSwitchDiscovery discovery,
        TextWriter output)
    {
        output.WriteLine("CC Switch 发现结果");
        output.WriteLine(
            $"已安装: {(discovery.IsInstalled ? "是" : "否")}");
        output.WriteLine($"可执行文件: {discovery.Executable ?? "未发现"}");
        output.WriteLine($"版本: {discovery.Version ?? "未知"}");
        output.WriteLine($"配置根: {discovery.ConfigRoot}");
        output.WriteLine($"settings.json: {discovery.SettingsFilePath}");
        output.WriteLine(
            $"codexConfigDir: {discovery.CodexConfigDirectory ?? "未设置"}");
        output.WriteLine(
            "WebView 兼容路径: " +
            (discovery.CompatibilityJunctions.Count == 0
                ? "未发现"
                : string.Join(
                    "、",
                    discovery.CompatibilityJunctions)));
        output.WriteLine($"结果: {discovery.Message}");
    }

    public static void WritePreview(
        CcSwitchBindingPreview preview,
        TextWriter output)
    {
        output.WriteLine("CC Switch 绑定预览");
        output.WriteLine($"settings.json: {preview.SettingsFilePath}");
        output.WriteLine(
            $"将修改字段: {preview.FieldName}: " +
            $"{preview.CurrentValue ?? "未设置"} -> {preview.TargetValue}");
        output.WriteLine($"当前绑定状态: {(preview.IsAlreadyBound ? "已指向目标" : "需要更新")}");
        output.WriteLine(
            "保持不变: " + string.Join("、", preview.PreservedContent));
        output.WriteLine(
            "兼容 Junction: " +
            (preview.CompatibilityJunctions.Count == 0
                ? "未发现"
                : string.Join(
                    "、",
                    preview.CompatibilityJunctions)));
        output.WriteLine($"影响范围: {preview.Impact}");
        output.WriteLine($"操作 ID: {preview.OperationId ?? "未记录"}");
        output.WriteLine($"恢复点: {preview.RecoveryPointId ?? "未记录"}");
    }

    public static void WriteBinding(
        CcSwitchBindingResult result,
        TextWriter output)
    {
        output.WriteLine("CC Switch 绑定完成");
        output.WriteLine(
            $"codexConfigDir: " +
            $"{result.Discovery.CodexConfigDirectory ?? "未设置"}");
        output.WriteLine($"操作 ID: {result.Operation.Id}");
        output.WriteLine($"恢复点: {result.RecoveryPoint.Id}");
    }
}
