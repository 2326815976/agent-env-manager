using System.Text.Json;
using AgentEnvManager.Core.Storage;

namespace AgentEnvManager.Core.EnvironmentVariables;

public sealed class FileEnvironmentVariableRecoveryPointStore(
    string recoveryDirectory)
    : IEnvironmentVariableRecoveryPointStore
{
    public async Task<EnvironmentVariableRecoveryPoint> CreateAsync(
        string operationId,
        IReadOnlyDictionary<string, string?> originalValues,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(recoveryDirectory);
        var point = new EnvironmentVariableRecoveryPoint(
            Guid.NewGuid().ToString("N"),
            new Dictionary<string, string?>(
                originalValues,
                StringComparer.OrdinalIgnoreCase),
            DateTimeOffset.UtcNow);
        var path = Path.Combine(recoveryDirectory, $"{point.Id}.json");
        await AtomicJsonFile.WriteAsync(path, point, cancellationToken);
        return point;
    }

    public async Task<EnvironmentVariableRecoveryPoint?> GetAsync(
        string recoveryPointId,
        CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(recoveryDirectory, $"{recoveryPointId}.json");
        if (!File.Exists(path))
        {
            return null;
        }

        var json = await File.ReadAllTextAsync(path, cancellationToken);
        return JsonSerializer.Deserialize<EnvironmentVariableRecoveryPoint>(
            json,
            ManagerJson.Options);
    }
}
