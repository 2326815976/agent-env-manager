namespace AgentEnvManager.Core.Runtimes;

internal static class PowerShellRuntimeStateLayout
{
    public static string GetModulesDirectory(string stateDirectory)
    {
        return Path.Combine(stateDirectory, "Modules");
    }

    public static string GetProfilePath(string stateDirectory)
    {
        return Path.Combine(stateDirectory, "profile.ps1");
    }
}
