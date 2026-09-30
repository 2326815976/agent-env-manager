using System.Diagnostics;
using AgentEnvManager.Core.Inspection;
using Microsoft.Win32;

namespace AgentEnvManager.Core.Scanning;

public sealed class WindowsEnvironmentSnapshotSource : IWindowsEnvironmentSnapshotSource
{
    private static readonly string[] AppPathVariableNames =
    [
        "python.exe",
        "node.exe",
        "git.exe",
        "pwsh.exe",
        "cmd.exe",
        "winget.exe"
    ];

    public WindowsEnvironmentSnapshot Capture()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("当前版本仅支持 Windows。");
        }

        var executables = new Dictionary<string, ExecutableCandidate>(
            StringComparer.OrdinalIgnoreCase);
        var directories = new Dictionary<string, DirectoryCandidate>(
            StringComparer.OrdinalIgnoreCase);

        AddPathExecutables(executables);
        AddKnownExecutables(executables);
        AddRegistryAppPathExecutables(executables);
        AddKnownAgentConfigurationDirectories(directories);

        return new WindowsEnvironmentSnapshot(
            executables.Values.ToArray(),
            directories.Values.ToArray(),
            ReadPathEntries());
    }

    private static void AddPathExecutables(
        IDictionary<string, ExecutableCandidate> executables)
    {
        AddResolvedCommand(
            executables,
            "python",
            EnvironmentAssetKind.ToolRuntime,
            "Python",
            isSystemComponent: false);
        AddResolvedCommand(
            executables,
            "py",
            EnvironmentAssetKind.ToolRuntime,
            "Python Launcher",
            isSystemComponent: false);
        AddResolvedCommand(
            executables,
            "node",
            EnvironmentAssetKind.ToolRuntime,
            "Node.js",
            isSystemComponent: false);
        AddResolvedCommand(
            executables,
            "git",
            EnvironmentAssetKind.ToolRuntime,
            "Git",
            isSystemComponent: false);
        AddResolvedCommand(
            executables,
            "pwsh",
            EnvironmentAssetKind.ToolRuntime,
            "PowerShell 7",
            isSystemComponent: false);
        AddResolvedCommand(
            executables,
            "powershell",
            EnvironmentAssetKind.Shell,
            "Windows PowerShell",
            isSystemComponent: true);
        AddResolvedCommand(
            executables,
            "cmd",
            EnvironmentAssetKind.Shell,
            "cmd",
            isSystemComponent: true);
        AddResolvedCommand(
            executables,
            "npm",
            EnvironmentAssetKind.PackageManager,
            "npm",
            isSystemComponent: false);
        AddResolvedCommand(
            executables,
            "pnpm",
            EnvironmentAssetKind.PackageManager,
            "pnpm",
            isSystemComponent: false);
        AddResolvedCommand(
            executables,
            "winget",
            EnvironmentAssetKind.PackageManager,
            "winget",
            isSystemComponent: false);
        AddResolvedCommand(
            executables,
            "uv",
            EnvironmentAssetKind.PackageManager,
            "uv",
            isSystemComponent: false);
        AddResolvedCommand(
            executables,
            "conda",
            EnvironmentAssetKind.PackageManager,
            "Conda",
            isSystemComponent: false);
    }

    private static void AddKnownExecutables(
        IDictionary<string, ExecutableCandidate> executables)
    {
        var programFiles = Environment.GetFolderPath(
            Environment.SpecialFolder.ProgramFiles);
        var systemRoot = Environment.GetFolderPath(
            Environment.SpecialFolder.Windows);
        var localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        var roamingAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.ApplicationData);

        AddExecutableIfExists(
            executables,
            Path.Combine(systemRoot, "System32", "cmd.exe"),
            EnvironmentAssetKind.Shell,
            "cmd",
            isSystemComponent: true,
            Source: "系统路径");
        AddExecutableIfExists(
            executables,
            Path.Combine(
                systemRoot,
                "System32",
                "WindowsPowerShell",
                "v1.0",
                "powershell.exe"),
            EnvironmentAssetKind.Shell,
            "Windows PowerShell",
            isSystemComponent: true,
            Source: "系统路径");
        AddExecutableIfExists(
            executables,
            Path.Combine(programFiles, "PowerShell", "7", "pwsh.exe"),
            EnvironmentAssetKind.ToolRuntime,
            "PowerShell 7",
            isSystemComponent: false,
            Source: "常见安装目录");
        AddExecutableIfExists(
            executables,
            Path.Combine(programFiles, "Git", "cmd", "git.exe"),
            EnvironmentAssetKind.ToolRuntime,
            "Git",
            isSystemComponent: false,
            Source: "常见安装目录");
        AddExecutableIfExists(
            executables,
            Path.Combine(programFiles, "nodejs", "node.exe"),
            EnvironmentAssetKind.ToolRuntime,
            "Node.js",
            isSystemComponent: false,
            Source: "常见安装目录");

        AddPythonInstallations(
            executables,
            Path.Combine(programFiles, "Python"),
            Source: "常见安装目录");
        AddPythonInstallations(
            executables,
            Path.Combine(localAppData, "Programs", "Python"),
            Source: "常见安装目录");
        AddUvManagedPythonInstallations(
            executables,
            Path.Combine(roamingAppData, "uv", "python"));
    }

    private static void AddRegistryAppPathExecutables(
        IDictionary<string, ExecutableCandidate> executables)
    {
        AddRegistryAppPaths(
            Registry.CurrentUser,
            executables);
        AddRegistryAppPaths(
            Registry.LocalMachine,
            executables);
    }

    private static void AddRegistryAppPaths(
        RegistryKey root,
        IDictionary<string, ExecutableCandidate> executables)
    {
        using var appPaths = root.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\App Paths");
        if (appPaths is null)
        {
            return;
        }

        foreach (var variableName in AppPathVariableNames)
        {
            using var appPath = appPaths.OpenSubKey(variableName);
            if (appPath?.GetValue(null) is not string rawPath)
            {
                continue;
            }

            var path = Environment.ExpandEnvironmentVariables(
                rawPath.Trim().Trim('"'));
            var asset = variableName.ToLowerInvariant() switch
            {
                "python.exe" => new ExecutableCandidate(
                    EnvironmentAssetKind.ToolRuntime,
                    "Python",
                    path,
                    IsSystemComponent: false,
                    Source: "App Paths 注册表"),
                "node.exe" => new ExecutableCandidate(
                    EnvironmentAssetKind.ToolRuntime,
                    "Node.js",
                    path,
                    IsSystemComponent: false,
                    Source: "App Paths 注册表"),
                "git.exe" => new ExecutableCandidate(
                    EnvironmentAssetKind.ToolRuntime,
                    "Git",
                    path,
                    IsSystemComponent: false,
                    Source: "App Paths 注册表"),
                "pwsh.exe" => new ExecutableCandidate(
                    EnvironmentAssetKind.ToolRuntime,
                    "PowerShell 7",
                    path,
                    IsSystemComponent: false,
                    Source: "App Paths 注册表"),
                "cmd.exe" => new ExecutableCandidate(
                    EnvironmentAssetKind.Shell,
                    "cmd",
                    path,
                    IsSystemComponent: true,
                    Source: "App Paths 注册表"),
                "winget.exe" => new ExecutableCandidate(
                    EnvironmentAssetKind.PackageManager,
                    "winget",
                    path,
                    IsSystemComponent: false,
                    Source: "App Paths 注册表"),
                _ => null
            };

            if (asset is null)
            {
                continue;
            }

            AddExecutableIfExists(executables, asset);
        }
    }

    private static void AddKnownAgentConfigurationDirectories(
        IDictionary<string, DirectoryCandidate> directories)
    {
        var userProfile = Environment.GetFolderPath(
            Environment.SpecialFolder.UserProfile);
        var codexHome = Environment.GetEnvironmentVariable("CODEX_HOME");

        AddDirectoryIfExists(
            directories,
            new DirectoryCandidate(
                "Codex",
                codexHome ?? Path.Combine(userProfile, ".codex"),
                codexHome is null ? "用户目录" : "CODEX_HOME"));
        AddDirectoryIfExists(
            directories,
            new DirectoryCandidate(
                "CC Switch",
                Path.Combine(userProfile, ".cc-switch"),
                "用户目录"));
        AddDirectoryIfExists(
            directories,
            new DirectoryCandidate(
                "CC Switch",
                Path.Combine(userProfile, ".ccswitch"),
                "用户目录"));
        AddDirectoryIfExists(
            directories,
            new DirectoryCandidate(
                "WorkBuddy",
                Path.Combine(userProfile, ".workbuddy"),
                "用户目录"));
    }

    private static void AddPythonInstallations(
        IDictionary<string, ExecutableCandidate> executables,
        string root,
        string Source)
    {
        if (!Directory.Exists(root))
        {
            return;
        }

        foreach (var directory in Directory.EnumerateDirectories(
            root,
            "Python*",
            SearchOption.TopDirectoryOnly))
        {
            AddExecutableIfExists(
                executables,
                new ExecutableCandidate(
                    EnvironmentAssetKind.ToolRuntime,
                    "Python",
                    Path.Combine(directory, "python.exe"),
                    IsSystemComponent: false,
                    Source));
        }
    }

    private static void AddUvManagedPythonInstallations(
        IDictionary<string, ExecutableCandidate> executables,
        string root)
    {
        if (!Directory.Exists(root))
        {
            return;
        }

        foreach (var directory in Directory.EnumerateDirectories(
            root,
            "cpython-*",
            SearchOption.TopDirectoryOnly))
        {
            AddExecutableIfExists(
                executables,
                new ExecutableCandidate(
                    EnvironmentAssetKind.ToolRuntime,
                    "Python",
                    Path.Combine(directory, "python.exe"),
                    IsSystemComponent: false,
                    Source: "uv 运行时目录"));
        }
    }

    private static void AddResolvedCommand(
        IDictionary<string, ExecutableCandidate> executables,
        string command,
        EnvironmentAssetKind kind,
        string name,
        bool isSystemComponent)
    {
        var path = ResolveExecutablePath(command);
        if (path is null)
        {
            return;
        }

        AddExecutableIfExists(
            executables,
            new ExecutableCandidate(
                kind,
                name,
                path,
                IsSystemComponent: isSystemComponent,
                Source: "PATH"));
    }

    private static void AddExecutableIfExists(
        IDictionary<string, ExecutableCandidate> executables,
        ExecutableCandidate candidate)
    {
        if (!File.Exists(candidate.Path))
        {
            return;
        }

        var path = Path.GetFullPath(candidate.Path);
        if (executables.ContainsKey(path))
        {
            return;
        }

        executables.Add(
            path,
            candidate with
            {
                Path = path,
                Version = ReadVersion(path)
            });
    }

    private static void AddExecutableIfExists(
        IDictionary<string, ExecutableCandidate> executables,
        string path,
        EnvironmentAssetKind kind,
        string name,
        bool isSystemComponent,
        string Source)
    {
        AddExecutableIfExists(
            executables,
            new ExecutableCandidate(
                kind,
                name,
                path,
                isSystemComponent,
                Source));
    }

    private static void AddDirectoryIfExists(
        IDictionary<string, DirectoryCandidate> directories,
        DirectoryCandidate candidate)
    {
        if (!Directory.Exists(candidate.Path))
        {
            return;
        }

        var path = Path.GetFullPath(candidate.Path);
        if (directories.ContainsKey(path))
        {
            return;
        }

        directories.Add(
            path,
            candidate with
            {
                Path = path
            });
    }

    private static string? ResolveExecutablePath(string command)
    {
        var pathValue = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathValue))
        {
            return null;
        }

        var extensions = (Environment.GetEnvironmentVariable("PATHEXT")
                ?? ".COM;.EXE;.BAT;.CMD")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var directory in pathValue.Split(
            Path.PathSeparator,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var expandedDirectory = Environment.ExpandEnvironmentVariables(
                directory.Trim().Trim('"'));

            foreach (var extension in extensions)
            {
                var candidate = Path.Combine(
                    expandedDirectory,
                    command + extension);
                if (File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }
        }

        return null;
    }

    private static IReadOnlyList<PathEntry> ReadPathEntries()
    {
        var userEntries = ReadPath(EnvironmentVariableTarget.User)
            .Select(value => new PathEntry(value, PathScope.User))
            .ToArray();
        var machineEntries = ReadPath(EnvironmentVariableTarget.Machine)
            .Select(value => new PathEntry(value, PathScope.Machine))
            .ToArray();
        var knownEntries = userEntries
            .Concat(machineEntries)
            .Select(entry => NormalizePath(entry.Value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var processOnlyEntries = ReadPath(EnvironmentVariableTarget.Process)
            .Where(value => !knownEntries.Contains(NormalizePath(value)))
            .Select(value => new PathEntry(value, PathScope.Process));

        return userEntries
            .Concat(machineEntries)
            .Concat(processOnlyEntries)
            .ToArray();
    }

    private static IReadOnlyList<string> ReadPath(
        EnvironmentVariableTarget target)
    {
        var path = Environment.GetEnvironmentVariable("Path", target);
        if (string.IsNullOrWhiteSpace(path))
        {
            return [];
        }

        return path.Split(
                Path.PathSeparator,
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => Environment.ExpandEnvironmentVariables(
                value.Trim().Trim('"')))
            .Select(Path.GetFullPath)
            .ToArray();
    }

    private static string NormalizePath(string path)
    {
        return Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .ToUpperInvariant();
    }

    private static string? ReadVersion(string path)
    {
        try
        {
            var versionInfo = FileVersionInfo.GetVersionInfo(path);
            var version = versionInfo.ProductVersion ?? versionInfo.FileVersion;
            return string.IsNullOrWhiteSpace(version)
                ? null
                : version
                    .Split('+', 2)[0]
                    .Split(' ', 2)[0];
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }
}
