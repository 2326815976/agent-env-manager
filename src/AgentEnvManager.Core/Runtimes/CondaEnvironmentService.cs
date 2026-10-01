using System.Text.Json;
using AgentEnvManager.Core.Inspection;

namespace AgentEnvManager.Core.Runtimes;

public sealed class CondaEnvironmentService(
    IRuntimeCommandRunner commandRunner)
{
    public const string ProviderId = "conda";
    public const string OfficialSource = "https://docs.conda.io/";

    public static RuntimeProviderDescriptor Descriptor { get; } = new(
        ProviderId,
        "Conda",
        EnvironmentAssetKind.PackageManager,
        RuntimeProviderMode.ObservedOnly,
        DiscoverySourceInfo.RuntimeProvider,
        OfficialSource,
        "BSD-3-Clause",
        RuntimeInstallStrategy.ObservedRebuild,
        []);

    public async Task<IReadOnlyList<ObservedCondaEnvironment>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        var result = await commandRunner.RunAsync(
            "conda",
            ["env", "list", "--json"],
            Environment.CurrentDirectory,
            new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase),
            cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                CreateFailure("读取 Conda 环境", result));
        }

        var json = result.StandardOutput.Trim();
        var jsonStart = json.IndexOf('{');
        if (jsonStart < 0)
        {
            throw new InvalidDataException(
                "Conda 环境列表不是有效 JSON。");
        }

        using var document = JsonDocument.Parse(json[jsonStart..]);
        var root = document.RootElement;
        var activePrefix = ReadString(root, "active_prefix");
        var condaPrefix = ReadString(root, "conda_prefix");
        if (!root.TryGetProperty("envs", out var environments)
            || environments.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException(
                "Conda 环境列表缺少 envs 数组。");
        }

        var observations = new List<ObservedCondaEnvironment>();
        foreach (var element in environments.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var prefix = element.GetString();
            if (string.IsNullOrWhiteSpace(prefix))
            {
                continue;
            }

            prefix = Path.GetFullPath(prefix);
            var isBase = condaPrefix is not null
                && PathsEqual(prefix, condaPrefix);
            observations.Add(new ObservedCondaEnvironment(
                isBase ? "base" : GetEnvironmentName(prefix),
                prefix,
                activePrefix is not null
                    && PathsEqual(prefix, activePrefix),
                isBase));
        }

        return observations
            .OrderByDescending(environment => environment.IsBase)
            .ThenBy(
                environment => environment.Name,
                StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<CondaRebuildPlan> ExportAsync(
        string prefix,
        string definitionPath,
        string? targetPrefix = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(prefix))
        {
            throw new ArgumentException(
                "Conda 环境路径不能为空。",
                nameof(prefix));
        }

        if (string.IsNullOrWhiteSpace(definitionPath))
        {
            throw new ArgumentException(
                "Conda 重建定义路径不能为空。",
                nameof(definitionPath));
        }

        var sourcePrefix = Path.GetFullPath(prefix);
        var resolvedDefinitionPath = Path.GetFullPath(definitionPath);
        var definitionDirectory = Path.GetDirectoryName(
            resolvedDefinitionPath);
        if (!string.IsNullOrWhiteSpace(definitionDirectory))
        {
            Directory.CreateDirectory(definitionDirectory);
        }

        var result = await commandRunner.RunAsync(
            "conda",
            [
                "env",
                "export",
                "--prefix",
                sourcePrefix,
                "--file",
                resolvedDefinitionPath,
                "--no-builds"
            ],
            Environment.CurrentDirectory,
            new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase),
            cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                CreateFailure("导出 Conda 环境", result));
        }

        if (!File.Exists(resolvedDefinitionPath))
        {
            throw new InvalidOperationException(
                $"Conda 导出未生成定义文件: {resolvedDefinitionPath}");
        }

        var resolvedTargetPrefix = string.IsNullOrWhiteSpace(targetPrefix)
            ? null
            : Path.GetFullPath(targetPrefix);
        var commandLine = string.IsNullOrWhiteSpace(resolvedTargetPrefix)
            ? $"conda env create --file \"{resolvedDefinitionPath}\""
            : $"conda env create --file \"{resolvedDefinitionPath}\" --prefix \"{resolvedTargetPrefix}\"";
        return new CondaRebuildPlan(
            sourcePrefix,
            resolvedDefinitionPath,
            resolvedTargetPrefix,
            commandLine,
            "只导出 Conda 环境描述并生成重建计划，不移动、删除或切换原环境。");
    }

    private static string ReadString(
        JsonElement element,
        string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : string.Empty;
    }

    private static string GetEnvironmentName(string prefix)
    {
        var name = Path.GetFileName(prefix.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar));
        return string.IsNullOrWhiteSpace(name) ? prefix : name;
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(
            WindowsPathNormalizer.NormalizeForComparison(left),
            WindowsPathNormalizer.NormalizeForComparison(right),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string CreateFailure(
        string action,
        RuntimeCommandResult result)
    {
        var detail = string.IsNullOrWhiteSpace(result.StandardError)
            ? result.StandardOutput.Trim()
            : result.StandardError.Trim();
        return string.IsNullOrWhiteSpace(detail)
            ? $"{action}失败，退出码 {result.ExitCode}。"
            : $"{action}失败: {detail}";
    }
}
