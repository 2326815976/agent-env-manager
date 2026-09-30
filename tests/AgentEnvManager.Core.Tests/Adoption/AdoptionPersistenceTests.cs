using AgentEnvManager.Core.Adoption;
using AgentEnvManager.Core.Inspection;
using AgentEnvManager.Core.Storage;
using AgentEnvManager.Core.Tests.TestSupport;
using Microsoft.Data.Sqlite;

namespace AgentEnvManager.Core.Tests.Adoption;

public sealed class AdoptionPersistenceTests
{
    [Fact]
    public async Task RebuildEnvironmentIndexAsync_rebuilds_sqlite_index_from_manifests()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "AgentEnvManager.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var store = new FileEnvironmentManifestStore(
                Path.Combine(root, "manifests"));
            var databasePath = Path.Combine(
                root,
                "index",
                "environments.db");
            var index = new SqliteEnvironmentIndex(databasePath);
            var manager = new EnvironmentManager(
                new StubEnvironmentProbe(),
                manifestStore: store,
                index: index,
                assetHasher: new FixedAssetHasher("asset-hash"),
                recoveryPointStore: new FileEnvironmentRecoveryPointStore(
                    Path.Combine(root, "recovery")),
                activationPathFactory: new FixedActivationPathFactory());

            var inventory = await manager.InspectAsync();
            var observed = Assert.Single(inventory.Environments);
            await manager.AdoptAsync(observed.Fingerprint);
            await index.RebuildAsync([]);
            Assert.Equal(0, await CountRowsAsync(databasePath));

            var rebuiltCount = await manager.RebuildEnvironmentIndexAsync();

            Assert.Equal(1, rebuiltCount);
            Assert.Equal(1, await CountRowsAsync(databasePath));
            var manifest = Assert.Single(await store.ReadAllAsync());
            Assert.Equal("asset-hash", manifest.AssetHash);
            Assert.False(string.IsNullOrWhiteSpace(manifest.RecoveryPointId));
            Assert.Single(Directory.EnumerateFiles(
                Path.Combine(root, "recovery"),
                "*.json"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class StubEnvironmentProbe : IEnvironmentProbe
    {
        public Task<EnvironmentProbeResult> ProbeAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new EnvironmentProbeResult(
                [
                    new EnvironmentAsset(
                        EnvironmentAssetKind.ToolRuntime,
                        "Node.js",
                        "22.22.0",
                        @"D:\Software\node",
                        IsSystemComponent: false,
                        Source: DiscoverySourceInfo.PathCommand)
                ],
                []));
        }
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

}
