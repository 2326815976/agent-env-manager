using System.Text.Json;
using System.Text.Json.Serialization;
using AgentEnvManager.Core.Adoption;

namespace AgentEnvManager.Core.Storage;

public sealed class FileEnvironmentManifestStore(string manifestDirectory)
    : IEnvironmentManifestStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters =
        {
            new JsonStringEnumConverter()
        }
    };

    public async Task<IReadOnlyList<EnvironmentManifest>> ReadAllAsync(
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(manifestDirectory))
        {
            return [];
        }

        var manifests = new List<EnvironmentManifest>();
        foreach (var path in Directory.EnumerateFiles(
            manifestDirectory,
            "*.json",
            SearchOption.TopDirectoryOnly))
        {
            var json = await File.ReadAllTextAsync(path, cancellationToken);
            var manifest = JsonSerializer.Deserialize<EnvironmentManifest>(
                json,
                JsonOptions)
                ?? throw new InvalidDataException($"无法读取 manifest: {path}");
            manifests.Add(manifest);
        }

        return manifests
            .OrderBy(manifest => manifest.Identity.Value, StringComparer.Ordinal)
            .ToArray();
    }

    public async Task<EnvironmentManifest?> FindByFingerprintAsync(
        EnvironmentFingerprint fingerprint,
        CancellationToken cancellationToken = default)
    {
        var manifests = await ReadAllAsync(cancellationToken);
        return manifests.FirstOrDefault(manifest =>
            manifest.Fingerprint == fingerprint);
    }

    public async Task<EnvironmentManifest> SaveAsync(
        EnvironmentManifest manifest,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(manifestDirectory);
        var lockPath = Path.Combine(manifestDirectory, ".adoption.lock");
        await using var lockStream = await AcquireLockAsync(
            lockPath,
            cancellationToken);
        var existing = await FindByFingerprintAsync(
            manifest.Fingerprint,
            cancellationToken);
        if (existing is not null)
        {
            if (existing.AssetHash == manifest.AssetHash)
            {
                return existing;
            }

            manifest = manifest with
            {
                Identity = existing.Identity,
                AdoptedAtUtc = existing.AdoptedAtUtc
            };
        }

        var path = Path.Combine(
            manifestDirectory,
            $"{manifest.Identity.Value}.json");
        var temporaryPath = $"{path}.tmp";
        var json = JsonSerializer.Serialize(manifest, JsonOptions);
        await File.WriteAllTextAsync(
            temporaryPath,
            json,
            cancellationToken);
        File.Move(temporaryPath, path, overwrite: true);
        return manifest;
    }

    private static async Task<FileStream> AcquireLockAsync(
        string path,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(
                    path,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 1,
                    FileOptions.DeleteOnClose);
            }
            catch (IOException) when (attempt < 40)
            {
                await Task.Delay(50, cancellationToken);
            }
        }
    }
}
