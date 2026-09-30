using AgentEnvManager.Core.Inspection;

namespace AgentEnvManager.Core.Scanning;

internal static class KnownInstallationCollector
{
    public static IReadOnlyList<ExecutableCandidate> Collect(
        IWindowsEnvironmentAccessor accessor)
    {
        var candidates = new List<ExecutableCandidate>();
        var programFiles = accessor.GetFolderPath(
            Environment.SpecialFolder.ProgramFiles);
        var systemRoot = accessor.GetFolderPath(
            Environment.SpecialFolder.Windows);
        var localAppData = accessor.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        var roamingAppData = accessor.GetFolderPath(
            Environment.SpecialFolder.ApplicationData);

        AddKnownExecutable(
            accessor,
            candidates,
            SystemShellPaths.GetCanonicalPath(
                CommandCatalog.FindByCommand("cmd"),
                accessor),
            "cmd",
            IsSystemComponent: true,
            DiscoverySourceInfo.SystemPath);
        AddKnownExecutable(
            accessor,
            candidates,
            SystemShellPaths.GetCanonicalPath(
                CommandCatalog.FindByCommand("powershell"),
                accessor),
            "powershell",
            IsSystemComponent: true,
            DiscoverySourceInfo.SystemPath);
        AddKnownExecutable(
            accessor,
            candidates,
            Path.Combine(programFiles, "PowerShell", "7", "pwsh.exe"),
            "pwsh",
            IsSystemComponent: false,
            DiscoverySourceInfo.KnownInstallation);
        AddKnownExecutable(
            accessor,
            candidates,
            Path.Combine(programFiles, "Git", "cmd", "git.exe"),
            "git",
            IsSystemComponent: false,
            DiscoverySourceInfo.KnownInstallation);
        AddKnownExecutable(
            accessor,
            candidates,
            Path.Combine(programFiles, "nodejs", "node.exe"),
            "node",
            IsSystemComponent: false,
            DiscoverySourceInfo.KnownInstallation);

        AddPythonInstallations(
            accessor,
            candidates,
            Path.Combine(programFiles, "Python"),
            DiscoverySourceInfo.KnownInstallation);
        AddPythonInstallations(
            accessor,
            candidates,
            Path.Combine(localAppData, "Programs", "Python"),
            DiscoverySourceInfo.KnownInstallation);
        AddPythonInstallations(
            accessor,
            candidates,
            Path.Combine(roamingAppData, "uv", "python"),
            DiscoverySourceInfo.UvRuntime,
            searchPattern: "cpython-*");

        return candidates;
    }

    private static void AddPythonInstallations(
        IWindowsEnvironmentAccessor accessor,
        ICollection<ExecutableCandidate> candidates,
        string root,
        DiscoverySourceInfo source,
        string searchPattern = "Python*")
    {
        if (!accessor.DirectoryExists(root))
        {
            return;
        }

        foreach (var directory in accessor.EnumerateDirectories(root, searchPattern))
        {
            AddKnownExecutable(
                accessor,
                candidates,
                Path.Combine(directory, "python.exe"),
                "python",
                IsSystemComponent: false,
                source);
        }
    }

    private static void AddKnownExecutable(
        IWindowsEnvironmentAccessor accessor,
        ICollection<ExecutableCandidate> candidates,
        string path,
        string commandName,
        bool IsSystemComponent,
        DiscoverySourceInfo source)
    {
        if (!accessor.FileExists(path))
        {
            return;
        }

        var command = CommandCatalog.FindByCommand(commandName);
        candidates.Add(new ExecutableCandidate(
            command.Kind,
            command.Name,
            path,
            IsSystemComponent,
            source,
            accessor.ReadFileVersion(path)));
    }
}
