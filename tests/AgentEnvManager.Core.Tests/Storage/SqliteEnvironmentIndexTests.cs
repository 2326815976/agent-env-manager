using AgentEnvManager.Core.Adoption;
using AgentEnvManager.Core.Inspection;
using AgentEnvManager.Core.Storage;
using Microsoft.Data.Sqlite;

namespace AgentEnvManager.Core.Tests.Storage;

public sealed class SqliteEnvironmentIndexTests
{
    [Fact]
    public async Task RebuildAsync_replaces_legacy_schema()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "AgentEnvManager.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var databasePath = Path.Combine(root, "environments.db");

        try
        {
            await using (var connection = new SqliteConnection(
                $"Data Source={databasePath}"))
            {
                await connection.OpenAsync();
                var command = connection.CreateCommand();
                command.CommandText =
                    """
                    CREATE TABLE environments (
                        identity TEXT PRIMARY KEY,
                        fingerprint TEXT NOT NULL UNIQUE,
                        kind TEXT NOT NULL,
                        name TEXT NOT NULL,
                        version TEXT NULL,
                        source_kind TEXT NOT NULL,
                        source_description TEXT NOT NULL,
                        location TEXT NOT NULL,
                        stable_activation_path TEXT NOT NULL,
                        asset_hash TEXT NOT NULL,
                        recovery_point_id TEXT NOT NULL,
                        is_system_component INTEGER NOT NULL,
                        adopted_at_utc TEXT NOT NULL
                    );
                    """;
                await command.ExecuteNonQueryAsync();
            }

            var index = new SqliteEnvironmentIndex(databasePath);
            await index.RebuildAsync([CreateManifest()]);

            Assert.Equal(1, await ReadOperationIdCountAsync(databasePath));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

    private static EnvironmentManifest CreateManifest()
    {
        return new EnvironmentManifest(
            new EnvironmentIdentity("identity-1"),
            new EnvironmentFingerprint("fingerprint-1"),
            EnvironmentAssetKind.ToolRuntime,
            "Node.js",
            "22.22.0",
            DiscoverySourceInfo.PathCommand,
            @"D:\Software\node",
            @"%LOCALAPPDATA%\AgentEnvManager\activations\identity-1\current",
            "asset-hash",
            "recovery-1",
            "operation-1",
            IsSystemComponent: false,
            DateTimeOffset.UnixEpoch);
    }

    private static async Task<int> ReadOperationIdCountAsync(string databasePath)
    {
        await using var connection = new SqliteConnection(
            $"Data Source={databasePath}");
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText =
            "SELECT COUNT(*) FROM environments WHERE operation_id = 'operation-1';";
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }
}
