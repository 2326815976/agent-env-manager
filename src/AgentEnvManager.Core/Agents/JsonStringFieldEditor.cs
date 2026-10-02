using System.Text;
using System.Text.Json;

namespace AgentEnvManager.Core.Agents;

internal static class JsonStringFieldEditor
{
    public static bool TryReplaceStringValue(
        string json,
        string fieldName,
        string newValue,
        out string updated)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        var reader = new Utf8JsonReader(
            bytes,
            new JsonReaderOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });
        while (reader.Read())
        {
            if (reader.TokenType != JsonTokenType.PropertyName
                || !string.Equals(
                    reader.GetString(),
                    fieldName,
                    StringComparison.Ordinal))
            {
                continue;
            }

            if (!reader.Read()
                || reader.TokenType != JsonTokenType.String)
            {
                updated = json;
                return false;
            }

            var start = (int)reader.TokenStartIndex;
            var literalLength = reader.ValueSpan.Length + 2;
            var replacement = Encoding.UTF8.GetBytes(
                JsonSerializer.Serialize(newValue));
            var result = new List<byte>(
                bytes.Length - literalLength + replacement.Length);
            result.AddRange(bytes[..start]);
            result.AddRange(replacement);
            result.AddRange(bytes[(start + literalLength)..]);
            updated = Encoding.UTF8.GetString(result.ToArray());
            return true;
        }

        updated = json;
        return false;
    }

    public static string? ReadStringValue(string json, string fieldName)
    {
        var reader = new Utf8JsonReader(
            Encoding.UTF8.GetBytes(json),
            new JsonReaderOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });
        while (reader.Read())
        {
            if (reader.TokenType != JsonTokenType.PropertyName
                || !string.Equals(
                    reader.GetString(),
                    fieldName,
                    StringComparison.Ordinal))
            {
                continue;
            }

            if (!reader.Read())
            {
                return null;
            }

            return reader.TokenType == JsonTokenType.String
                ? reader.GetString()
                : null;
        }

        return null;
    }
}
