using System.Text.Json;
using System.Text.RegularExpressions;

namespace AgentEnvManager.Core.Agents;

internal static class AgentHealthOutput
{
    public static AgentHealthCheckResult Evaluate(
        AgentProcessResult result,
        string runtimeName,
        string runtimeVersion)
    {
        var output = $"{result.StandardOutput}\n{result.StandardError}";
        var healthy = result.ExitCode == 0
            && ContainsSuccessfulToolExecution(result.StandardOutput)
            && Regex.IsMatch(
                output,
                $@"(?<![A-Za-z0-9_.-]){Regex.Escape(runtimeVersion)}(?![A-Za-z0-9_.-])",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return new AgentHealthCheckResult(
            healthy,
            healthy
                ? $"{runtimeName} 调用成功。"
                : $"exit={result.ExitCode}; stdout={result.StandardOutput.Trim()}; stderr={result.StandardError.Trim()}");
    }

    public static bool ContainsSuccessfulToolExecution(string output)
    {
        foreach (var line in output.Split(
                     ['\r', '\n'],
                     StringSplitOptions.RemoveEmptyEntries
                     | StringSplitOptions.TrimEntries))
        {
            if (!line.StartsWith('{'))
            {
                continue;
            }

            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (root.TryGetProperty("item", out var item))
                {
                    root = item;
                }

                if (!root.TryGetProperty("type", out var type)
                    || type.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                if (type.GetString() == "command_execution")
                {
                    if (IsSuccessfulExitCode(root))
                    {
                        return true;
                    }

                    continue;
                }

                if (type.GetString() == "tool_call"
                    && (IsSuccessfulExitCode(root)
                        || HasSuccessfulStatus(root)))
                {
                    return true;
                }
            }
            catch (JsonException)
            {
            }
        }

        return false;
    }

    private static bool IsSuccessfulExitCode(JsonElement element)
    {
        return element.TryGetProperty("exit_code", out var exitCode)
            && exitCode.ValueKind == JsonValueKind.Number
            && exitCode.TryGetInt32(out var value)
            && value == 0;
    }

    private static bool HasSuccessfulStatus(JsonElement element)
    {
        if (!element.TryGetProperty("status", out var status)
            || status.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        return status.GetString() is "completed" or "success";
    }
}
