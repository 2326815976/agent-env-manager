namespace AgentEnvManager.Cli;

internal enum CliAction
{
    Inspect,
    PreviewAdoption,
    Adopt,
    RebuildIndex,
    RollbackPreview,
    Rollback,
    DiagnosticsPreview,
    DiagnosticsExport,
    EnvironmentVariablesInspect,
    EnvironmentVariablesApply,
    RuntimeList,
    RuntimeInstallPreview,
    RuntimeInstall,
    RuntimeImport,
    MigrationPreview,
    Migrate,
    AgentDiscover,
    AgentBindPreview,
    AgentBind,
    AgentHealth,
    Help,
    Invalid,
    Unknown
}

internal sealed record CliRequest(
    CliAction Action,
    string? Fingerprint = null,
    string? OperationId = null,
    string? DestinationPath = null,
    string? ProviderId = null,
    string? Version = null,
    string? ArtifactPath = null,
    string? MirrorUrl = null,
    string? InstallRoot = null,
    string? VariableName = null,
    string? VariableValue = null,
    string? ManagedPathEntry = null,
    bool ShowSecrets = false,
    string? AgentName = null,
    string? ConfigurationDirectory = null,
    string? ExecutablePath = null,
    string? ManagedEntryPath = null,
    string? RuntimeName = null,
    string? RuntimeVersion = null,
    string? WorkspacePath = null,
    string? RuntimeCommand = null,
    bool Confirmed = false,
    string? Error = null,
    string? UnknownCommand = null);

internal static class CliArguments
{
    private static readonly string[] AgentBindingOptions =
    [
        "--config-dir",
        "--executable",
        "--managed-entry",
        "--runtime",
        "--runtime-version",
        "--workspace",
        "--runtime-command"
    ];

    public static CliRequest Parse(IReadOnlyList<string> args)
    {
        if (args.Count == 0)
        {
            return new CliRequest(CliAction.Inspect);
        }

        if (args[0] is "--help" or "-h" or "help")
        {
            return new CliRequest(CliAction.Help);
        }

        return args[0] switch
        {
            "inspect" => Exact(args, CliAction.Inspect),
            "preview" => ParseSingleValue(
                args,
                value => new CliRequest(
                    CliAction.PreviewAdoption,
                    Fingerprint: value)),
            "adopt" => ParseAdopt(args),
            "rebuild-index" => Exact(args, CliAction.RebuildIndex),
            "rollback-preview" => ParseSingleValue(
                args,
                value => new CliRequest(
                    CliAction.RollbackPreview,
                    OperationId: value)),
            "rollback" => ParseRollback(args),
            "diagnostics-preview" => Exact(
                args,
                CliAction.DiagnosticsPreview),
            "diagnostics-export" => ParseSingleValue(
                args,
                value => new CliRequest(
                    CliAction.DiagnosticsExport,
                    DestinationPath: value)),
            "env-vars" => ParseEnvironmentVariablesInspect(args),
            "env-vars-apply" => ParseEnvironmentVariablesApply(args),
            "runtimes" => Exact(args, CliAction.RuntimeList),
            "runtime-install-preview" => ParseRuntimeInstall(
                args,
                isPreview: true),
            "runtime-install" => ParseRuntimeInstall(
                args,
                isPreview: false),
            "runtime-import" => ParseRuntimeImport(args),
            "migrate-preview" => ParseMigration(
                args,
                isPreview: true),
            "migrate" => ParseMigration(args, isPreview: false),
            "agent-discover" => ParseAgentDiscover(args),
            "agent-bind-preview" => ParseAgentBinding(
                args,
                CliAction.AgentBindPreview,
                allowConfirm: false),
            "agent-bind" => ParseAgentBinding(
                args,
                CliAction.AgentBind,
                allowConfirm: true),
            "agent-health" => ParseAgentBinding(
                args,
                CliAction.AgentHealth,
                allowConfirm: false),
            _ => new CliRequest(
                CliAction.Unknown,
                UnknownCommand: args[0])
        };
    }

    private static CliRequest Exact(
        IReadOnlyList<string> args,
        CliAction action)
    {
        return args.Count == 1
            ? new CliRequest(action)
            : Invalid($"{args[0]} 不接受额外参数。");
    }

    private static CliRequest ParseSingleValue(
        IReadOnlyList<string> args,
        Func<string, CliRequest> create)
    {
        return args.Count == 2
            ? create(args[1])
            : Invalid($"{args[0]} 需要一个参数。");
    }

