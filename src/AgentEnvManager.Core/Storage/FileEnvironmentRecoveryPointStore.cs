using System.Text.Json;
using AgentEnvManager.Core.Adoption;

namespace AgentEnvManager.Core.Storage;

public sealed class FileEnvironmentRecoveryPointStore(string recoveryDirectory)
    : IEnvironmentRecoveryPointStore
{
    public async Task<AdoptionRecoveryPoint> CreateAsync(
        AdoptionPreview preview,
        EnvironmentManifest? existingManifest,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(recoveryDirectory);
        var point = new AdoptionRecoveryPoint(
            Guid.NewGuid().ToString("N"),
            preview.Fingerprint,
            existingManifest?.Identity,
            existingManifest,
            "纳管前记录 manifest 变更，原始环境文件不会被修改。",
            DateTimeOffset.UtcNow);
        var path = Path.Combine(recoveryDirectory, $"{point.Id}.json");
        await AtomicJsonFile.WriteAsync(
            path,
            point,
            cancellationToken);
        return point;
    }

    public async Task<AdoptionRecoveryPoint?> GetAsync(
        string recoveryPointId,
        CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(recoveryDirectory, $"{recoveryPointId}.json");
        if (!File.Exists(path))
        {
            return null;
        }

        var json = await File.ReadAllTextAsync(path, cancellationToken);
        return JsonSerializer.Deserialize<AdoptionRecoveryPoint>(
            json,
            ManagerJson.Options);
    }
}
