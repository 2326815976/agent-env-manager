using System.Text.Json;
using System.Text.Json.Serialization;
using AgentEnvManager.Core.Adoption;

namespace AgentEnvManager.Core.Storage;

public sealed class FileEnvironmentRecoveryPointStore(string recoveryDirectory)
    : IEnvironmentRecoveryPointStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters =
        {
            new JsonStringEnumConverter()
        }
    };

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
            "纳管前记录 manifest 变更，原始环境文件不会被修改。",
            DateTimeOffset.UtcNow);
        var path = Path.Combine(recoveryDirectory, $"{point.Id}.json");
        var json = JsonSerializer.Serialize(point, JsonOptions);
        await File.WriteAllTextAsync(path, json, cancellationToken);
        return point;
    }
}
