namespace AgentEnvManager.Cli;

internal enum CliAction
{
    Inspect,
    Help,
    Unknown
}

internal sealed record CliRequest(
    CliAction Action,
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

        return new CliRequest(
            CliAction.Unknown,
            args[0]);
    }
}
