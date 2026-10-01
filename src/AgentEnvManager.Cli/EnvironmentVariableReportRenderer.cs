using AgentEnvManager.Core.EnvironmentVariables;

namespace AgentEnvManager.Cli;

internal static class EnvironmentVariableReportRenderer
{
    public static void WriteSnapshot(
        EnvironmentVariableEditorSnapshot snapshot,
        TextWriter output,
        bool showValues = false)
    {
        output.WriteLine("环境变量快照");
        output.WriteLine(
            $"用户 PATH（高风险，修改需预览、恢复点与确认）: " +
            $"{snapshot.Path ?? "未设置"}");
        foreach (var entry in snapshot.PathEntries)
        {
            output.WriteLine(entry.IsManaged
                ? $"  [受管] {entry.ManagedEntryPath} ({entry.Name} {entry.Version}) 启用: {(entry.IsEnabled ? "是" : "否")}"
                : $"  [外部/只读] {entry.ManagedEntryPath}");
        }

        foreach (var variable in snapshot.Variables)
        {
            var risk = variable.IsHighRisk ? "（高风险）" : string.Empty;
            output.WriteLine(variable.IsManaged
                ? $"  [受管] {variable.Name}={Format(variable.Value, showValues)}{risk}"
                : $"  [外部/只读] {variable.Name}={Format(variable.Value, showValues)}{risk}");
        }

        output.WriteLine(
            $"机器级变量（{snapshot.MachineScopeDescription}）" +
            (showValues ? string.Empty : "；值默认掩码，使用 --show-secrets 显示"));
        foreach (var variable in snapshot.MachineVariables)
        {
            var risk = variable.IsHighRisk ? "（高风险）" : string.Empty;
            output.WriteLine(
                $"  [只读] {variable.Name}={Format(variable.Value, showValues)}{risk}");
        }
    }

    private static string Format(string? value, bool showValues)
    {
        if (value is null)
        {
            return "<未设置>";
        }

        return showValues ? value : "******";
    }

    public static void WritePreview(
        EnvironmentVariableUpdatePreview preview,
        TextWriter output)
    {
        output.WriteLine("环境变量变更预览");
        foreach (var change in preview.Changes)
        {
            var original =
                preview.OriginalValues.GetValueOrDefault(change.Name)
                ?? "<未设置>";
            output.WriteLine(
                $"  {change.Name}: {original} -> " +
                $"{change.Value ?? "<删除>"}");
        }

        output.WriteLine($"影响范围: {preview.Impact}");
    }

    public static void WriteResult(
        EnvironmentVariableTransactionResult result,
        TextWriter output)
    {
        output.WriteLine("环境变量已应用");
        output.WriteLine($"操作 ID: {result.Operation.Id}");
        output.WriteLine($"恢复点: {result.RecoveryPoint.Id}");
    }
}
