using AgentEnvManager.Core.Inspection;
using AgentEnvManager.Core.Scanning;
using AgentEnvManager.Core.Tests.TestSupport;

namespace AgentEnvManager.Core.Tests.Scanning;

public sealed class WindowsEnvironmentSnapshotSourceTests
{
    [Fact]
    public async Task InspectAsync_resolves_compatibility_junctions_to_real_storage()
    {
        var accessor = CreateAccessor();
        accessor.SetFolder(
            Environment.SpecialFolder.UserProfile,
            @"C:\Users\tester");
        accessor.SetVariable("CODEX_HOME", @"E:\Codex\.codex");
        accessor.AddDirectory(@"E:\Codex\.codex");
        var ccSwitchJunction = @"C:\Users\tester\.cc-switch";
        accessor.AddDirectory(ccSwitchJunction);
        accessor.AddDirectory(@"E:\Codex\.cc-switch");
        accessor.SetLinkTarget(
            ccSwitchJunction,
            @"E:\Codex\.cc-switch");

        var report = await InspectAsync(accessor);

        // 兼容 Junction 不能再以链接路径出现在环境清单里。
        var ccSwitchLocations = report.Environments
            .Where(item => item.Asset.Name == "CC Switch")
            .Select(item => item.Asset.Location)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        Assert.Contains(@"E:\Codex\.cc-switch", ccSwitchLocations);
        Assert.DoesNotContain(ccSwitchJunction, ccSwitchLocations);

        // ChatGPT 的配置环境应来自 CODEX_HOME，而不是 Launcher 目录。
        var chatGpt = Assert.Single(
            report.Environments,
            item => item.Asset.Name == "ChatGPT");
        Assert.Equal(@"E:\Codex\.codex", chatGpt.Asset.Location);
    }

    [Fact]
    public async Task InspectAsync_falls_back_to_version_probe_for_package_managers()
    {
        var accessor = CreateAccessor();
        accessor.AddPath(PathScope.User, @"D:\tools\pnpm");
        accessor.AddPath(PathScope.Process, @"D:\tools\pnpm");
        accessor.AddFile(@"D:\tools\pnpm\pnpm.cmd");

        var report = await InspectAsync(
            accessor,
            new FakeVersionProbe("10.1.0"));

        var pnpm = Assert.Single(
            report.Environments,
            item => item.Asset.Name == "pnpm");
        Assert.Equal("10.1.0", pnpm.Asset.Version);
    }

    [Fact]
    public async Task InspectAsync_reads_package_manager_version_from_manifest()
    {
        var accessor = CreateAccessor();
        accessor.AddPath(PathScope.User, @"D:\node");
        accessor.AddPath(PathScope.Process, @"D:\node");
        accessor.AddFile(@"D:\node\npm.CMD");
        accessor.AddTextFile(
            @"D:\node\node_modules\npm\package.json",
            """{ "version": "11.21.0" }""");

        var report = await InspectAsync(accessor);

        var npm = Assert.Single(
            report.Environments,
            item => item.Asset.Name == "npm");
        Assert.Equal("11.21.0", npm.Asset.Version);
    }

