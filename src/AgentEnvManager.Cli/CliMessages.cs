namespace AgentEnvManager.Cli;

using AgentEnvManager.Core.Operations;

internal static class CliMessages
{
    public static void WriteUsage(TextWriter output)
    {
        output.WriteLine("用法:");
        output.WriteLine("  agent-env-manager inspect");
        output.WriteLine("  agent-env-manager preview <fingerprint>");
        output.WriteLine("  agent-env-manager adopt <fingerprint>");
        output.WriteLine("  agent-env-manager rebuild-index");
        output.WriteLine("  agent-env-manager rollback <operation-id>");
    }

    public static void WriteIndexRebuild(int count, TextWriter output)
    {
        output.WriteLine($"索引重建完成: {count} 个已纳管环境");
    }

    public static void WriteRollback(
        OperationRecord operation,
        TextWriter output)
    {
        output.WriteLine($"操作已回滚: {operation.Id}");
        output.WriteLine($"状态: {operation.State}");
        output.WriteLine($"原因: {operation.FailureReason}");
    }
}
