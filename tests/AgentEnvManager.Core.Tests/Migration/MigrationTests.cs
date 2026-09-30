using AgentEnvManager.Core.Adoption;
using AgentEnvManager.Core.Activation;
using AgentEnvManager.Core.Inspection;
using AgentEnvManager.Core.Migrations;
using AgentEnvManager.Core.Operations;
using AgentEnvManager.Core.Storage;
using AgentEnvManager.Core.Tests.TestSupport;

namespace AgentEnvManager.Core.Tests.Migration;

public sealed class MigrationTests
{
    [Fact]
    public async Task PreviewMigrationAsync_rejects_occupied_environment()
    {
        var root = CreateTempRoot();
        try
        {
            var sourcePath = Path.Combine(root, "runtime");
            var destinationPath = Path.Combine(root, "moved", "runtime");
            Directory.CreateDirectory(sourcePath);
            var manifestStore = new InMemoryManifestStore();
            var manifest = CreateManifest(sourcePath);
            await manifestStore.SaveAsync(manifest);
            var activationLink = new RecordingActivationLink();
            await ConfigureActivationAsync(
                activationLink,
                manifest,
                sourcePath);
            var manager = CreateManager(
                manifestStore,
                new StubMigrationOccupancyProbe(
                    [
                        new MigrationBlocker(
                            "file-lock",
                            "文件被占用",
                            Path.Combine(sourcePath, "node.exe"))
                    ]),
                activationLink);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => manager.PreviewMigrationAsync(
                    manifest.Fingerprint,
                    destinationPath));

            Assert.Contains("占用", exception.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task MigrateEnvironmentAsync_moves_same_volume_and_updates_activation()
    {
        var root = CreateTempRoot();
        try
        {
            var sourcePath = Path.Combine(root, "runtime");
            var destinationPath = Path.Combine(root, "moved", "runtime");
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
                manifestStore,
                new StubMigrationOccupancyProbe([]),
                activationLink);

            var preview = await manager.PreviewMigrationAsync(
                manifest.Fingerprint,
                destinationPath);
            var operation = await manager.MigrateEnvironmentAsync(preview);

            Assert.Equal(OperationState.Succeeded, operation.State);
            Assert.False(Directory.Exists(sourcePath));
            Assert.True(Directory.Exists(destinationPath));
            Assert.True(File.Exists(
                Path.Combine(destinationPath, "node.exe")));
            Assert.Equal(
                destinationPath,
                (await manifestStore.FindByFingerprintAsync(
                    manifest.Fingerprint))!.Location);
            Assert.Equal(
                destinationPath,
                await activationLink.GetTargetAsync(
                    manifest.StableActivationPath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task MigrateEnvironmentAsync_restores_source_when_health_fails()
    {
        var root = CreateTempRoot();
        try
        {
            var sourcePath = Path.Combine(root, "runtime");
            var destinationPath = Path.Combine(root, "moved", "runtime");
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
                manifestStore,
                new StubMigrationOccupancyProbe([]),
                activationLink,
                new RecordingRuntimeHealthCheck(isHealthy: false),
                journal);

            var preview = await manager.PreviewMigrationAsync(
                manifest.Fingerprint,
                destinationPath);
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => manager.MigrateEnvironmentAsync(preview));

            Assert.Contains("已恢复原激活路径", exception.Message);
            Assert.True(Directory.Exists(sourcePath));
            Assert.False(Directory.Exists(destinationPath));
            Assert.Equal(
                sourcePath,
                (await manifestStore.FindByFingerprintAsync(
                    manifest.Fingerprint))!.Location);
            Assert.Equal(
                sourcePath,
                await activationLink.GetTargetAsync(
                    manifest.StableActivationPath));
            Assert.Equal(
                OperationState.RolledBack,
                journal.History
                    .Where(record => record.Id == preview.OperationId)
                    .Last()
                    .State);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task MigrateEnvironmentAsync_rejects_tampered_destination()
    {
        var root = CreateTempRoot();
        try
        {
            var sourcePath = Path.Combine(root, "runtime");
            var destinationPath = Path.Combine(root, "moved", "runtime");
            Directory.CreateDirectory(sourcePath);
            var manifestStore = new InMemoryManifestStore();
            var manifest = CreateManifest(sourcePath);
            await manifestStore.SaveAsync(manifest);
            var activationLink = new RecordingActivationLink();
            await ConfigureActivationAsync(
                activationLink,
                manifest,
                sourcePath);
            var manager = CreateManager(
                manifestStore,
                new StubMigrationOccupancyProbe([]),
                activationLink);
            var preview = await manager.PreviewMigrationAsync(
                manifest.Fingerprint,
                destinationPath);
            var tampered = preview with
            {
                DestinationPath = Path.Combine(root, "not-reviewed")
            };

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => manager.MigrateEnvironmentAsync(tampered));

            Assert.Contains("迁移计划已过期", exception.Message);
            Assert.True(Directory.Exists(sourcePath));
            Assert.False(Directory.Exists(tampered.DestinationPath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task MigrateEnvironmentAsync_restores_previous_activation_target()
    {
        var root = CreateTempRoot();
        try
        {
            var sourcePath = Path.Combine(root, "runtime");
            var previousTarget = Path.Combine(root, "active-runtime");
            var destinationPath = Path.Combine(root, "moved", "runtime");
            Directory.CreateDirectory(sourcePath);
            Directory.CreateDirectory(previousTarget);
            var manifestStore = new InMemoryManifestStore();
            var manifest = CreateManifest(sourcePath);
            await manifestStore.SaveAsync(manifest);
            var activationLink = new RecordingActivationLink();
            await ConfigureActivationAsync(
                activationLink,
                manifest,
                previousTarget);
            var manager = CreateManager(
                manifestStore,
                new StubMigrationOccupancyProbe([]),
                activationLink,
                new RecordingRuntimeHealthCheck(isHealthy: false));
            var preview = await manager.PreviewMigrationAsync(
                manifest.Fingerprint,
                destinationPath);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => manager.MigrateEnvironmentAsync(preview));

            Assert.Equal(
                previousTarget,
                await activationLink.GetTargetAsync(
                    manifest.StableActivationPath));
            Assert.True(Directory.Exists(sourcePath));
            Assert.False(Directory.Exists(destinationPath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task MigrateEnvironmentAsync_keeps_non_active_version_inactive()
    {
        var root = CreateTempRoot();
        try
        {
            var sourcePath = Path.Combine(root, "runtime");
            var previousTarget = Path.Combine(root, "active-runtime");
            var destinationPath = Path.Combine(root, "moved", "runtime");
            Directory.CreateDirectory(sourcePath);
            Directory.CreateDirectory(previousTarget);
            var manifestStore = new InMemoryManifestStore();
            var manifest = CreateManifest(sourcePath);
            await manifestStore.SaveAsync(manifest);
            var activationLink = new RecordingActivationLink();
            await ConfigureActivationAsync(
                activationLink,
                manifest,
                previousTarget);
            var manager = CreateManager(
                manifestStore,
                new StubMigrationOccupancyProbe([]),
                activationLink);

            var preview = await manager.PreviewMigrationAsync(
                manifest.Fingerprint,
                destinationPath);
            var operation = await manager.MigrateEnvironmentAsync(preview);

            Assert.Equal(OperationState.Succeeded, operation.State);
            Assert.Equal(
                previousTarget,
                await activationLink.GetTargetAsync(
                    manifest.StableActivationPath));
            Assert.Equal(
                destinationPath,
                (await manifestStore.FindByFingerprintAsync(
                    manifest.Fingerprint))!.Location);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task MigrateEnvironmentAsync_rejects_activation_target_drift()
    {
        var root = CreateTempRoot();
        try
        {
            var sourcePath = Path.Combine(root, "runtime");
            var destinationPath = Path.Combine(root, "moved", "runtime");
            var otherTarget = Path.Combine(root, "other-runtime");
            Directory.CreateDirectory(sourcePath);
            Directory.CreateDirectory(otherTarget);
            var manifestStore = new InMemoryManifestStore();
            var manifest = CreateManifest(sourcePath);
            await manifestStore.SaveAsync(manifest);
            var activationLink = new RecordingActivationLink();
            await ConfigureActivationAsync(
                activationLink,
                manifest,
                sourcePath);
            var manager = CreateManager(
                manifestStore,
                new StubMigrationOccupancyProbe([]),
                activationLink);
            var preview = await manager.PreviewMigrationAsync(
                manifest.Fingerprint,
                destinationPath);
            await ConfigureActivationAsync(
                activationLink,
                manifest,
                otherTarget);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => manager.MigrateEnvironmentAsync(preview));

            Assert.Contains("迁移计划已过期", exception.Message);
            Assert.True(Directory.Exists(sourcePath));
            Assert.False(Directory.Exists(destinationPath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task WindowsMigrationOccupancyProbe_reports_locked_file()
    {
        var root = CreateTempRoot();
        try
        {
            var filePath = Path.Combine(root, "node.exe");
            await File.WriteAllTextAsync(filePath, "node");
            await using var locked = File.Open(
                filePath,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.None);

            var blockers = await new WindowsMigrationOccupancyProbe()
                .FindBlockersAsync(
                    root,
                    Path.Combine(root, "moved"));

            var blocker = Assert.Single(
                blockers,
                item => item.Path == filePath);
            Assert.Equal("file-lock", blocker.Code);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void FileSystemEnvironmentPathMover_rejects_cross_volume_paths()
    {
        var mover = new FileSystemEnvironmentPathMover();

        var exception = Assert.Throws<InvalidOperationException>(
            () => mover.ValidateSameVolume(
                @"C:\source\runtime",
                @"D:\destination\runtime"));

        Assert.Contains("同一卷", exception.Message);
    }

    [Fact]
    public async Task RollbackOperationAsync_restores_interrupted_migration()
    {
        var root = CreateTempRoot();
        try
        {
            var sourcePath = Path.Combine(root, "runtime");
            var destinationPath = Path.Combine(root, "moved", "runtime");
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
                manifestStore,
                new StubMigrationOccupancyProbe([]),
                activationLink,
                operationJournal: journal);
            var preview = await manager.PreviewMigrationAsync(
                manifest.Fingerprint,
                destinationPath);
            var mover = new FileSystemEnvironmentPathMover();
            await mover.MoveAsync(sourcePath, destinationPath);
            await ConfigureActivationAsync(
                activationLink,
                manifest,
                destinationPath);
            await manifestStore.SaveAsync(
                manifest with { Location = destinationPath });
            var current = await journal.GetAsync(preview.OperationId!);
            await journal.SaveAsync(current! with
            {
                State = OperationState.Verifying
            });

            var rolledBack = await manager.RollbackOperationAsync(
                preview.OperationId!);

            Assert.Equal(OperationState.RolledBack, rolledBack.State);
            Assert.True(Directory.Exists(sourcePath));
            Assert.False(Directory.Exists(destinationPath));
            Assert.Equal(
                sourcePath,
                (await manifestStore.FindByFingerprintAsync(
                    manifest.Fingerprint))!.Location);
            Assert.Equal(
                sourcePath,
                await activationLink.GetTargetAsync(
                    manifest.StableActivationPath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task PreviewRollbackAsync_describes_migration_reversal()
    {
        var root = CreateTempRoot();
        try
        {
            var sourcePath = Path.Combine(root, "runtime");
            var destinationPath = Path.Combine(root, "moved", "runtime");
            Directory.CreateDirectory(sourcePath);
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
                manifestStore,
                new StubMigrationOccupancyProbe([]),
                activationLink,
                operationJournal: journal);
            var preview = await manager.PreviewMigrationAsync(
                manifest.Fingerprint,
                destinationPath);
            var operation = await journal.GetAsync(preview.OperationId!);
            await journal.SaveAsync(operation! with
            {
                State = OperationState.Failed
            });

            var rollback = await manager.PreviewRollbackAsync(
                preview.OperationId!);

            Assert.Equal(sourcePath, rollback.Target);
            Assert.Contains(destinationPath, rollback.Impact);
            Assert.Contains(sourcePath, rollback.Impact);
            Assert.Equal(preview.RecoveryPointId, rollback.RecoveryPointId);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task FileEnvironmentManifestStore_persists_location_change()
    {
        var root = CreateTempRoot();
        try
        {
            var store = new FileEnvironmentManifestStore(
                Path.Combine(root, "manifests"));
            var manifest = CreateManifest(
                Path.Combine(root, "runtime"));

            await store.SaveAsync(manifest);
            var moved = manifest with
            {
                Location = Path.Combine(root, "moved-runtime")
            };
            await store.SaveAsync(moved);

            var persisted = await store.FindByFingerprintAsync(
                manifest.Fingerprint);
            Assert.Equal(moved.Location, persisted!.Location);
            Assert.Equal(manifest.AssetHash, persisted.AssetHash);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static EnvironmentManager CreateManager(
        IEnvironmentManifestStore manifestStore,
        IMigrationOccupancyProbe occupancyProbe,
        RecordingActivationLink? activationLink = null,
        RecordingRuntimeHealthCheck? healthCheck = null,
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
            healthCheck: healthCheck
                ?? new RecordingRuntimeHealthCheck(isHealthy: true),
            migrationOccupancyProbe: occupancyProbe,
            environmentPathMover: new FileSystemEnvironmentPathMover());
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

    private sealed class StubMigrationOccupancyProbe(
        IReadOnlyList<MigrationBlocker> blockers)
        : IMigrationOccupancyProbe
    {
        public Task<IReadOnlyList<MigrationBlocker>> FindBlockersAsync(
            string sourcePath,
            string destinationPath,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(blockers);
        }
    }
}
