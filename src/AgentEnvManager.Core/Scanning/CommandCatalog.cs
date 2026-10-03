using AgentEnvManager.Core.Inspection;

namespace AgentEnvManager.Core.Scanning;

internal sealed record CommandDefinition(
    string Command,
    EnvironmentAssetKind Kind,
    string Name,
    bool IsCanonicalSystemShell,
    string AppPathFileName);

internal static class CommandCatalog
{
    public static readonly IReadOnlyList<CommandDefinition> Commands =
    [
        new("python", EnvironmentAssetKind.ToolRuntime, "Python", false, "python.exe"),
        new("py", EnvironmentAssetKind.ToolRuntime, "Python Launcher", false, "py.exe"),
        new("node", EnvironmentAssetKind.ToolRuntime, "Node.js", false, "node.exe"),
        new("git", EnvironmentAssetKind.ToolRuntime, "Git", false, "git.exe"),
        new("pwsh", EnvironmentAssetKind.Shell, "PowerShell 7", false, "pwsh.exe"),
        new(
            "powershell",
            EnvironmentAssetKind.Shell,
            "Windows PowerShell",
            true,
            "powershell.exe"),
        new("cmd", EnvironmentAssetKind.Shell, "cmd", true, "cmd.exe"),
        new("npm", EnvironmentAssetKind.PackageManager, "npm", false, "npm.cmd"),
        new("pnpm", EnvironmentAssetKind.PackageManager, "pnpm", false, "pnpm.cmd"),
        new("winget", EnvironmentAssetKind.PackageManager, "winget", false, "winget.exe"),
        new("uv", EnvironmentAssetKind.PackageManager, "uv", false, "uv.exe"),
        new("conda", EnvironmentAssetKind.PackageManager, "Conda", false, "conda.exe")
        ,
        new("rg", EnvironmentAssetKind.ToolRuntime, "ripgrep", false, "rg.exe"),
        new("gh", EnvironmentAssetKind.ToolRuntime, "GitHub CLI", false, "gh.exe"),
        new("bash", EnvironmentAssetKind.ToolRuntime, "Git Bash", false, "bash.exe"),
        new("codex", EnvironmentAssetKind.ToolRuntime, "Codex CLI", false, "codex.cmd")
    ];

    public static CommandDefinition FindByCommand(string command)
    {
        return Commands.Single(definition => definition.Command == command);
    }
}

internal static class SystemExecutableClassifier
{
    public static bool IsProtectedSystemPath(
        string path,
        IWindowsEnvironmentAccessor accessor)
    {
        var normalized = WindowsPathNormalizer.NormalizeForComparison(path);
        var windowsDirectory = accessor.GetFolderPath(
            Environment.SpecialFolder.Windows);
        if (IsEqualOrDescendant(normalized, windowsDirectory))
        {
            return true;
        }

        var windowsApps = Path.Combine(
            accessor.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "WindowsApps");
        return IsEqualOrDescendant(normalized, windowsApps);
    }

    private static bool IsEqualOrDescendant(string path, string root)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            return false;
        }

        var normalizedRoot = WindowsPathNormalizer
            .NormalizeForComparison(root)
            .TrimEnd('\\', '/');
        return string.Equals(
                path,
                normalizedRoot,
                StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(
                normalizedRoot + "\\",
                StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsCanonicalSystemShell(
        CommandDefinition command,
        string path,
        IWindowsEnvironmentAccessor accessor)
    {
        if (!command.IsCanonicalSystemShell)
        {
            return false;
        }

        var expectedPath = SystemShellPaths.GetCanonicalPath(command, accessor);

        return string.Equals(
            WindowsPathNormalizer.NormalizeForComparison(path),
            WindowsPathNormalizer.NormalizeForComparison(expectedPath),
            StringComparison.OrdinalIgnoreCase);
    }
}

internal static class SystemShellPaths
{
    public static string GetCanonicalPath(
        CommandDefinition command,
        IWindowsEnvironmentAccessor accessor)
    {
        return command.Command switch
        {
            "cmd" => Path.Combine(
                accessor.GetFolderPath(Environment.SpecialFolder.Windows),
                "System32",
                "cmd.exe"),
            "powershell" => Path.Combine(
                accessor.GetFolderPath(Environment.SpecialFolder.Windows),
                "System32",
                "WindowsPowerShell",
                "v1.0",
                "powershell.exe"),
            _ => string.Empty
        };
    }
}
