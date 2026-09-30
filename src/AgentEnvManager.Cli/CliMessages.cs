namespace AgentEnvManager.Cli;

internal static class CliMessages
{
    public static void WriteUsage(TextWriter output)
    {
        output.WriteLine("用法:");
        output.WriteLine("  agent-env-manager inspect");
        output.WriteLine("  agent-env-manager preview <fingerprint>");
        output.WriteLine("  agent-env-manager adopt <fingerprint>");
        output.WriteLine("  agent-env-manager rebuild-index");
    }

    public static void WriteIndexRebuild(int count, TextWriter output)
    {
        output.WriteLine($"索引重建完成: {count} 个已纳管环境");
    }
}
