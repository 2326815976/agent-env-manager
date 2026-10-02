using AgentEnvManager.Core.Migrations;

namespace AgentEnvManager.Cli;

internal static class CoordinatedMigrationReportRenderer
{
    public static void WritePreview(
        CoordinatedMigrationPreview preview,
        TextWriter output)
    {
        output.WriteLine("协同迁移预览");
        foreach (var target in preview.Targets)
        {
            output.WriteLine(
                $"  {target.DisplayName}: {target.SourcePath} -> " +
                $"{target.DestinationPath}");
        }

        output.WriteLine(
            "需先停止进程: " +
            string.Join(
                "、",
                preview.ProcessesToStop.Select(item => item.DisplayName)));
        foreach (var rewrite in preview.PlannedRewrites)
        {
            output.WriteLine($"  计划重写: {rewrite}");
        }

        foreach (var step in preview.StartupOrder)
        {
            output.WriteLine($"  启动顺序: {step}");
        }

        foreach (var blocker in preview.Blockers)
        {
            output.WriteLine(
                $"  阻断项[{blocker.Code}]: {blocker.Message}");
        }

        output.WriteLine($"影响范围: {preview.Impact}");
        output.WriteLine($"操作 ID: {preview.OperationId ?? "未记录"}");
        output.WriteLine(
            $"恢复点: {preview.RecoveryPointId ?? "执行时创建"}");
    }

    public static void WriteResult(
        CoordinatedMigrationResult result,
        TextWriter output)
    {
        output.WriteLine("协同迁移完成");
        output.WriteLine($"操作 ID: {result.Operation.Id}");
        output.WriteLine(
            $"已迁移: {string.Join("、", result.MigratedPaths)}");
        output.WriteLine(
            $"已重写: {string.Join("、", result.RewrittenPaths)}");
        if (result.RewiredJunctions.Count > 0)
        {
            output.WriteLine(
                $"已重接 Junction: " +
                string.Join("、", result.RewiredJunctions));
        }

        output.WriteLine(
            result.QuarantinedSourceIds.Count == 0
                ? "源目录: 仍保留在原位置"
                : $"源目录已隔离: " +
                    string.Join("、", result.QuarantinedSourceIds));
    }
}
