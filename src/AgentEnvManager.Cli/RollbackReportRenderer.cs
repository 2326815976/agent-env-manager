using AgentEnvManager.Core.Operations;

namespace AgentEnvManager.Cli;

internal static class RollbackReportRenderer
{
    public static void WritePreview(
        OperationRollbackPlan plan,
        TextWriter output)
    {
        output.WriteLine("操作回滚预览");
        output.WriteLine($"操作 ID: {plan.OperationId}");
        output.WriteLine($"目标: {plan.Target}");
        output.WriteLine($"影响范围: {plan.Impact}");
        output.WriteLine($"恢复点: {plan.RecoveryPointId}");
        output.WriteLine($"预期结果: {plan.ExpectedResult}");
    }
}