    private static CliRequest ParseRollback(IReadOnlyList<string> args)
    {
        if (args.Count < 2)
        {
            return Invalid("rollback 需要一个操作 ID。");
        }

        return TryParseOptions(
            args,
            2,
            [],
            allowConfirm: true,
            out _,
            out var confirmed,
            out var error)
            ? new CliRequest(
                CliAction.Rollback,
                OperationId: args[1],
                Confirmed: confirmed)
            : Invalid(error!);
    }

    private static CliRequest ParseAdopt(IReadOnlyList<string> args)
    {
        if (args.Count < 2)
        {
            return Invalid("adopt 需要环境指纹。");
        }

        return TryParseOptions(
            args,
            2,
            [],
            allowConfirm: true,
            out _,
            out var confirmed,
            out var error)
            ? new CliRequest(
                CliAction.Adopt,
                Fingerprint: args[1],
                Confirmed: confirmed)
            : Invalid(error!);
    }

    private static CliRequest ParseRuntimeInstall(
        IReadOnlyList<string> args,
        bool isPreview)
    {
        if (args.Count < 3)
        {
            return Invalid(
                $"{args[0]} 需要提供者 ID 和版本。");
        }

        if (!TryParseOptions(
            args,
            3,
            ["--mirror", "--install-dir"],
            allowConfirm: !isPreview,
            out var options,
            out var confirmed,
            out var error))
        {
            return Invalid(error!);
        }

        return new CliRequest(
            isPreview
                ? CliAction.RuntimeInstallPreview
                : CliAction.RuntimeInstall,
            ProviderId: args[1],
            Version: args[2],
            MirrorUrl: options.GetValueOrDefault("--mirror"),
            InstallRoot: options.GetValueOrDefault("--install-dir"),
            Confirmed: confirmed);
    }

    private static CliRequest ParseEnvironmentVariablesApply(
        IReadOnlyList<string> args)
    {
        if (!TryParseOptions(
            args,
            1,
            ["--set", "--managed-path"],
            allowConfirm: true,
            out var options,
            out var confirmed,
            out var error))
        {
            return Invalid(error!);
        }

        string? variableName = null;
        string? variableValue = null;
        if (options.TryGetValue("--set", out var assignment))
        {
            var separator = assignment.IndexOf('=');
            if (separator <= 0)
            {
                return Invalid("--set 需要 NAME=VALUE 形式。");
            }

            variableName = assignment[..separator].Trim();
            variableValue = assignment[(separator + 1)..];
            if (variableName.Length == 0)
            {
                return Invalid("--set 需要 NAME=VALUE 形式。");
            }
        }

        var managedPathEntry = options.GetValueOrDefault("--managed-path");
        if (variableName is null
            && string.IsNullOrWhiteSpace(managedPathEntry))
        {
            return Invalid(
                "env-vars-apply 需要 --set 或 --managed-path。");
        }

        return new CliRequest(
            CliAction.EnvironmentVariablesApply,
            VariableName: variableName,
            VariableValue: variableValue,
            ManagedPathEntry: managedPathEntry,
            Confirmed: confirmed);
    }

    private static CliRequest ParseEnvironmentVariablesInspect(
        IReadOnlyList<string> args)
    {
        var showSecrets = false;
        for (var index = 1; index < args.Count; index++)
        {
            if (string.Equals(
                    args[index],
                    "--show-secrets",
                    StringComparison.OrdinalIgnoreCase))
            {
                showSecrets = true;
                continue;
            }

            return Invalid($"无法识别参数: {args[index]}");
        }

        return new CliRequest(
            CliAction.EnvironmentVariablesInspect,
            ShowSecrets: showSecrets);
    }

    private static CliRequest ParseRuntimeImport(
        IReadOnlyList<string> args)
    {
        if (args.Count < 4)
        {
            return Invalid(
                "runtime-import 需要提供者 ID、版本和制品路径。");
        }

        return TryParseOptions(
            args,
            4,
            [],
            allowConfirm: true,
            out _,
            out var confirmed,
            out var error)
            ? new CliRequest(
                CliAction.RuntimeImport,
                ProviderId: args[1],
                Version: args[2],
                ArtifactPath: args[3],
                Confirmed: confirmed)
            : Invalid(error!);
    }

