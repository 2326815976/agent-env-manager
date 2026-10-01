namespace AgentEnvManager.Cli;

internal enum CliAction
{
    Inspect,
    PreviewAdoption,
    Adopt,
    RebuildIndex,
    Rollback,
    DiagnosticsPreview,
    DiagnosticsExport,
    Help,
    Unknown
}

internal sealed record CliRequest(
    CliAction Action,
    string? Fingerprint = null,
    string? OperationId = null,
    string? DestinationPath = null,
    string? UnknownCommand = null);

internal static class CliArguments
{
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

        if (args[0] == "inspect")
        {
            return new CliRequest(CliAction.Inspect);
        }

        if (args[0] == "preview" && args.Count == 2)
        {
            return new CliRequest(
                CliAction.PreviewAdoption,
                Fingerprint: args[1]);
        }

        if (args[0] == "adopt" && args.Count == 2)
        {
            return new CliRequest(
                CliAction.Adopt,
                Fingerprint: args[1]);
        }

        if (args[0] == "rebuild-index" && args.Count == 1)
        {
            return new CliRequest(CliAction.RebuildIndex);
        }

        if (args[0] == "rollback" && args.Count == 2)
        {
            return new CliRequest(
                CliAction.Rollback,
                OperationId: args[1]);
        }

        if (args[0] == "diagnostics-preview" && args.Count == 1)
        {
            return new CliRequest(CliAction.DiagnosticsPreview);
        }

        if (args[0] == "diagnostics-export" && args.Count == 2)
        {
            return new CliRequest(
                CliAction.DiagnosticsExport,
                DestinationPath: args[1]);
        }

        return new CliRequest(
            CliAction.Unknown,
            UnknownCommand: args[0]);
    }
}
