namespace AgentEnvManager.Cli;

internal enum CliAction
{
    Inspect,
    PreviewAdoption,
    Adopt,
    RebuildIndex,
    Help,
    Unknown
}

internal sealed record CliRequest(
    CliAction Action,
    string? Fingerprint = null,
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

        return new CliRequest(
            CliAction.Unknown,
            UnknownCommand: args[0]);
    }
}