    [Fact]
    public async Task InspectAsync_merges_entries_from_the_same_install_root()
    {
        var accessor = CreateAccessor();
        accessor.AddPath(
            PathScope.User,
            @"D:\Anaconda3\Scripts",
            @"D:\Anaconda3\Library\bin");
        accessor.AddPath(
            PathScope.Process,
            @"D:\Anaconda3\Scripts",
            @"D:\Anaconda3\Library\bin");
        accessor.AddFile(@"D:\Anaconda3\Scripts\conda.exe");
        accessor.AddFile(@"D:\Anaconda3\Library\bin\conda.bat");

        var report = await InspectAsync(accessor);

        // conda.exe 与 Library\bin\conda.bat 属于同一安装，合并为一条。
        var conda = Assert.Single(
            report.Environments,
            item => item.Asset.Name == "Conda");
        Assert.EndsWith(
            Path.Combine("Scripts", "conda.exe"),
            conda.Asset.Location,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InspectAsync_covers_cli_tools_and_protects_system_paths()
    {
        var accessor = CreateAccessor();
        accessor.SetFolder(
            Environment.SpecialFolder.Windows,
            @"C:\WINDOWS");
        accessor.SetFolder(
            Environment.SpecialFolder.ProgramFiles,
            @"C:\Program Files");
        accessor.AddPath(PathScope.User, @"D:\Tools");
        accessor.AddPath(PathScope.Process, @"D:\Tools");
        accessor.AddPath(
            PathScope.User,
            @"C:\Program Files\WindowsApps\Store_1.0_x64__abc");
        accessor.AddPath(
            PathScope.Process,
            @"C:\Program Files\WindowsApps\Store_1.0_x64__abc");
        accessor.AddFile(@"D:\Tools\rg.exe", "15.2.0");
        accessor.AddFile(@"D:\Tools\gh.exe", "2.102.0");
        accessor.AddFile(@"D:\Tools\bash.exe");
        accessor.AddFile(@"D:\Tools\codex.cmd");
        accessor.AddFile(
            @"C:\Program Files\WindowsApps\Store_1.0_x64__abc\winget.exe",
            "1.26.0");

        var report = await InspectAsync(accessor);

        Assert.Contains(
            report.Environments,
            item => item.Asset.Name == "ripgrep");
        Assert.Contains(
            report.Environments,
            item => item.Asset.Name == "GitHub CLI");
        Assert.Contains(
            report.Environments,
            item => item.Asset.Name == "Git Bash");
        Assert.Contains(
            report.Environments,
            item => item.Asset.Name == "Codex CLI");
        var winget = Assert.Single(
            report.Environments,
            item => item.Asset.Name == "winget");
        Assert.True(winget.Asset.IsSystemComponent);
    }

    [Fact]
    public async Task InspectAsync_finds_chatgpt_through_compatibility_junctions()
    {
        var accessor = CreateAccessor();
        accessor.SetFolder(
            Environment.SpecialFolder.UserProfile,
            @"C:\Users\tester");
        accessor.SetFolder(
            Environment.SpecialFolder.ApplicationData,
            @"C:\Users\tester\AppData\Roaming");
        accessor.SetFolder(
            Environment.SpecialFolder.LocalApplicationData,
            @"C:\Users\tester\AppData\Local");
        var localJunction = @"C:\Users\tester\AppData\Local\Codex";
        accessor.AddDirectory(localJunction);
        accessor.SetLinkTarget(localJunction, @"E:\Codex\.codex");

        var report = await InspectAsync(accessor);

        // 未设置 CODEX_HOME 时，也应通过 %LOCALAPPDATA%\Codex 兼容
        // Junction 找到真实配置环境。
        var chatGpt = Assert.Single(
            report.Environments,
            item => item.Asset.Name == "ChatGPT");
        Assert.Equal(@"E:\Codex\.codex", chatGpt.Asset.Location);
    }

    [Fact]
    public async Task InspectAsync_reports_commands_found_in_multiple_path_locations()
    {
        var accessor = CreateAccessor();
        accessor.AddPath(PathScope.User, @"D:\First", @"""E:\Second\""");
        accessor.AddPath(PathScope.Process, @"D:\First", @"E:\Second");
        accessor.AddFile(@"D:\First\node.exe", "22.22.0");
        accessor.AddFile(@"E:\Second\node.exe", "20.18.0");

        var report = await InspectAsync(accessor);

        var conflict = Assert.Single(
            report.CommandPathConflicts,
            item => item.Name == "Node.js");
        Assert.Equal(
            [@"D:\First\node.EXE", @"E:\Second\node.EXE"],
            conflict.Candidates.Select(candidate => candidate.Path));
        Assert.True(conflict.Candidates[0].Effective);
        Assert.False(conflict.Candidates[1].Effective);
    }

    [Fact]
    public async Task InspectAsync_only_marks_the_canonical_system_shell_as_system()
    {
        var accessor = CreateAccessor();
        accessor.AddPath(PathScope.User, @"E:\Tools");
        accessor.AddPath(PathScope.Process, @"E:\Tools");
        accessor.AddFile(@"E:\Tools\cmd.exe");
        accessor.AddFile(@"E:\Tools\pwsh.exe");

        var report = await InspectAsync(accessor);

        var cmd = Assert.Single(
            report.Environments,
            item => item.Asset.Name == "cmd");
        var powerShell7 = Assert.Single(
            report.Environments,
            item => item.Asset.Name == "PowerShell 7");
        Assert.False(cmd.Asset.IsSystemComponent);
        Assert.Equal(EnvironmentAssetKind.Shell, powerShell7.Asset.Kind);
    }

    [Fact]
    public async Task InspectAsync_preserves_drive_root_path_display()
    {
        var accessor = CreateAccessor();
        accessor.AddPath(PathScope.User, @"C:\");
        accessor.AddPath(PathScope.Machine, "C:/");

        var report = await InspectAsync(accessor);

        var conflict = Assert.Single(report.PathConflicts);
        Assert.Equal(@"C:\", conflict.Path);
    }

    [Fact]
    public async Task InspectAsync_discovers_runtimes_from_app_paths_registry()
    {
        var accessor = CreateAccessor();
        accessor.AddAppPath("node.exe", @"E:\Registered\node.exe");
        accessor.AddFile(@"E:\Registered\node.exe", "22.22.0");

        var report = await InspectAsync(accessor);

        var node = Assert.Single(
            report.Environments,
            item => item.Asset.Name == "Node.js");
        Assert.Equal(@"E:\Registered\node.exe", node.Asset.Location);
        Assert.Equal(
            DiscoverySource.AppPathsRegistry,
            node.Asset.Source.Kind);
    }

    [Fact]
    public async Task InspectAsync_respects_pathext_priority_within_a_path_directory()
    {
        var accessor = CreateAccessor();
        accessor.SetVariable("PATHEXT", ".CMD;.EXE");
        accessor.AddPath(PathScope.User, @"E:\Tools");
        accessor.AddPath(PathScope.Process, @"E:\Tools");
        accessor.AddFile(@"E:\Tools\node.CMD", "22.22.0");
        accessor.AddFile(@"E:\Tools\node.EXE", "22.22.0");

        var report = await InspectAsync(accessor);

        var node = Assert.Single(
            report.Environments,
            item => item.Asset.Name == "Node.js");
        Assert.EndsWith(@"\node.CMD", node.Asset.Location);
    }

    [Fact]
    public async Task InspectAsync_discovers_chatgpt_configuration_environment()
    {
        var accessor = CreateAccessor();
        accessor.AddDirectory(
            @"C:\Users\Test\AppData\Local\OpenAI\ChatGPTLauncher");

        var report = await InspectAsync(accessor);

        var chatGpt = Assert.Single(
            report.Environments,
            item => item.Asset.Name == "ChatGPT Launcher");
        Assert.Equal(
            EnvironmentAssetKind.AgentConfiguration,
            chatGpt.Asset.Kind);
        Assert.Equal(
            "LocalAppData OpenAI",
            chatGpt.Asset.Source.Description);
    }

    [Fact]
    public async Task InspectAsync_discovers_real_agent_configuration_roots()
    {
        var accessor = CreateAccessor();
        accessor.SetVariable("CC_SWITCH_HOME", @"E:\Codex\.cc-switch");
        accessor.AddDirectory(@"C:\Users\Test\.workbuddy");
        accessor.AddDirectory(@"C:\Users\Test\WorkBuddy");
        accessor.AddDirectory(@"C:\Users\Test\.marvis");
        accessor.AddDirectory(@"E:\Codex\.cc-switch");

        var report = await InspectAsync(accessor);

        Assert.Contains(
            report.Environments,
            item => item.Asset.Name == "WorkBuddy");
        Assert.Contains(
            report.Environments,
            item => item.Asset.Name == "WorkBuddy 工作区");
        Assert.Contains(
            report.Environments,
            item => item.Asset.Name == "Marvis");
        Assert.Contains(
            report.Environments,
            item => item.Asset.Name == "CC Switch"
                && item.Asset.Location == @"E:\Codex\.cc-switch");
    }

    private static async Task<InspectionReport> InspectAsync(
        FakeWindowsEnvironmentAccessor accessor,
        IVersionProbe? versionProbe = null)
    {
        var manager = new EnvironmentManager(
            new WindowsEnvironmentProbe(
                new WindowsEnvironmentSnapshotSource(
                    accessor,
                    versionProbe)),
            assetHasher: new FixedAssetHasher("asset-hash"));
        return await manager.InspectAsync();
    }

    private sealed class FakeVersionProbe(string? version) : IVersionProbe
    {
        public string? TryReadVersion(
            string command,
            string executablePath)
        {
            return version;
        }
    }

    private static FakeWindowsEnvironmentAccessor CreateAccessor()
    {
        var accessor = new FakeWindowsEnvironmentAccessor();
        accessor.SetFolder(Environment.SpecialFolder.Windows, @"C:\Windows");
        accessor.SetFolder(Environment.SpecialFolder.ProgramFiles, @"C:\Program Files");
        accessor.SetFolder(
            Environment.SpecialFolder.LocalApplicationData,
            @"C:\Users\Test\AppData\Local");
        accessor.SetFolder(
            Environment.SpecialFolder.ApplicationData,
            @"C:\Users\Test\AppData\Roaming");
        accessor.SetFolder(
            Environment.SpecialFolder.UserProfile,
            @"C:\Users\Test");
        accessor.SetVariable("PATHEXT", ".EXE;.CMD");
        return accessor;
    }

    private sealed class FakeWindowsEnvironmentAccessor
        : IWindowsEnvironmentAccessor
    {
        private readonly Dictionary<string, string?> _variables = [];
        private readonly Dictionary<Environment.SpecialFolder, string> _folders = [];
        private readonly Dictionary<PathScope, List<string>> _paths = [];
        private readonly HashSet<string> _files = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _directories = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string?> _versions =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _linkTargets =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _textFiles =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly List<WindowsAppPathRegistration> _appPaths = [];

        public string? GetEnvironmentVariable(string name)
        {
            return _variables.GetValueOrDefault(name);
        }

        public IReadOnlyList<string> ReadPath(PathScope scope)
        {
            return _paths.GetValueOrDefault(scope) ?? [];
        }

        public string GetFolderPath(Environment.SpecialFolder folder)
        {
            return _folders.GetValueOrDefault(folder, string.Empty);
        }

        public string ExpandEnvironmentVariables(string value)
        {
            var expanded = value;
            foreach (var (name, replacement) in _variables)
            {
                expanded = expanded.Replace(
                    $"%{name}%",
                    replacement ?? string.Empty,
                    StringComparison.OrdinalIgnoreCase);
            }

            return expanded;
        }

        public bool FileExists(string path)
        {
            return _files.Contains(path);
        }

        public bool DirectoryExists(string path)
        {
            return _directories.Contains(path);
        }

        public IReadOnlyList<string> EnumerateDirectories(
            string root,
            string searchPattern)
        {
            return [];
        }

        public IReadOnlyList<WindowsAppPathRegistration> ReadAppPathRegistrations()
        {
            return _appPaths;
        }

        public string? ReadFileVersion(string path)
        {
            return _versions.GetValueOrDefault(path);
        }

        public string? ResolveLinkTarget(string path)
        {
            return _linkTargets.GetValueOrDefault(path);
        }

        public void SetLinkTarget(string path, string target)
        {
            _linkTargets[path] = target;
        }

        public string? ReadTextFile(string path)
        {
            return _textFiles.GetValueOrDefault(path);
        }

        public void AddTextFile(string path, string content)
        {
            _textFiles[path] = content;
        }

        public void SetVariable(string name, string? value)
        {
            _variables[name] = value;
        }

        public void SetFolder(
            Environment.SpecialFolder folder,
            string path)
        {
            _folders[folder] = path;
        }

        public void AddPath(PathScope scope, params string[] values)
        {
            if (!_paths.TryGetValue(scope, out var paths))
            {
                paths = [];
                _paths[scope] = paths;
            }

            paths.AddRange(values);
        }

        public void AddFile(string path, string? version = null)
        {
            _files.Add(path);
            _versions[path] = version;
        }

        public void AddDirectory(string path)
        {
            _directories.Add(path);
        }

        public void AddAppPath(string executableName, string path)
        {
            _appPaths.Add(new WindowsAppPathRegistration(executableName, path));
        }
    }
}
