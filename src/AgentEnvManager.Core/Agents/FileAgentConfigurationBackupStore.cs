using System.Text.Json;
using AgentEnvManager.Core.Storage;

namespace AgentEnvManager.Core.Agents;

public sealed class FileAgentConfigurationBackupStore(string backupRoot)
    : IAgentConfigurationBackupStore
{
    public async Task<AgentConfigurationRecoveryPoint> CreateAsync(
        string agentName,
        IReadOnlyList<string> sourcePaths,
        CancellationToken cancellationToken = default)
    {
        var pointId = Guid.NewGuid().ToString("N");
        var directory = Path.Combine(backupRoot, pointId);
        Directory.CreateDirectory(directory);
        var entries = new List<AgentConfigurationBackupEntry>();

        for (var index = 0; index < sourcePaths.Count; index++)
        {
            var sourcePath = Path.GetFullPath(sourcePaths[index]);
            var exists = File.Exists(sourcePath);
            var backupPath = Path.Combine(directory, $"{index}.bak");
            if (exists)
            {
                await using var source = File.OpenRead(sourcePath);
                await using var target = File.Create(backupPath);
                await source.CopyToAsync(target, cancellationToken);
            }

            entries.Add(new AgentConfigurationBackupEntry(
                sourcePath,
                backupPath,
                exists));
        }

        var point = new AgentConfigurationRecoveryPoint(
            pointId,
            agentName,
            entries,
            DateTimeOffset.UtcNow);
        var pointPath = Path.Combine(directory, "recovery-point.json");
        await AtomicJsonFile.WriteAsync(pointPath, point, cancellationToken);
        return point;
    }

    public async Task<AgentConfigurationRecoveryPoint?> GetAsync(
        string recoveryPointId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(recoveryPointId)
            || recoveryPointId.Contains(
                Path.DirectorySeparatorChar)
            || recoveryPointId.Contains(
                Path.AltDirectorySeparatorChar)
            || recoveryPointId.Contains("..", StringComparison.Ordinal))
        {
            return null;
        }

        var pointPath = Path.Combine(
            backupRoot,
            recoveryPointId,
            "recovery-point.json");
        if (!File.Exists(pointPath))
        {
            return null;
        }

        return JsonSerializer.Deserialize<AgentConfigurationRecoveryPoint>(
            await File.ReadAllTextAsync(pointPath, cancellationToken),
            ManagerJson.Options);
    }

    public async Task RestoreAsync(
        AgentConfigurationRecoveryPoint recoveryPoint,
        CancellationToken cancellationToken = default)
    {
        foreach (var entry in recoveryPoint.Entries)
        {
            if (entry.Existed)
            {
                Directory.CreateDirectory(
                    Path.GetDirectoryName(entry.SourcePath)!);
                await using var source = File.OpenRead(entry.BackupPath);
                await using var target = File.Create(entry.SourcePath);
                await source.CopyToAsync(target, cancellationToken);
            }
            else if (File.Exists(entry.SourcePath))
            {
                File.Delete(entry.SourcePath);
            }
        }
    }
}
