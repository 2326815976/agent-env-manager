using AgentEnvManager.Core.Activation;
using AgentEnvManager.Core.Adoption;
using AgentEnvManager.Core.Deletion;
using AgentEnvManager.Core.Inspection;
using AgentEnvManager.Core.Migrations;
using AgentEnvManager.Core.Operations;
using AgentEnvManager.Core.Tests.TestSupport;

namespace AgentEnvManager.Core.Tests.Deletion;

public sealed class DeletionTests
{
    [Fact]
    public async Task PreviewEnvironmentDeletionAsync_rejects_system_component()
    {
        var root = CreateTempRoot();
        try
        {
            var sourcePath = Path.Combine(root, "runtime");
            Directory.CreateDirectory(sourcePath);
            var manifestStore = new InMemoryManifestStore();
            var manifest = CreateManifest(sourcePath) with
            {
                IsSystemComponent = true
            };
            await manifestStore.SaveAsync(manifest);
            var manager = CreateManager(root, manifestStore);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => manager.PreviewEnvironmentDeletionAsync(
                    manifest.Fingerprint));

            Assert.Contains("系统组件", exception.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task QuarantineEnvironmentAsync_can_be_restored()
    {
        var root = CreateTempRoot();
        try
        {
            var sourcePath = Path.Combine(root, "runtime");
            Directory.CreateDirectory(sourcePath);
            File.WriteAllText(
                Path.Combine(sourcePath, "node.exe"),
                "node");
            var manifestStore = new InMemoryManifestStore();
            var manifest = CreateManifest(sourcePath);
            await manifestStore.SaveAsync(manifest);
            var activationLink = new RecordingActivationLink();
            await ConfigureActivationAsync(
                activationLink,
                manifest,
                sourcePath);
            var manager = CreateManager(
                root,
                manifestStore,
                activationLink);
            var preview = await manager.PreviewEnvironmentDeletionAsync(
                manifest.Fingerprint,
                ["全局包", "缓存"]);

            var operation = await manager.QuarantineEnvironmentAsync(preview);

            Assert.Equal(OperationState.Succeeded, operation.State);
            Assert.False(Directory.Exists(sourcePath));
            Assert.True(Directory.Exists(preview.QuarantinePath));
            Assert.Null(await activationLink.GetTargetAsync(
                manifest.StableActivationPath));
            Assert.Null(await manifestStore.FindByFingerprintAsync(
                manifest.Fingerprint));
            var quarantined = Assert.Single(
                await manager.ListQuarantinedEnvironmentsAsync());
            Assert.Contains("全局包", preview.Impact);
            Assert.Contains("缓存", preview.Impact);

            var restoreOperation = await manager.RestoreQuarantinedEnvironmentAsync(
                quarantined.Id);

            Assert.Equal(OperationState.Succeeded, restoreOperation.State);
            Assert.True(Directory.Exists(sourcePath));
            Assert.False(Directory.Exists(preview.QuarantinePath));
            Assert.NotNull(await manifestStore.FindByFingerprintAsync(
                manifest.Fingerprint));
            Assert.Equal(
                sourcePath,
                await activationLink.GetTargetAsync(
                    manifest.StableActivationPath));
            Assert.Empty(await manager.ListQuarantinedEnvironmentsAsync());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task PermanentDeleteAsync_requires_matching_confirmation()
    {
        var root = CreateTempRoot();
        try
        {
            var sourcePath = Path.Combine(root, "runtime");
            Directory.CreateDirectory(sourcePath);
            File.WriteAllText(
                Path.Combine(sourcePath, "node.exe"),
                "node");
            var manifestStore = new InMemoryManifestStore();
            var manifest = CreateManifest(sourcePath);
            await manifestStore.SaveAsync(manifest);
            var activationLink = new RecordingActivationLink();
            await ConfigureActivationAsync(
                activationLink,
                manifest,
                sourcePath);
            var manager = CreateManager(
                root,
                manifestStore,
                activationLink);
            var deletion = await manager.PreviewEnvironmentDeletionAsync(
                manifest.Fingerprint);
            await manager.QuarantineEnvironmentAsync(deletion);
            var quarantined = Assert.Single(
                await manager.ListQuarantinedEnvironmentsAsync());
            var preview = await manager.PreviewPermanentDeleteAsync(
                quarantined.Id);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => manager.PermanentDeleteAsync(
                    preview,
                    confirmed: false));

            Assert.True(Directory.Exists(preview.QuarantinePath));
            Assert.Single(await manager.ListQuarantinedEnvironmentsAsync());

            await manager.PermanentDeleteAsync(
                preview,
                confirmed: true);

            Assert.False(Directory.Exists(preview.QuarantinePath));
            Assert.Empty(await manager.ListQuarantinedEnvironmentsAsync());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task PreviewQuarantineRestoreAsync_creates_plan_before_write()
    {
        var root = CreateTempRoot();
        try
        {
            var sourcePath = Path.Combine(root, "runtime");
            Directory.CreateDirectory(sourcePath);
            File.WriteAllText(
                Path.Combine(sourcePath, "node.exe"),
                "node");
            var manifestStore = new InMemoryManifestStore();
            var manifest = CreateManifest(sourcePath);
            await manifestStore.SaveAsync(manifest);
            var activationLink = new RecordingActivationLink();
            await ConfigureActivationAsync(
                activationLink,
                manifest,
                sourcePath);
            var journal = new RecordingOperationJournal();
            var manager = CreateManager(
                root,
                manifestStore,
                activationLink,
                journal);
            var deletion = await manager.PreviewEnvironmentDeletionAsync(
                manifest.Fingerprint);
            await manager.QuarantineEnvironmentAsync(deletion);
            var quarantined = Assert.Single(
                await manager.ListQuarantinedEnvironmentsAsync());

            var restore = await manager.PreviewQuarantineRestoreAsync(
                quarantined.Id);

            var operation = await journal.GetAsync(restore.OperationId!);
            Assert.Equal(OperationState.RecoveryReady, operation!.State);
            Assert.False(Directory.Exists(sourcePath));
            Assert.True(Directory.Exists(quarantined.QuarantinePath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task PermanentDeleteAsync_rejects_modified_preview()
    {
        var root = CreateTempRoot();
        try
        {
            var sourcePath = Path.Combine(root, "runtime");
            Directory.CreateDirectory(sourcePath);
            File.WriteAllText(
                Path.Combine(sourcePath, "node.exe"),
                "node");
            var manifestStore = new InMemoryManifestStore();
            var manifest = CreateManifest(sourcePath);
            await manifestStore.SaveAsync(manifest);
            var activationLink = new RecordingActivationLink();
            await ConfigureActivationAsync(
                activationLink,
                manifest,
                sourcePath);
            var manager = CreateManager(
                root,
                manifestStore,
                activationLink);
            var deletion = await manager.PreviewEnvironmentDeletionAsync(
                manifest.Fingerprint);
            await manager.QuarantineEnvironmentAsync(deletion);
            var quarantined = Assert.Single(
                await manager.ListQuarantinedEnvironmentsAsync());
            var preview = await manager.PreviewPermanentDeleteAsync(
                quarantined.Id);
            var tampered = preview with { Impact = "伪造影响范围" };

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => manager.PermanentDeleteAsync(
                    tampered,
                    confirmed: true));

            Assert.Contains("预览已过期", exception.Message);
            Assert.True(Directory.Exists(quarantined.QuarantinePath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static EnvironmentManager CreateManager(
        string root,
        IEnvironmentManifestStore manifestStore,
        RecordingActivationLink? activationLink = null,
        RecordingOperationJournal? operationJournal = null)
    {
        return new EnvironmentManager(
            new StubEnvironmentProbe(
                new EnvironmentProbeResult([], [])),
            manifestStore: manifestStore,
            assetHasher: new FixedAssetHasher("asset-hash"),
            recoveryPointStore: new RecordingRecoveryPointStore(),
            operationJournal: operationJournal
                ?? new RecordingOperationJournal(),
            activationLink: activationLink ?? new RecordingActivationLink(),
            healthCheck: new RecordingRuntimeHealthCheck(isHealthy: true),
            environmentPathMover: new FileSystemEnvironmentPathMover(),
            quarantineStore: new FileEnvironmentQuarantineStore(
                Path.Combine(root, "quarantine")),
            runtimeStateCatalog: new StubRuntimeStateCatalog());
    }

    private static async Task ConfigureActivationAsync(
        RecordingActivationLink activationLink,
        EnvironmentManifest manifest,
        string targetPath)
    {
        await activationLink.SetTargetAsync(
            manifest.StableActivationPath,
            targetPath);
        await activationLink.SetTargetAsync(
            manifest.ManagedEntryPath,
            manifest.StableActivationPath);
    }

    private static EnvironmentManifest CreateManifest(string sourcePath)
    {
        return new EnvironmentManifest(
            new EnvironmentIdentity("node-runtime"),
            new EnvironmentFingerprint("node-runtime-fingerprint"),
            EnvironmentAssetKind.ToolRuntime,
            "Node.js",
            "24.1.0",
            DiscoverySourceInfo.PathCommand,
            sourcePath,
            @"C:\activation\node\current",
            "asset-hash",
            "recovery",
            "adopt",
            IsSystemComponent: false,
            DateTimeOffset.UnixEpoch,
            "node-activation",
            @"C:\shims\node");
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "AgentEnvManager.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private sealed class StubEnvironmentProbe(
        EnvironmentProbeResult result)
        : IEnvironmentProbe
    {
        public Task<EnvironmentProbeResult> ProbeAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(result);
        }
    }

    private sealed class StubRuntimeStateCatalog : IRuntimeStateCatalog
    {
        public Task<IReadOnlyList<string>> DescribeAsync(
            EnvironmentManifest manifest,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<string>>(
                ["全局包", "缓存"]);
        }
    }
}
