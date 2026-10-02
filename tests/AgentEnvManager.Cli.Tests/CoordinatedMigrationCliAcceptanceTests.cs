using AgentEnvManager.Core.Activation;
using AgentEnvManager.Core.Agents;
using AgentEnvManager.Core.EnvironmentVariables;
using AgentEnvManager.Core.Inspection;
using AgentEnvManager.Core.Migrations;
using AgentEnvManager.Core.Storage;

namespace AgentEnvManager.Cli.Tests;

public sealed class CoordinatedMigrationCliAcceptanceTests
{
    [Fact]
    public async Task CoordinatedMigrate_runs_full_flow_through_cli_entry()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"aem-cli-acceptance-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var codexSource = Path.Combine(root, "codex-home");
            var ccSource = Path.Combine(root, ".cc-switch");
            var codexDestination = Path.Combine(root, "moved", "codex-home");
            var ccDestination = Path.Combine(root, "moved", ".cc-switch");
            Directory.CreateDirectory(codexSource);
            Directory.CreateDirectory(ccSource);
            await File.WriteAllTextAsync(
                Path.Combine(codexSource, "config.toml"),
                "# 注释保留\n"
                + $"notify = [\"{codexSource.Replace("\\", "\\\\")}\\\\n.exe\"]\n");
            await File.WriteAllTextAsync(
                Path.Combine(ccSource, "settings.json"),
                "{\n  \"codexConfigDir\": \"E:\\\\Old\\\\.codex\",\n"
                + "  \"providerSecrets\": \"sk-x\"\n}\n");
            var store = new RecordingEnvironmentVariableStore(
                new Dictionary<string, string?>
                {
                    ["CODEX_HOME"] = @"E:\Old\.codex"
                });
            var startup = new RecordingStartupProbe();
            var manager = new EnvironmentManager(
                new StubEnvironmentProbe(),
                userEnvironmentVariableStore: store,
                activationLink: new StubActivationLink(),
                managerPaths: ManagerPaths.Resolve(
                    Path.Combine(root, "state"),
                    Path.Combine(root, "data")),
                coordinatedMigration: new CoordinatedMigrationWiring(
                    new StubProcessProbe(),
                    startup,
                    new RecordingShortcutEditor(),
                    new FileSystemMigrationSourceQuarantine(
                        Path.Combine(root, "quarantine")),
                    new StubTargetsProvider(),
                    new FixedHealthProbe(isHealthy: true)));
            var adapter = new EnvironmentManagerCliAdapter(manager);
            var output = new StringWriter();
            var error = new StringWriter();

            var exitCode = await CliApplication.RunWithManagerAsync(
                [
                    "coordinated-migrate",
                    "--codex-source",
                    codexSource,
                    "--codex-dest",
                    codexDestination,
                    "--cc-switch-source",
                    ccSource,
                    "--cc-switch-dest",
                    ccDestination,
                    "--confirm"
                ],
                () => adapter,
                output,
                error);

            var text = output.ToString();
            Assert.Equal(0, exitCode);
            Assert.Contains("协同迁移完成", text);
            Assert.Contains("源目录已隔离", text);
            Assert.Equal(string.Empty, error.ToString());
            Assert.Equal(codexDestination, await store.GetAsync("CODEX_HOME"));
            Assert.True(File.Exists(
                Path.Combine(codexDestination, "config.toml")));
            Assert.Contains(
                "sk-x",
                await File.ReadAllTextAsync(
                    Path.Combine(ccDestination, "settings.json")));
            Assert.False(Directory.Exists(codexSource));
            Assert.False(Directory.Exists(ccSource));
            Assert.Equal(2, startup.StartedPaths.Count);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class StubEnvironmentProbe : IEnvironmentProbe
    {
        public Task<EnvironmentProbeResult> ProbeAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new EnvironmentProbeResult([], []));
        }
    }

    private sealed class StubProcessProbe : IProcessControlProbe
    {
        public Task<IReadOnlyList<string>> FindRunningProcessesAsync(
            IReadOnlyList<string> processNames,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<string>>([]);
        }

        public Task StopProcessesAsync(
            IReadOnlyList<string> processNames,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingStartupProbe : ICoordinatedStartupProbe
    {
        public List<string> StartedPaths { get; } = [];

        public Task StartAsync(
            string executablePath,
            CancellationToken cancellationToken = default)
        {
            StartedPaths.Add(executablePath);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedHealthProbe(bool isHealthy)
        : ICoordinatedHealthProbe
    {
        public Task<AgentHealthCheckResult> CheckAsync(
            string codexConfigDirectory,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new AgentHealthCheckResult(
                isHealthy,
                isHealthy ? "启动链就绪。" : "启动链未就绪。"));
        }
    }

    private sealed class RecordingShortcutEditor : IShortcutEditor
    {
        public Task<ShortcutState> ReadAsync(
            string shortcutPath,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ShortcutState.Empty);
        }

        public Task WriteAsync(
            string shortcutPath,
            string targetPath,
            string workingDirectory,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class StubTargetsProvider : ICoordinatedTargetsProvider
    {
        public Task<CoordinatedShortcutTarget?> ResolveChatGptShortcutAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<CoordinatedShortcutTarget?>(null);
        }

        public Task<CoordinatedStartupTargets?> ResolveStartupTargetsAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<CoordinatedStartupTargets?>(
                new CoordinatedStartupTargets(
                    @"D:\Software\CCSwitch\cc-switch.exe",
                    @"E:\Codex\app\ChatGPT.exe"));
        }
    }

    private sealed class RecordingEnvironmentVariableStore(
        IReadOnlyDictionary<string, string?> values)
        : IUserEnvironmentVariableStore
    {
        private readonly Dictionary<string, string?> _values = new(
            values,
            StringComparer.OrdinalIgnoreCase);

        public Task<string?> GetAsync(
            string name,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_values.GetValueOrDefault(name));
        }

        public Task<IReadOnlyList<string>> ListAllAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<string>>(
                _values.Keys.ToArray());
        }

        public Task SetAsync(
            string name,
            string? value,
            bool isExpandable = false,
            CancellationToken cancellationToken = default)
        {
            _values[name] = value;
            return Task.CompletedTask;
        }

        public Task BroadcastAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class StubActivationLink : IEnvironmentActivationLink
    {
        public Task<string?> GetTargetAsync(
            string activationPath,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<string?>(null);
        }

        public Task SetTargetAsync(
            string activationPath,
            string targetPath,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task DeleteAsync(
            string activationPath,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<bool> TargetExistsAsync(
            string targetPath,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(false);
        }

        public string GetActivationTarget(string location)
        {
            return location;
        }
    }
}
