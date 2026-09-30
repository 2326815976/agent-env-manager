using AgentEnvManager.Core.Adoption;
using Microsoft.Data.Sqlite;

namespace AgentEnvManager.Core.Storage;

public sealed class SqliteEnvironmentIndex(string databasePath)
    : IEnvironmentIndex
{
    public async Task RebuildAsync(
        IReadOnlyList<EnvironmentManifest> manifests,
        CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var connection = new SqliteConnection(
            $"Data Source={databasePath}");
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            cancellationToken);

        var createCommand = connection.CreateCommand();
        createCommand.Transaction = (SqliteTransaction)transaction;
        createCommand.CommandText =
            """
            CREATE TABLE IF NOT EXISTS environments (
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
            DELETE FROM environments;
            """;
        await createCommand.ExecuteNonQueryAsync(cancellationToken);

        foreach (var manifest in manifests)
        {
            var insertCommand = connection.CreateCommand();
            insertCommand.Transaction = (SqliteTransaction)transaction;
            insertCommand.CommandText =
                """
                INSERT INTO environments (
                    identity,
                    fingerprint,
                    kind,
                    name,
                    version,
                    source_kind,
                    source_description,
                    location,
                    stable_activation_path,
                    asset_hash,
                    recovery_point_id,
                    is_system_component,
                    adopted_at_utc
                )
                VALUES (
                    $identity,
                    $fingerprint,
                    $kind,
                    $name,
                    $version,
                    $source_kind,
                    $source_description,
                    $location,
                    $stable_activation_path,
                    $asset_hash,
                    $recovery_point_id,
                    $is_system_component,
                    $adopted_at_utc
                );
                """;
            insertCommand.Parameters.AddWithValue("$identity", manifest.Identity.Value);
            insertCommand.Parameters.AddWithValue(
                "$fingerprint",
                manifest.Fingerprint.Value);
            insertCommand.Parameters.AddWithValue("$kind", manifest.Kind.ToString());
            insertCommand.Parameters.AddWithValue("$name", manifest.Name);
            insertCommand.Parameters.AddWithValue(
                "$version",
                (object?)manifest.Version ?? DBNull.Value);
            insertCommand.Parameters.AddWithValue(
                "$source_kind",
                manifest.Source.Kind.ToString());
            insertCommand.Parameters.AddWithValue(
                "$source_description",
                manifest.Source.Description);
            insertCommand.Parameters.AddWithValue("$location", manifest.Location);
            insertCommand.Parameters.AddWithValue(
                "$stable_activation_path",
                manifest.StableActivationPath);
            insertCommand.Parameters.AddWithValue("$asset_hash", manifest.AssetHash);
            insertCommand.Parameters.AddWithValue(
                "$recovery_point_id",
                manifest.RecoveryPointId);
            insertCommand.Parameters.AddWithValue(
                "$is_system_component",
                manifest.IsSystemComponent ? 1 : 0);
            insertCommand.Parameters.AddWithValue(
                "$adopted_at_utc",
                manifest.AdoptedAtUtc.ToUniversalTime().ToString("O"));
            await insertCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }
}
