using AgentEnvManager.Core.Adoption;
using AgentEnvManager.Core.Inspection;
using AgentEnvManager.Core.Operations;
using AgentEnvManager.Core.Storage;
using Microsoft.Data.Sqlite;

namespace AgentEnvManager.Core.Tests.Adoption;

public sealed class OperationRollbackPersistenceTests
{
    [Fact]
    public async Task RollbackOperationAsync_restores_manifest_and_index_after_restart()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "AgentEnvManager.Tests",
            Guid.NewGuid().ToString("N"));
        var manifestDirectory = Path.Combine(root, "manifests");
        var recoveryDirectory = Path.Combine(root, "recovery");
        var operationDirectory = Path.Combine(root, "operations");
        var databasePath = Path.Combine(root, "index", "environments.db");
        Directory.CreateDirectory(root);

        try
        {
            var manifestStore = new FileEnvironmentManifestStore(
                manifestDirectory);
            var recoveryStore = new FileEnvironmentRecoveryPointStore(
                recoveryDirectory);
            var journal = new FileOperationJournal(operationDirectory);
            var index = new SqliteEnvironmentIndex(databasePath);
            var firstManager = CreateManager(
                root,
                manifestStore,
                index,
                recoveryStore,
                journal);
            var inventory = await firstManager.InspectAsync();
            var observed = Assert.Single(inventory.Environments);
            var preview = await firstManager.PreviewAdoptionAsync(
                observed.Fingerprint);
            var recoveryPoint = await recoveryStore.CreateAsync(
                preview,
                existingManifest: null);
            var operation = new OperationRecord(
                "operation-1",
                OperationType.Adopt,
                OperationState.Executing,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                "模拟中断的纳管操作",
                recoveryPoint.Id);
            await journal.SaveAsync(operation);
            await manifestStore.SaveAsync(new EnvironmentManifest(
                preview.ProposedIdentity,
                preview.Fingerprint,
                preview.Asset.Kind,
                preview.Asset.Name,
                preview.Asset.Version,
                preview.Asset.Source,
                preview.Asset.Location,
                preview.StableActivationPath,
                preview.AssetHash,
                recoveryPoint.Id,
                operation.Id,
                preview.Asset.IsSystemComponent,
                DateTimeOffset.UtcNow));
            await index.RebuildAsync(await manifestStore.ReadAllAsync());

            var restartedManager = CreateManager(
                root,
                manifestStore,
                index,
                recoveryStore,
                journal);
            var rolledBack = await restartedManager.RollbackOperationAsync(
                operation.Id);

            Assert.Equal(OperationState.RolledBack, rolledBack.State);
            Assert.Empty(await manifestStore.ReadAllAsync());
            Assert.Equal(0, await CountRowsAsync(databasePath));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

    private static EnvironmentManager CreateManager(
        string assetPath,
        IEnvironmentManifestStore manifestStore,
        IEnvironmentIndex index,
        IEnvironmentRecoveryPointStore recoveryStore,
        IOperationJournal journal)
    {
        return new EnvironmentManager(
            new DirectoryEnvironmentProbe(assetPath),
            manifestStore: manifestStore,
            index: index,
            recoveryPointStore: recoveryStore,
            operationJournal: journal);
    }

    private static async Task<int> CountRowsAsync(string databasePath)
    {
        await using var connection = new SqliteConnection(
            $"Data Source={databasePath}");
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM environments;";
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private sealed class DirectoryEnvironmentProbe(string path)
        : IEnvironmentProbe
    {
        public Task<EnvironmentProbeResult> ProbeAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new EnvironmentProbeResult(
                [
                    new EnvironmentAsset(
                        EnvironmentAssetKind.AgentConfiguration,
                        "Test",
                        Version: null,
                        path,
                        IsSystemComponent: false,
                        DiscoverySourceInfo.AgentConfiguration)
                ],
                []));
        }
    }
}
