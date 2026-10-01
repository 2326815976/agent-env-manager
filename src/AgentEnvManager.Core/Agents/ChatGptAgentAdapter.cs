namespace AgentEnvManager.Core.Agents;

public sealed class ChatGptAgentAdapter(
    IAgentProcessRunner processRunner,
    IAgentConfigurationBackupStore backupStore,
    IExecutableLocator? executableLocator = null,
    Func<Environment.SpecialFolder, string>? getFolderPath = null,
    Func<string, bool>? isReparsePoint = null,
    Func<string, string?>? resolveLinkTarget = null,
    Func<string, string?>? readEnvironmentVariable = null)
    : IAgentAdapter
{
    private const string LauncherFileName = "agent-env-manager.launch.ps1";
    private readonly IExecutableLocator _executableLocator =
        executableLocator ?? new ChatGptExecutableLocator();
    private readonly Func<Environment.SpecialFolder, string> _getFolderPath =
        getFolderPath ?? Environment.GetFolderPath;
    private readonly Func<string, bool> _isReparsePoint =
        isReparsePoint ?? IsReparsePoint;
    private readonly Func<string, string?> _resolveLinkTarget =
        resolveLinkTarget ?? ResolveLinkTarget;
    private readonly Func<string, string?> _readEnvironmentVariable =
        readEnvironmentVariable ?? Environment.GetEnvironmentVariable;

    public string Name => "ChatGPT";

    public Task<AgentDiscoveryResult> DiscoverAsync(
        AgentDiscoveryRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var homeSource = "显式指定";
        var home = request.ConfigurationDirectory;
        if (string.IsNullOrWhiteSpace(home))
        {
            home = _readEnvironmentVariable("CODEX_HOME");
            if (!string.IsNullOrWhiteSpace(home))
            {
                homeSource = "CODEX_HOME";
            }
        }

        if (string.IsNullOrWhiteSpace(home))
        {
            home = Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "OpenAI",
                "ChatGPTLauncher");
            homeSource = "默认位置";
        }

        home = Path.GetFullPath(home);
        var executable = request.Executable;
        if (string.IsNullOrWhiteSpace(executable))
        {
            executable = _executableLocator.FindExecutable("ChatGPT");
        }

        var bundledCodexPath = string.IsNullOrWhiteSpace(executable)
            ? null
            : TryGetBundledCodexExecutable(executable);
        var installed = Directory.Exists(home)
            && !string.IsNullOrWhiteSpace(executable)
            && File.Exists(executable)
            && bundledCodexPath is not null
            && File.Exists(bundledCodexPath);
        var compatibilityJunctions = GetCompatibilityJunctions(home);
        var bindingFilePath = Path.Combine(home, LauncherFileName);
        var isBound = File.Exists(bindingFilePath);
        return Task.FromResult(new AgentDiscoveryResult(
            installed,
            home,
            executable,
            installed
                ? isBound
                    ? "已发现 ChatGPT 启动链，且已存在绑定文件。"
                    : "已发现 ChatGPT 启动链，尚未绑定。"
                : "未完整发现 ChatGPT 配置环境或可执行文件。",
            BundledCodexPath: bundledCodexPath,
            HomeSource: homeSource,
            CompatibilityJunctionPaths: compatibilityJunctions,
            IsBound: isBound,
            BindingFilePath: bindingFilePath));
    }

    // 兼容 Junction 由研究契约确定：%APPDATA%\Codex 与
    // %LOCALAPPDATA%\Codex 指向真实配置环境。
    private IReadOnlyList<string> GetCompatibilityJunctions(string home)
    {
        string[] candidates =
        [
            Path.Combine(
                _getFolderPath(Environment.SpecialFolder.ApplicationData),
                "Codex"),
            Path.Combine(
                _getFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "Codex")
        ];
        return candidates
            .Where(candidate =>
                !string.IsNullOrWhiteSpace(candidate)
                && _isReparsePoint(candidate)
                && IsLinkedTo(candidate, home))
            .ToArray();
    }

    private bool IsLinkedTo(string linkPath, string targetPath)
    {
        var target = _resolveLinkTarget(linkPath);
        return target is not null
            && PathsEqual(target, targetPath);
    }

    private static bool PathsEqual(string left, string right)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(left).TrimEnd('\\', '/'),
                Path.GetFullPath(right).TrimEnd('\\', '/'),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (
            exception is ArgumentException
            or NotSupportedException
            or PathTooLongException)
        {
            return false;
        }
    }

    private static string? ResolveLinkTarget(string linkPath)
    {
        try
        {
            return Directory.ResolveLinkTarget(
                linkPath,
                returnFinalTarget: true)?.FullName;
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException)
        {
            return null;
        }
    }

    private static string? TryGetBundledCodexExecutable(string executable)
    {
        try
        {
            return GetBundledCodexExecutable(executable);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
            or ArgumentException
            or NotSupportedException
            or PathTooLongException)
        {
            return null;
        }
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            return (File.GetAttributes(path)
                & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException)
        {
            return false;
        }
    }

    public Task<AgentBindingPlan> CreatePlanAsync(
        AgentBindingRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var configurationDirectory = Path.GetFullPath(
            request.ConfigurationDirectory);
        var executable = Path.GetFullPath(request.Executable);
        EnsureBundledCodexExecutableExists(executable);

        var workspacePath = string.IsNullOrWhiteSpace(request.WorkspacePath)
            ? configurationDirectory
            : Path.GetFullPath(request.WorkspacePath);
        var runtimeCommand = string.IsNullOrWhiteSpace(request.RuntimeCommand)
            ? request.RuntimeName
            : request.RuntimeCommand;
        var bindingFilePath = Path.Combine(
            configurationDirectory,
            LauncherFileName);
        var content = CreateLauncherContent(
            executable,
            workspacePath,
            request.ManagedEntryPath,
            configurationDirectory,
            GetBundledCodexExecutable(executable));
        var healthArguments = request.HealthArguments ??
        [
            "exec",
            "--json",
            "--skip-git-repo-check",
            "--ephemeral",
            "--color",
            "never",
            "--cd",
            workspacePath,
            $"Run {runtimeCommand} --version"
        ];

        return Task.FromResult(new AgentBindingPlan(
            Name,
            configurationDirectory,
            executable,
            request.ManagedEntryPath,
            request.RuntimeName,
            request.RuntimeVersion,
            workspacePath,
            runtimeCommand,
            healthArguments,
            bindingFilePath,
            content));
    }

    public Task<AgentConfigurationRecoveryPoint> CreateRecoveryPointAsync(
        AgentBindingPlan plan,
        CancellationToken cancellationToken = default)
    {
        return backupStore.CreateAsync(
            Name,
            [plan.BindingFilePath],
            cancellationToken);
    }

    public async Task<AgentBinding> ActivateAsync(
        AgentBindingPlan plan,
        AgentConfigurationRecoveryPoint recoveryPoint,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(plan.ConfigurationDirectory);
        await File.WriteAllTextAsync(
            plan.BindingFilePath,
            plan.BindingContent,
            cancellationToken);

        return new AgentBinding(
            plan.AgentName,
            plan.ConfigurationDirectory,
            plan.Executable,
            plan.ManagedEntryPath,
            plan.RuntimeName,
            plan.RuntimeVersion,
            plan.WorkspacePath,
            plan.RuntimeCommand,
            plan.HealthArguments,
            plan.BindingFilePath,
            recoveryPoint);
    }

    public Task<AgentProcessResult> LaunchAsync(
        AgentBinding binding,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default)
    {
        return processRunner.RunAsync(
            "powershell.exe",
            [
                "-NoProfile",
                "-ExecutionPolicy",
                "Bypass",
                "-File",
                binding.BindingFilePath,
                .. arguments
            ],
            binding.WorkspacePath,
            CreateEnvironment(binding),
            cancellationToken);
    }

    public async Task<AgentHealthCheckResult> CheckHealthAsync(
        AgentBinding binding,
        CancellationToken cancellationToken = default)
    {
        var launch = await LaunchAsync(
            binding,
            [],
            cancellationToken);
        if (launch.ExitCode != 0)
        {
            return new AgentHealthCheckResult(
                false,
                $"ChatGPT 启动失败: exit={launch.ExitCode}; " +
                $"stderr={launch.StandardError.Trim()}");
        }

        var healthExecutable = GetBundledCodexExecutable(
            binding.Executable);
        var result = await processRunner.RunAsync(
            healthExecutable,
            binding.HealthArguments,
            binding.WorkspacePath,
            CreateEnvironment(binding),
            cancellationToken);
        var health = AgentHealthOutput.Evaluate(
            result,
            binding.RuntimeName,
            binding.RuntimeVersion);
        return health.IsHealthy
            ? health with
            {
                Message =
                    $"ChatGPT app-server 已启动，{binding.RuntimeName} 工具调用成功。"
            }
            : health;
    }

    public Task RollbackAsync(
        AgentBindingPlan plan,
        AgentConfigurationRecoveryPoint recoveryPoint,
        CancellationToken cancellationToken = default)
    {
        return backupStore.RestoreAsync(
            recoveryPoint,
            cancellationToken);
    }

    private static string GetBundledCodexExecutable(
        string chatGptExecutable)
    {
        var applicationDirectory = Path.GetDirectoryName(
            Path.GetFullPath(chatGptExecutable))
            ?? throw new InvalidOperationException(
                "无法确定 ChatGPT 安装目录。");
        var bundledCodex = Path.Combine(
            applicationDirectory,
            "resources",
            "codex.exe");
        return bundledCodex;
    }

    private static void EnsureBundledCodexExecutableExists(
        string chatGptExecutable)
    {
        if (!File.Exists(GetBundledCodexExecutable(chatGptExecutable)))
        {
            throw new InvalidOperationException(
                "未找到 ChatGPT 内置 Codex，无法执行真实工具调用健康检查。");
        }
    }

    private static IReadOnlyDictionary<string, string> CreateEnvironment(
        AgentBinding binding)
    {
        return new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["CODEX_HOME"] = binding.ConfigurationDirectory,
            ["PATH"] =
                $"{binding.ManagedEntryPath};" +
                $"{Environment.GetEnvironmentVariable("PATH")}"
        };
    }

    private static string CreateLauncherContent(
        string executable,
        string workspacePath,
        string managedEntryPath,
        string configurationDirectory,
        string codexExecutable)
    {
        return string.Join(
            "\r\n",
            [
                "$ErrorActionPreference = 'Stop'",
                $"$env:CODEX_HOME = '{EscapePowerShellValue(configurationDirectory)}'",
                $"$env:PATH = '{EscapePowerShellValue(managedEntryPath)};' + $env:PATH",
                $"$executable = '{EscapePowerShellValue(executable)}'",
                $"$workspacePath = '{EscapePowerShellValue(workspacePath)}'",
                $"$codexExecutable = '{EscapePowerShellValue(codexExecutable)}'",
                "if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) { exit 2 }",
                "$launchArguments = @('--open-project', ('\"{0}\"' -f $workspacePath)) + $args",
                "Start-Process -FilePath $executable -ArgumentList $launchArguments",
                "$deadline = [DateTime]::UtcNow.AddSeconds(30)",
                "$processName = [IO.Path]::GetFileNameWithoutExtension($executable)",
                "while ([DateTime]::UtcNow -lt $deadline) {",
                "    $match = Get-Process -Name $processName -ErrorAction SilentlyContinue |",
                "        Where-Object { [string]::Equals($_.Path, $executable, [StringComparison]::OrdinalIgnoreCase) } |",
                "        Select-Object -First 1",
                "    $appServer = Get-CimInstance Win32_Process -ErrorAction SilentlyContinue |",
                "        Where-Object { $_.Name -eq 'codex.exe' -and $_.ExecutablePath -and",
                "            [string]::Equals([IO.Path]::GetFullPath($_.ExecutablePath), $codexExecutable, [StringComparison]::OrdinalIgnoreCase) -and",
                "            $_.CommandLine -match 'app-server' } |",
                "        Select-Object -First 1",
                "    if ($null -ne $match -and $null -ne $appServer) { exit 0 }",
                "    Start-Sleep -Milliseconds 250",
                "}",
                "exit 3",
                string.Empty
            ]);
    }

    private static string EscapePowerShellValue(string value)
    {
        return value.Replace("'", "''", StringComparison.Ordinal);
    }
}
