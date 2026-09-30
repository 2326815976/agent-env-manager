using System.Text.Json;
using AgentEnvManager.Core.Adoption;

namespace AgentEnvManager.Core.Storage;

public sealed class FileEnvironmentManifestStore(string manifestDirectory)
    : IEnvironmentManifestStore
{
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
                ManagerJson.Options)
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
            manifest = manifest with
            {
                Identity = existing.Identity,
                AdoptedAtUtc = existing.AdoptedAtUtc
            };
        }

        var path = Path.Combine(
            manifestDirectory,
            $"{manifest.Identity.Value}.json");
        await AtomicJsonFile.WriteAsync(
            path,
            manifest,
            cancellationToken);
        return manifest;
    }

    public Task DeleteAsync(
        EnvironmentIdentity identity,
        CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(
            manifestDirectory,
            $"{identity.Value}.json");
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
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
