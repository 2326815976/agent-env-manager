using AgentEnvManager.Core.Storage;

namespace AgentEnvManager.Core.Deletion;

public sealed class FileEnvironmentQuarantineStore(string rootDirectory)
    : IEnvironmentQuarantineStore
{
    public async Task<IReadOnlyList<QuarantinedEnvironment>> ReadAllAsync(
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(rootDirectory))
        {
            return [];
        }

        var entries = new List<QuarantinedEnvironment>();
        foreach (var path in Directory.EnumerateFiles(
                     rootDirectory,
                     "*.json",
                     SearchOption.TopDirectoryOnly))
        {
            entries.Add(await ReadAsync(path, cancellationToken));
        }

        return entries
            .OrderByDescending(entry => entry.QuarantinedAtUtc)
            .ToArray();
    }

    public async Task<QuarantinedEnvironment?> GetAsync(
        string quarantineId,
        CancellationToken cancellationToken = default)
    {
        var path = GetMetadataPath(quarantineId);
        return File.Exists(path)
            ? await ReadAsync(path, cancellationToken)
            : null;
    }

    public async Task<QuarantinedEnvironment> SaveAsync(
        QuarantinedEnvironment entry,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(rootDirectory);
        await AtomicJsonFile.WriteAsync(
            GetMetadataPath(entry.Id),
            entry,
            cancellationToken);
        return entry;
    }

    public Task DeleteAsync(
        string quarantineId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = GetMetadataPath(quarantineId);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    public string CreatePath(string identity)
    {
        return Path.Combine(
            rootDirectory,
            "assets",
            $"{identity}-{Guid.NewGuid():N}");
    }

    private string GetMetadataPath(string quarantineId)
    {
        return Path.Combine(rootDirectory, $"{quarantineId}.json");
    }

    private static async Task<QuarantinedEnvironment> ReadAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var json = await File.ReadAllTextAsync(path, cancellationToken);
        return ManagerJson.Deserialize<QuarantinedEnvironment>(json)
            ?? throw new InvalidDataException(
                $"无法读取隔离记录: {path}");
    }
}
