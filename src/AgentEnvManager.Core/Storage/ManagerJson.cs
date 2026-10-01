using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentEnvManager.Core.Storage;

internal static class ManagerJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        WriteIndented = true,
        Converters =
        {
            new JsonStringEnumConverter()
        }
    };

    public static T? Deserialize<T>(string json)
    {
        return JsonSerializer.Deserialize<T>(json, Options);
    }
}
