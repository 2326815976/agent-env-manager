using AgentEnvManager.Core.Migrations;
using AgentEnvManager.Core.Operations;

namespace AgentEnvManager.Cli;

internal static class MigrationReportRenderer
{
    public static void WritePreview(
        MigrationPreview preview,
        TextWriter output)
    {
        output.WriteLine("迁移预览");
        output.WriteLine(
            $"已纳管环境: {preview.Target.Name} " +
            $"{preview.Target.Version ?? "未知"}");
        output.WriteLine(
            $"环境指纹: {preview.TargetFingerprint.Value}");
        output.WriteLine($"源路径: {preview.SourcePath}");
        output.WriteLine($"目标路径: {preview.DestinationPath}");
        output.WriteLine($"迁移策略: {preview.Strategy}");
        output.WriteLine(
            $"文件数量: {preview.Statistics.FileCount}");
        output.WriteLine(
            $"数据大小: {preview.Statistics.TotalBytes} 字节");
        output.WriteLine(
            $"稳定激活路径: {preview.StableActivationPath}");
        output.WriteLine(
            $"迁移前激活目标: {preview.PreviousActivationTarget}");
        output.WriteLine($"影响范围: {preview.Impact}");
        output.WriteLine($"预期结果: {preview.ExpectedResult}");
        output.WriteLine(
            $"操作 ID: {preview.OperationId ?? "未记录"}");
        output.WriteLine(
            $"恢复点: {preview.RecoveryPointId ?? "未记录"}");
    }

    public static void WriteResult(
        OperationRecord operation,
        TextWriter output)
    {
        output.WriteLine($"迁移完成: {operation.Id}");
        output.WriteLine($"状态: {operation.State}");
        output.WriteLine($"目标: {operation.Target}");
        output.WriteLine($"影响范围: {operation.Impact}");
        output.WriteLine(
            $"恢复点: {operation.RecoveryPointId ?? "未记录"}");
    }
}
