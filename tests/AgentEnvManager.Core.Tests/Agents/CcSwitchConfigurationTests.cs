using AgentEnvManager.Core.Agents;
using AgentEnvManager.Core.Inspection;
using AgentEnvManager.Core.Storage;

namespace AgentEnvManager.Core.Tests.Agents;

public sealed class CcSwitchConfigurationTests
{
    private const string SettingsJson = """
        {
          // 保留注释与未知字段
          "codexConfigDir": "E:\\Codex\\.codex",
          "skillStorageLocation": "cc_switch",
          "visibleApps": { "codex": true },
          "providerSecrets": "sk-should-not-be-exposed"
        }
        """;

    [Fact]
    public async Task DiscoverCcSwitchAsync_reports_paths_version_and_junctions()
    {
        var root = CreateTempRoot();
        try
        {
            var configRoot = Path.Combine(root, ".cc-switch");
            Directory.CreateDirectory(configRoot);
            await File.WriteAllTextAsync(
                Path.Combine(configRoot, "settings.json"),
                SettingsJson);
            var executable = Path.Combine(root, "cc-switch.exe");
            await File.WriteAllTextAsync(executable, string.Empty);
            var localRoot = Path.Combine(root, "local");
            var localJunction = Path.Combine(
                localRoot,
                "com.ccswitch.desktop");
            var manager = CreateManager(
                root,
                path => string.Equals(
                    path,
                    localJunction,
                    StringComparison.OrdinalIgnoreCase));

            var discovery = await manager.DiscoverCcSwitchAsync(
                new CcSwitchDiscoveryRequest(
                    ConfigRoot: configRoot,
                    Executable: executable,
                    WebViewLocalRoot: localRoot,
                    WebViewRoamingRoot: Path.Combine(root, "roaming")));

            Assert.True(discovery.IsInstalled);
            Assert.Equal(Path.GetFullPath(executable), discovery.Executable);
            Assert.Equal(configRoot, discovery.ConfigRoot);
            Assert.Equal(
                Path.Combine(configRoot, "settings.json"),
                discovery.SettingsFilePath);
            Assert.Equal(@"E:\Codex\.codex", discovery.CodexConfigDirectory);
            Assert.Equal([localJunction], discovery.CompatibilityJunctions);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task PreviewCcSwitchCodexConfigDirAsync_creates_recovery_ready_plan()
    {
        var root = CreateTempRoot();
        try
        {
            var configRoot = await CreateSettingsAsync(root);
            var manager = CreateManager(root);
            var target = Path.Combine(root, "new-codex-home");

            var preview = await manager.PreviewCcSwitchCodexConfigDirAsync(
                configRoot,
                target);

            Assert.Equal("codexConfigDir", preview.FieldName);
            Assert.Equal(@"E:\Codex\.codex", preview.CurrentValue);
            Assert.Equal(Path.GetFullPath(target), preview.TargetValue);
            Assert.False(preview.IsAlreadyBound);
            Assert.NotNull(preview.OperationId);
            Assert.NotNull(preview.RecoveryPointId);
            Assert.Contains(
                preview.PreservedContent,
                item => item.Contains("provider", StringComparison.Ordinal));
            Assert.DoesNotContain(
                "sk-should-not-be-exposed",
                preview.Impact);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ApplyCcSwitchCodexConfigDirAsync_preserves_unknown_fields_and_comments()
    {
        var root = CreateTempRoot();
        try
        {
            var configRoot = await CreateSettingsAsync(root);
            var settingsFilePath = Path.Combine(
                configRoot,
                "settings.json");
            var manager = CreateManager(root);
            var target = Path.Combine(root, "new-codex-home");
            var preview = await manager.PreviewCcSwitchCodexConfigDirAsync(
                configRoot,
                target);

            var result = await manager.ApplyCcSwitchCodexConfigDirAsync(
                preview);

            var updated = await File.ReadAllTextAsync(settingsFilePath);
            Assert.Equal(target, result.Discovery.CodexConfigDirectory);
            Assert.Equal(
                result.RecoveryPoint.Id,
                result.Operation.RecoveryPointId);
            Assert.Contains("保留注释与未知字段", updated);
            Assert.Contains(
                "\"skillStorageLocation\": \"cc_switch\"",
                updated);
            Assert.Contains(
                "\"providerSecrets\": \"sk-should-not-be-exposed\"",
                updated);
            Assert.Contains(
                "\"visibleApps\": { \"codex\": true }",
                updated);
            Assert.DoesNotContain(@"E:\Codex\.codex", updated);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ApplyCcSwitchCodexConfigDirAsync_rejects_changed_settings()
    {
        var root = CreateTempRoot();
        try
        {
            var configRoot = await CreateSettingsAsync(root);
            var settingsFilePath = Path.Combine(
                configRoot,
                "settings.json");
            var manager = CreateManager(root);
            var preview = await manager.PreviewCcSwitchCodexConfigDirAsync(
                configRoot,
                Path.Combine(root, "new-codex-home"));
            await File.WriteAllTextAsync(
                settingsFilePath,
                "{ \"skillStorageLocation\": \"cc_switch\" }");

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => manager.ApplyCcSwitchCodexConfigDirAsync(preview));

            var current = await manager.DiscoverCcSwitchAsync(
                new CcSwitchDiscoveryRequest(ConfigRoot: configRoot));
            Assert.Null(current.CodexConfigDirectory);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task PreviewCcSwitchCodexConfigDirAsync_rejects_drive_root()
    {
        var root = CreateTempRoot();
        try
        {
            var configRoot = await CreateSettingsAsync(root);
            var manager = CreateManager(root);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => manager.PreviewCcSwitchCodexConfigDirAsync(
                    configRoot,
                    @"C:\"));

            Assert.Contains(
                "磁盘根目录",
                exception.Message,
                StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task<string> CreateSettingsAsync(string root)
    {
        var configRoot = Path.Combine(root, ".cc-switch");
        Directory.CreateDirectory(configRoot);
        await File.WriteAllTextAsync(
            Path.Combine(configRoot, "settings.json"),
            SettingsJson);
        return configRoot;
    }

    private static EnvironmentManager CreateManager(
        string root,
        Func<string, bool>? isReparsePoint = null)
    {
        return new EnvironmentManager(
            new StubEnvironmentProbe(new EnvironmentProbeResult([], [])),
            managerPaths: ManagerPaths.Resolve(
                Path.Combine(root, "state"),
                Path.Combine(root, "data")),
            agentConfigurationBackupStore:
                new FileAgentConfigurationBackupStore(
                    Path.Combine(root, "agent-backups")),
            isReparsePoint: isReparsePoint);
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"agent-env-manager-cc-switch-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    private sealed class StubEnvironmentProbe(EnvironmentProbeResult result)
        : IEnvironmentProbe
    {
        public Task<EnvironmentProbeResult> ProbeAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(result);
        }
    }
}