    private static CliRequest ParseMigration(
        IReadOnlyList<string> args,
        bool isPreview)
    {
        if (args.Count < 3)
        {
            return Invalid(
                $"{args[0]} 需要环境指纹和目标路径。");
        }

        return TryParseOptions(
            args,
            3,
            [],
            allowConfirm: !isPreview,
            out _,
            out var confirmed,
            out var error)
            ? new CliRequest(
                isPreview ? CliAction.MigrationPreview : CliAction.Migrate,
                Fingerprint: args[1],
                DestinationPath: args[2],
                Confirmed: confirmed)
            : Invalid(error!);
    }

    private static CliRequest ParseAgentDiscover(
        IReadOnlyList<string> args)
    {
        if (args.Count < 2)
        {
            return Invalid("agent-discover 需要 Agent 名称。");
        }

        if (!TryParseOptions(
            args,
            2,
            ["--config-dir", "--executable"],
            allowConfirm: false,
            out var options,
            out _,
            out var error))
        {
            return Invalid(error!);
        }

        return new CliRequest(
            CliAction.AgentDiscover,
            AgentName: args[1],
            ConfigurationDirectory: options.GetValueOrDefault("--config-dir"),
            ExecutablePath: options.GetValueOrDefault("--executable"));
    }

    private static CliRequest ParseAgentBinding(
        IReadOnlyList<string> args,
        CliAction action,
        bool allowConfirm)
    {
        if (args.Count < 2)
        {
            return Invalid($"{args[0]} 需要 Agent 名称。");
        }

        if (!TryParseOptions(
            args,
            2,
            AgentBindingOptions,
            allowConfirm,
            out var options,
            out var confirmed,
            out var error))
        {
            return Invalid(error!);
        }

        if (!TryGetRequiredOption(
                options,
                "--config-dir",
                out var configurationDirectory)
            || !TryGetRequiredOption(
                options,
                "--executable",
                out var executable)
            || !TryGetRequiredOption(
                options,
                "--managed-entry",
                out var managedEntry)
            || !TryGetRequiredOption(
                options,
                "--runtime",
                out var runtimeName)
            || !TryGetRequiredOption(
                options,
                "--runtime-version",
                out var runtimeVersion))
        {
            return Invalid(
                $"{args[0]} 需要 --config-dir、--executable、" +
                "--managed-entry、--runtime 和 --runtime-version。");
        }

        return new CliRequest(
            action,
            AgentName: args[1],
            ConfigurationDirectory: configurationDirectory,
            ExecutablePath: executable,
            ManagedEntryPath: managedEntry,
            RuntimeName: runtimeName,
            RuntimeVersion: runtimeVersion,
            WorkspacePath: options.GetValueOrDefault("--workspace"),
            RuntimeCommand: options.GetValueOrDefault("--runtime-command"),
            Confirmed: confirmed);
    }

    private static bool TryParseOptions(
        IReadOnlyList<string> args,
        int startIndex,
        IReadOnlyCollection<string> allowedOptions,
        bool allowConfirm,
        out Dictionary<string, string> options,
        out bool confirmed,
        out string? error)
    {
        options = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);
        confirmed = false;
        error = null;
        for (var index = startIndex; index < args.Count; index++)
        {
            var option = args[index];
            if (option == "--confirm")
            {
                if (!allowConfirm)
                {
                    error = $"{args[0]} 不支持 --confirm。";
                    return false;
                }

                confirmed = true;
                continue;
            }

            if (!option.StartsWith("--", StringComparison.Ordinal)
                || !allowedOptions.Contains(
                    option,
                    StringComparer.OrdinalIgnoreCase))
            {
                error = $"无法识别参数: {option}";
                return false;
            }

            if (index + 1 >= args.Count
                || args[index + 1].StartsWith(
                    "--",
                    StringComparison.Ordinal))
            {
                error = $"{option} 缺少值。";
                return false;
            }

            if (!options.TryAdd(option, args[++index]))
            {
                error = $"{option} 重复。";
                return false;
            }
        }

        return true;
    }

    private static bool TryGetRequiredOption(
        IReadOnlyDictionary<string, string> options,
        string name,
        out string value)
    {
        return options.TryGetValue(name, out value!)
            && !string.IsNullOrWhiteSpace(value);
    }

    private static CliRequest Invalid(string message)
    {
        return new CliRequest(CliAction.Invalid, Error: message);
    }
}
