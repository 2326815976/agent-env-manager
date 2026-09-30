using System.Text.Json;

namespace AgentEnvManager.Core.Storage;

internal static class JsonDirectoryReader
{
    public static async Task<IReadOnlyList<T>> ReadAllAsync<T>(
        string directory,
        string itemName,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        var items = new List<T>();
        foreach (var path in Directory.EnumerateFiles(
            directory,
            "*.json",
            SearchOption.TopDirectoryOnly))
        {
            var json = await File.ReadAllTextAsync(path, cancellationToken);
            var item = JsonSerializer.Deserialize<T>(
                json,
                ManagerJson.Options)
                ?? throw new InvalidDataException(
                    $"无法读取{itemName}: {path}");
            items.Add(item);
        }

        return items;
    }
}
