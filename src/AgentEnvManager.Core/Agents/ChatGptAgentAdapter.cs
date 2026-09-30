namespace AgentEnvManager.Core.Agents;

public sealed class ChatGptAgentAdapter(
    IAgentProcessRunner processRunner,
    IAgentConfigurationBackupStore backupStore)
    : IAgentAdapter
{
    private const string LauncherFileName = "agent-env-manager.launch.ps1";

    public string Name => "ChatGPT";

    public Task<AgentDiscoveryResult> DiscoverAsync(
        AgentDiscoveryRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var home = request.ConfigurationDirectory;
        if (string.IsNullOrWhiteSpace(home))
        {
            home = Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "OpenAI",
                "ChatGPTLauncher");
        }

        home = Path.GetFullPath(home);
        var executable = request.Executable;
        var installed = Directory.Exists(home)
            && !string.IsNullOrWhiteSpace(executable)
            && File.Exists(executable)
            && File.Exists(GetBundledCodexExecutable(executable));
        return Task.FromResult(new AgentDiscoveryResult(
            installed,
            home,
            executable,
            installed
                ? "已发现 ChatGPT。"
                : "未完整发现 ChatGPT 配置环境或可执行文件。"));
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
