using AgentEnvManager.Core.Inspection;
using AgentEnvManager.Core.Operations;
using AgentEnvManager.Core.Runtimes;
using AgentEnvManager.Core.Storage;
using AgentEnvManager.Core.Activation;
using AgentEnvManager.Core.Adoption;
using AgentEnvManager.Core.Tests.TestSupport;

namespace AgentEnvManager.Core.Tests.Runtimes;

public sealed class PythonRuntimeInstallationTests
{
    [Fact]
    public async Task PreviewRuntimeInstallAsync_creates_recovery_ready_plan_without_touching_activation()
    {
        var root = CreateTempRoot();
        try
        {
            var manifestStore = new InMemoryManifestStore();
            var journal = new RecordingOperationJournal();
            var recoveryStore = new RecordingRecoveryPointStore();
            var link = new RecordingActivationLink();
            var manager = CreateManager(
                root,
                manifestStore,
                journal,
                recoveryStore,
                link);

            var preview = await manager.PreviewRuntimeInstallAsync(
                "python",
                "3.13.7");

            Assert.Equal("python", preview.Provider.Id);
            Assert.Equal("3.13.7", preview.Artifact.Version);
            Assert.False(preview.IsAlreadyInstalled);
            Assert.Equal(
                Path.Combine(root, "runtimes", "python", "3.13.7"),
                preview.InstallRoot);
            Assert.Equal(
                Path.Combine(
                    preview.InstallRoot,
                    "cpython-3.13.7-windows-x86_64-none"),
                preview.Location);
            Assert.Equal(
                Path.Combine(preview.Location, "python.exe"),
                preview.ExecutablePath);
            Assert.True(Path.IsPathFullyQualified(
                preview.StableActivationPath));
            Assert.True(Path.IsPathFullyQualified(
                preview.ManagedEntryPath));
            Assert.Contains(
                "不会改变当前激活版本",
                preview.Impact,
                StringComparison.Ordinal);
            Assert.Empty(link.SetHistory);

            var operation = Assert.Single(
                journal.History
                    .Where(record => record.Id == preview.OperationId)
                    .GroupBy(record => record.Id)
                    .Select(group => group.Last()));
            Assert.Equal(OperationType.Install, operation.Type);
            Assert.Equal(OperationState.RecoveryReady, operation.State);
            Assert.Equal(
                preview.RecoveryPointId,
                operation.RecoveryPointId);
            Assert.Equal(preview.InstallRoot, operation.Target);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task InstallRuntimeAsync_installs_python_and_exposes_managed_entry()
    {
        var root = CreateTempRoot();
        try
        {
            var manifestStore = new InMemoryManifestStore();
            var journal = new RecordingOperationJournal();
            var recoveryStore = new RecordingRecoveryPointStore();
            var link = new RecordingActivationLink();
            var healthCheck = new RecordingRuntimeHealthCheck(
                isHealthy: true);
            var runner = new RecordingRuntimeCommandRunner(
                async invocation =>
            {
                var installRoot = invocation.Arguments[
                    Array.IndexOf(
                        invocation.Arguments.ToArray(),
                        "--install-dir") + 1];
                var executable = Path.Combine(
                    installRoot,
                    "cpython-3.13.7-windows-x86_64-none",
                    "python.exe");
                Directory.CreateDirectory(
                    Path.GetDirectoryName(executable)!);
                File.WriteAllText(executable, string.Empty);
                var versionDirectory = Path.GetDirectoryName(executable)!;
                var aliasTarget = Path.Combine(
                    installRoot,
                    "alias-target");
                Directory.CreateDirectory(aliasTarget);
                await new WindowsJunctionActivationLink()
                    .SetTargetAsync(
                        Path.Combine(
                            installRoot,
                            "cpython-3.13-windows-x86_64-none"),
                        versionDirectory);
                return new RuntimeCommandResult(0, "installed", string.Empty);
            });
            var manager = CreateManager(
                root,
                manifestStore,
                journal,
                recoveryStore,
                link,
                runner,
                healthCheck);

            var preview = await manager.PreviewRuntimeInstallAsync(
                "python",
                "3.13.7");
            var installed = await manager.InstallRuntimeAsync(preview);

            Assert.Equal(preview.Fingerprint, installed.Manifest.Fingerprint);
            Assert.Equal(preview.ManagedEntryPath, installed.ManagedEntryPath);
            Assert.Equal(preview.ExecutablePath, installed.ExecutablePath);
            Assert.True(File.Exists(installed.ExecutablePath));
            Assert.Equal(
                preview.Location,
                await link.GetTargetAsync(preview.StableActivationPath));
            Assert.Equal(
                preview.StableActivationPath,
                await link.GetTargetAsync(preview.ManagedEntryPath));
            Assert.Equal(
                preview.ManagedEntryPath,
                healthCheck.LastManagedEntryPath);
            Assert.False(Directory.Exists(Path.Combine(
                preview.InstallRoot,
                "cpython-3.13-windows-x86_64-none")));

            var invocation = Assert.Single(runner.Invocations);
            Assert.Equal("uv", invocation.Executable);
            Assert.Equal(
                [
                    "python",
                    "install",
                    "3.13.7",
                    "--install-dir",
                    preview.InstallRoot,
                    "--no-bin",
                    "--no-registry"
                ],
                invocation.Arguments);

            var installOperation = Assert.Single(
                journal.History
                    .Where(record =>
                        record.Id == preview.OperationId)
                    .GroupBy(record => record.Id)
                    .Select(group => group.Last()));
            Assert.Equal(
                OperationState.Succeeded,
                installOperation.State);
            Assert.Equal(
                [
                    OperationState.Draft,
                    OperationState.Validated,
                    OperationState.RecoveryReady,
                    OperationState.Executing,
                    OperationState.Verifying,
                    OperationState.Succeeded
                ],
                journal.History
                    .Where(record =>
                        record.Id == preview.OperationId)
                    .Select(record => record.State));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task InstallRuntimeAsync_rolls_back_new_directory_when_uv_install_fails()
    {
        var root = CreateTempRoot();
        try
        {
            var manifestStore = new InMemoryManifestStore();
            var journal = new RecordingOperationJournal();
            var recoveryStore = new RecordingRecoveryPointStore();
            var link = new RecordingActivationLink();
            var runner = new RecordingRuntimeCommandRunner(invocation =>
            {
                var installRoot = invocation.Arguments[
                    Array.IndexOf(
                        invocation.Arguments.ToArray(),
                        "--install-dir") + 1];
                Directory.CreateDirectory(installRoot);
                return new RuntimeCommandResult(
                    1,
                    string.Empty,
                    "download failed");
            });
            var manager = CreateManager(
                root,
                manifestStore,
                journal,
                recoveryStore,
                link,
                runner);

            var preview = await manager.PreviewRuntimeInstallAsync(
                "python",
                "3.13.7");
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => manager.InstallRuntimeAsync(preview));

            Assert.Contains("download failed", exception.Message);
            Assert.Null(await link.GetTargetAsync(
                preview.StableActivationPath));
            Assert.False(Directory.Exists(preview.InstallRoot));
            Assert.Empty(await manifestStore.ReadAllAsync());
            Assert.Equal(
                OperationState.RolledBack,
                FinalOperation(journal, preview.OperationId).State);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task InstallRuntimeAsync_restores_current_python_when_health_check_fails()
    {
        var root = CreateTempRoot();
        try
        {
            var manifestStore = new InMemoryManifestStore();
            var journal = new RecordingOperationJournal();
            var recoveryStore = new RecordingRecoveryPointStore();
            var link = new RecordingActivationLink();
            var provider = new TwoVersionTestProvider();
            var runner = new RecordingRuntimeCommandRunner(invocation =>
            {
                var executable = Path.Combine(
                    invocation.WorkingDirectory,
                    "payload",
                    "python.exe");
                Directory.CreateDirectory(
                    Path.GetDirectoryName(executable)!);
                File.WriteAllText(executable, string.Empty);
                return new RuntimeCommandResult(0, "installed", string.Empty);
            });
            var manager = CreateManager(
                root,
                manifestStore,
                journal,
                recoveryStore,
                link,
                runner,
                new QueuedRuntimeHealthCheck(true, false),
                provider);

            var firstPreview = await manager.PreviewRuntimeInstallAsync(
                provider.Descriptor.Id,
                "3.13.7");
            var first = await manager.InstallRuntimeAsync(firstPreview);
            var secondPreview = await manager.PreviewRuntimeInstallAsync(
                provider.Descriptor.Id,
                "3.13.8");

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => manager.InstallRuntimeAsync(secondPreview));

            Assert.Equal(
                first.Manifest.Location,
                await link.GetTargetAsync(
                    firstPreview.StableActivationPath));
            Assert.False(Directory.Exists(secondPreview.InstallRoot));
            var manifest = Assert.Single(
                await manifestStore.ReadAllAsync());
            Assert.Equal(first.Manifest.Identity, manifest.Identity);
            Assert.Equal(
                OperationState.RolledBack,
                FinalOperation(journal, secondPreview.OperationId).State);
            Assert.Contains(
                "健康检查失败",
                FinalOperation(journal, secondPreview.OperationId)
                    .FailureReason);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task InstallRuntimeAsync_preserves_new_runtime_when_restore_is_not_confirmed()
    {
        var root = CreateTempRoot();
        try
        {
            var manifestStore = new InMemoryManifestStore();
            var journal = new RecordingOperationJournal();
            var recoveryStore = new RecordingRecoveryPointStore();
            var link = new RecordingActivationLink();
            var provider = new TwoVersionTestProvider();
            var runner = new RecordingRuntimeCommandRunner(invocation =>
            {
                var executable = Path.Combine(
                    invocation.WorkingDirectory,
                    "payload",
                    "python.exe");
                Directory.CreateDirectory(
                    Path.GetDirectoryName(executable)!);
                File.WriteAllText(executable, string.Empty);
                return new RuntimeCommandResult(0, "installed", string.Empty);
            });
            var manager = CreateManager(
                root,
                manifestStore,
                journal,
                recoveryStore,
                link,
                runner,
                new QueuedRuntimeHealthCheck(true, false),
                provider);
            var firstPreview = await manager.PreviewRuntimeInstallAsync(
                provider.Descriptor.Id,
                "3.13.7");
            await manager.InstallRuntimeAsync(firstPreview);
            var secondPreview = await manager.PreviewRuntimeInstallAsync(
                provider.Descriptor.Id,
                "3.13.8");
            link.ThrowBeforeSetNumber = link.SetHistory.Count + 2;

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => manager.InstallRuntimeAsync(secondPreview));

            Assert.Contains(
                "未确认原激活版本已恢复",
                exception.Message,
                StringComparison.Ordinal);
            Assert.True(Directory.Exists(secondPreview.InstallRoot));
            Assert.Contains(
                await manifestStore.ReadAllAsync(),
                manifest => manifest.Fingerprint
                    == secondPreview.Fingerprint);
            Assert.Equal(
                OperationState.Failed,
                FinalOperation(journal, secondPreview.OperationId).State);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task InstallRuntimeAsync_activates_existing_runtime_after_location_changed()
    {
        var root = CreateTempRoot();
        try
        {
            var manifestStore = new InMemoryManifestStore();
            var journal = new RecordingOperationJournal();
            var recoveryStore = new RecordingRecoveryPointStore();
            var link = new RecordingActivationLink();
            var runner = new RecordingRuntimeCommandRunner(invocation =>
            {
                var executable = Path.Combine(
                    invocation.WorkingDirectory,
                    "cpython-3.13.7-windows-x86_64-none",
                    "python.exe");
                Directory.CreateDirectory(
                    Path.GetDirectoryName(executable)!);
                File.WriteAllText(executable, string.Empty);
                return new RuntimeCommandResult(0, "installed", string.Empty);
            });
            var manager = CreateManager(
                root,
                manifestStore,
                journal,
                recoveryStore,
                link,
                runner);
            var preview = await manager.PreviewRuntimeInstallAsync(
                "python",
                "3.13.7");
            var installed = await manager.InstallRuntimeAsync(preview);
            var migratedRoot = Path.Combine(root, "migrated", "3.13.7");
            Directory.CreateDirectory(
                Path.GetDirectoryName(migratedRoot)!);
            Directory.Move(preview.InstallRoot, migratedRoot);
            var migratedLocation = Path.Combine(
                migratedRoot,
                "cpython-3.13.7-windows-x86_64-none");
            await manifestStore.SaveAsync(
                installed.Manifest with { Location = migratedLocation });
            await link.SetTargetAsync(
                preview.StableActivationPath,
                migratedLocation);

            var existingPreview = await manager.PreviewRuntimeInstallAsync(
                "python",
                "3.13.7");
            var activated = await manager.InstallRuntimeAsync(
                existingPreview);

            Assert.True(existingPreview.IsAlreadyInstalled);
            Assert.Equal(migratedLocation, activated.Manifest.Location);
            Assert.Equal(
                migratedLocation,
                await link.GetTargetAsync(
                    preview.StableActivationPath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RollbackOperationAsync_removes_interrupted_install()
    {
        var root = CreateTempRoot();
        try
        {
            var manifestStore = new InMemoryManifestStore();
            var journal = new RecordingOperationJournal();
            var recoveryStore = new RecordingRecoveryPointStore();
            var link = new RecordingActivationLink();
            var manager = CreateManager(
                root,
                manifestStore,
                journal,
                recoveryStore,
                link);
            var preview = await manager.PreviewRuntimeInstallAsync(
                "python",
                "3.13.7");
            Directory.CreateDirectory(
                Path.GetDirectoryName(preview.ExecutablePath)!);
            File.WriteAllText(preview.ExecutablePath, string.Empty);
            var aliasTarget = Path.Combine(root, "alias-target");
            Directory.CreateDirectory(aliasTarget);
            File.WriteAllText(
                Path.Combine(aliasTarget, "marker.txt"),
                "target");
            await new WindowsJunctionActivationLink().SetTargetAsync(
                Path.Combine(preview.InstallRoot, "cpython-alias"),
                aliasTarget);
            await manifestStore.SaveAsync(new EnvironmentManifest(
                preview.Identity,
                preview.Fingerprint,
                EnvironmentAssetKind.ToolRuntime,
                "Python",
                "3.13.7",
                DiscoverySourceInfo.UvRuntime,
                preview.Location,
                preview.StableActivationPath,
                preview.Artifact.Sha256,
                preview.RecoveryPointId!,
                preview.OperationId!,
                IsSystemComponent: false,
                DateTimeOffset.UnixEpoch,
                preview.ActivationIdentity,
                preview.ManagedEntryPath));

            var rolledBack = await manager.RollbackOperationAsync(
                preview.OperationId!);

            Assert.Equal(OperationState.RolledBack, rolledBack.State);
            Assert.False(Directory.Exists(preview.InstallRoot));
            Assert.Empty(await manifestStore.ReadAllAsync());
            Assert.Null(await link.GetTargetAsync(
                preview.StableActivationPath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static OperationRecord FinalOperation(
        RecordingOperationJournal journal,
        string? operationId)
    {
        return Assert.Single(
            journal.History
                .Where(record => record.Id == operationId)
                .GroupBy(record => record.Id)
                .Select(group => group.Last()));
    }

    private static EnvironmentManager CreateManager(
        string root,
        InMemoryManifestStore manifestStore,
        RecordingOperationJournal journal,
        RecordingRecoveryPointStore recoveryStore,
        RecordingActivationLink link,
        RecordingRuntimeCommandRunner? runner = null,
        IRuntimeHealthCheck? healthCheck = null,
        IRuntimeProvider? provider = null)
    {
        return new EnvironmentManager(
            new StubEnvironmentProbe(
                new EnvironmentProbeResult([], [])),
            manifestStore: manifestStore,
            assetHasher: new FixedAssetHasher("asset-hash"),
            recoveryPointStore: recoveryStore,
            operationJournal: journal,
            activationLink: link,
            healthCheck: healthCheck
                ?? new RecordingRuntimeHealthCheck(isHealthy: true),
            managerPaths: ManagerPaths.Resolve(
                Path.Combine(root, "state")),
            runtimeRoot: Path.Combine(root, "runtimes"),
            runtimeCommandRunner: runner
                ?? new RecordingRuntimeCommandRunner(_ =>
                    new RuntimeCommandResult(0, string.Empty, string.Empty)),
            runtimeProviders: provider is null ? null : [provider]);
    }

    private static string CreateTempRoot()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"agent-env-manager-runtime-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
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

    private sealed class TwoVersionTestProvider : IRuntimeProvider
    {
        public RuntimeProviderDescriptor Descriptor { get; } = new(
            "test-python",
            "Python",
            EnvironmentAssetKind.ToolRuntime,
            RuntimeProviderMode.Installable,
            DiscoverySourceInfo.RuntimeProvider,
            "https://example.test/python",
            "PSF-2.0",
            RuntimeInstallStrategy.UvManagedDownload,
            [
                new RuntimeArtifactDescriptor(
                    "3.13.7",
                    "https://example.test/python/3.13.7",
                    "PSF-2.0",
                    "sha-3.13.7",
                    RuntimeInstallStrategy.UvManagedDownload,
                    ["win-x64"],
                    "https://example.test/python/3.13.7.tar.gz"),
                new RuntimeArtifactDescriptor(
                    "3.13.8",
                    "https://example.test/python/3.13.8",
                    "PSF-2.0",
                    "sha-3.13.8",
                    RuntimeInstallStrategy.UvManagedDownload,
                    ["win-x64"],
                    "https://example.test/python/3.13.8.tar.gz")
            ]);

        public RuntimeInstallCommand CreateInstallCommand(
            RuntimeInstallContext context)
        {
            return new RuntimeInstallCommand(
                "test-runtime",
                ["install", context.Artifact.Version],
                context.InstallRoot);
        }

        public string GetExecutableRelativePath(
            RuntimeArtifactDescriptor artifact)
        {
            return Path.Combine("payload", "python.exe");
        }
    }
}
