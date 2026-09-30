using System.Text.Json;

namespace AgentEnvManager.Core.Storage;

internal static class AtomicJsonFile
{
    public static async Task WriteAsync<T>(
        string path,
        T value,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = $"{path}.tmp";
        var json = JsonSerializer.Serialize(value, ManagerJson.Options);
        await File.WriteAllTextAsync(
            temporaryPath,
            json,
            cancellationToken);
        File.Move(temporaryPath, path, overwrite: true);
    }
}
