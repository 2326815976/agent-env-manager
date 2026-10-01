namespace AgentEnvManager.Cli;

using AgentEnvManager.Core.Operations;
using AgentEnvManager.Core.Diagnostics;

internal static class CliMessages
{
    public static void WriteUsage(TextWriter output)
    {
        output.WriteLine("用法:");
        output.WriteLine("  agent-env-manager inspect");
        output.WriteLine("  agent-env-manager preview <fingerprint>");
        output.WriteLine(
            "  agent-env-manager adopt <fingerprint> --confirm");
        output.WriteLine("  agent-env-manager rebuild-index");
        output.WriteLine("  agent-env-manager runtimes");
        output.WriteLine(
            "  agent-env-manager runtime-install-preview <provider> <version> [--mirror <url>] [--install-dir <path>]");
        output.WriteLine(
            "  agent-env-manager runtime-install <provider> <version> [--mirror <url>] [--install-dir <path>] --confirm");
        output.WriteLine(
            "  agent-env-manager runtime-import <provider> <version> <artifact-path> --confirm");
        output.WriteLine(
            "  agent-env-manager migrate-preview <fingerprint> <destination>");
        output.WriteLine(
            "  agent-env-manager migrate <fingerprint> <destination> --confirm");
        output.WriteLine(
            "  agent-env-manager rollback-preview <operation-id>");
        output.WriteLine(
            "  agent-env-manager rollback <operation-id> --confirm");
        output.WriteLine(
            "  agent-env-manager agent-discover <agent> [--config-dir <path>] [--executable <path>]");
        output.WriteLine(
            "  ChatGPT 发现仍需显式提供 --executable。");
        output.WriteLine(
            "  agent-env-manager agent-bind-preview <agent> --config-dir <path> --executable <path> --managed-entry <path> --runtime <name> --runtime-version <version> [--workspace <path>] [--runtime-command <command>]");
        output.WriteLine(
            "  agent-env-manager agent-bind <agent> <binding-options> --confirm");
        output.WriteLine(
            "  agent-env-manager agent-health <agent> <binding-options>");
        output.WriteLine("  agent-env-manager diagnostics-preview");
        output.WriteLine("  agent-env-manager diagnostics-export <local-zip-path>");
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

    public static void WriteDiagnosticPreview(
        DiagnosticPackagePreview preview,
        TextWriter output)
    {
        output.WriteLine(preview.Impact);
        output.WriteLine($"遥测: {(preview.TelemetryEnabled ? "启用" : "关闭")}");
        output.WriteLine(
            $"自动上传: {(preview.AllowsAutomaticUpload ? "允许" : "禁止")}");
        foreach (var entry in preview.Entries)
        {
            output.WriteLine();
            output.WriteLine($"===== {entry.Name} =====");
            output.WriteLine(entry.Description);
            output.WriteLine(entry.Content);
        }
    }

    public static void WriteDiagnosticExport(
        DiagnosticPackageResult result,
        TextWriter output)
    {
        output.WriteLine($"诊断包已导出: {result.DestinationPath}");
        output.WriteLine($"条目数量: {result.EntryCount}");
        output.WriteLine($"操作记录: {result.OperationId}");
    }
}
