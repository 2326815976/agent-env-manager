namespace AgentEnvManager.Core.Runtimes;

internal static class RuntimeStateLayout
{
    public static string GetVersionStateDirectory(
        string runtimeStateRoot,
        string identity)
    {
        return Path.Combine(runtimeStateRoot, identity);
    }
}

internal static class NodeRuntimeStateLayout
{
    public static string GetGlobalPrefix(string stateDirectory)
    {
        return Path.Combine(stateDirectory, "npm-global");
    }

    public static string GetCacheDirectory(string stateDirectory)
    {
        return Path.Combine(stateDirectory, "npm-cache");
    }

    public static string GetNpmrcPath(string nodeLocation)
    {
        return Path.Combine(
            nodeLocation,
            "node_modules",
            "npm",
            "npmrc");
    }

    public static string GetNpmCommandPath(string nodeLocation)
    {
        return Path.Combine(nodeLocation, "npm.cmd");
    }

    public static string GetNpxCommandPath(string nodeLocation)
    {
        return Path.Combine(nodeLocation, "npx.cmd");
    }
}
