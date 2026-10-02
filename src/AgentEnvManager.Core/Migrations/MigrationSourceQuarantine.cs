using System.Text.Json;
using AgentEnvManager.Core.Storage;

namespace AgentEnvManager.Core.Migrations;

public interface IMigrationSourceQuarantineStore
{
    Task<string> QuarantineAsync(
        string sourcePath,
        CancellationToken cancellationToken = default);

    Task RestoreAsync(
        string quarantineId,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        string quarantineId,
        CancellationToken cancellationToken = default);
}

internal sealed record MigrationSourceQuarantineEntry(
    string Id,
    string OriginalPath,
    string QuarantinePath,
    DateTimeOffset CreatedAtUtc);

public sealed class FileSystemMigrationSourceQuarantine(
    string quarantineRoot,
    TimeProvider? timeProvider = null) : IMigrationSourceQuarantineStore
{
    private readonly TimeProvider _timeProvider =
        timeProvider ?? TimeProvider.System;

    public async Task<string> QuarantineAsync(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        var source = Path.GetFullPath(sourcePath);
        if (!Directory.Exists(source))
        {
            throw new DirectoryNotFoundException(
                $"待隔离的源目录不存在: {source}");
        }

        var id = Guid.NewGuid().ToString("N");
        var quarantinePath = Path.Combine(
            quarantineRoot,
            id,
            Path.GetFileName(source.TrimEnd('\\', '/')));
        Directory.CreateDirectory(Path.GetDirectoryName(quarantinePath)!);
        Directory.Move(source, quarantinePath);
        await AtomicJsonFile.WriteAsync(
            GetEntryPath(id),
            new MigrationSourceQuarantineEntry(
                id,
                source,
                quarantinePath,
                _timeProvider.GetUtcNow()),
            cancellationToken);
        return id;
    }

    public async Task RestoreAsync(
        string quarantineId,
        CancellationToken cancellationToken = default)
    {
        var entry = await ReadEntryAsync(quarantineId, cancellationToken);
        if (Directory.Exists(entry.OriginalPath))
        {
            throw new InvalidOperationException(
                $"原路径已存在，无法恢复隔离目录: {entry.OriginalPath}");
        }

        Directory.CreateDirectory(
            Path.GetDirectoryName(entry.OriginalPath)!);
        Directory.Move(entry.QuarantinePath, entry.OriginalPath);
        Directory.Delete(Path.GetDirectoryName(
            entry.QuarantinePath)!, recursive: true);
    }

    public Task DeleteAsync(
        string quarantineId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var directory = Path.Combine(quarantineRoot, quarantineId);
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }

        return Task.CompletedTask;
    }

    private string GetEntryPath(string quarantineId)
    {
        return Path.Combine(quarantineRoot, quarantineId, "quarantine.json");
    }

    private async Task<MigrationSourceQuarantineEntry> ReadEntryAsync(
        string quarantineId,
        CancellationToken cancellationToken)
    {
        var path = GetEntryPath(quarantineId);
        if (!File.Exists(path))
        {
            throw new KeyNotFoundException(
                $"未找到隔离记录: {quarantineId}");
        }

        return JsonSerializer.Deserialize<MigrationSourceQuarantineEntry>(
            await File.ReadAllTextAsync(path, cancellationToken),
            ManagerJson.Options)
            ?? throw new InvalidOperationException("隔离记录无法解析。");
    }
}
