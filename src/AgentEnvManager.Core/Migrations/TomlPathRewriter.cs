namespace AgentEnvManager.Core.Migrations;

internal static class TomlPathRewriter
{
    public static string RewritePathPrefix(
        string content,
        string sourceRoot,
        string destinationRoot)
    {
        if (string.IsNullOrWhiteSpace(content)
            || string.IsNullOrWhiteSpace(sourceRoot)
            || string.IsNullOrWhiteSpace(destinationRoot))
        {
            return content;
        }

        var source = sourceRoot.TrimEnd('\\', '/');
        var destination = destinationRoot.TrimEnd('\\', '/');
        // 先处理 TOML 基本字符串中的转义反斜杠形态，再处理原样形态。
        var result = content.Replace(
            source.Replace("\\", "\\\\"),
            destination.Replace("\\", "\\\\"),
            StringComparison.OrdinalIgnoreCase);
        result = result.Replace(
            source,
            destination,
            StringComparison.OrdinalIgnoreCase);
        return result.Replace(
            source.Replace('\\', '/'),
            destination.Replace('\\', '/'),
            StringComparison.OrdinalIgnoreCase);
    }
}
