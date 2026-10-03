using System.Text.Json;

namespace AgentEnvManager.Core.Scanning;

internal static class PackageManagerVersionReader
{
    public static string? TryReadVersion(
        string command,
        string executablePath,
        IWindowsEnvironmentAccessor accessor)
    {
        if (!IsNodePackageManager(command))
        {
            return null;
        }

        var directory = Path.GetDirectoryName(executablePath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return null;
        }

        // Node 自带布局：<node>\node_modules\<tool>\package.json
        var manifestPath = Path.Combine(
            directory,
            "node_modules",
            command,
            "package.json");
        var content = accessor.ReadTextFile(manifestPath);
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(content);
            return document.RootElement.TryGetProperty(
                    "version",
                    out var version)
                && version.ValueKind == JsonValueKind.String
                    ? version.GetString()
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsNodePackageManager(string command)
    {
        return command is "npm" or "pnpm" or "npx";
    }
}

internal static class InstallRootHeuristics
{
    private static readonly HashSet<string> LayoutDirectories =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "Scripts",
            "Library",
            "bin",
            "cmd",
            "usr",
            "current"
        };

    // 同一个安装目录下的多个入口（例如 conda.exe 与 Library\bin\conda.bat）
    // 归一到安装根，用于去重。
    public static string GetInstallRoot(string executablePath)
    {
        var directory = Path.GetDirectoryName(
            Path.GetFullPath(executablePath));
        while (!string.IsNullOrWhiteSpace(directory))
        {
            var name = Path.GetFileName(directory);
            if (!LayoutDirectories.Contains(name))
            {
                return directory;
            }

            directory = Path.GetDirectoryName(directory);
        }

        return Path.GetDirectoryName(Path.GetFullPath(executablePath))
            ?? Path.GetFullPath(executablePath);
    }
}
