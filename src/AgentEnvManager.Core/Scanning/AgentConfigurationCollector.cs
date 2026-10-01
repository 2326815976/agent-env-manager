using AgentEnvManager.Core.Inspection;

namespace AgentEnvManager.Core.Scanning;

internal static class AgentConfigurationCollector
{
    public static IReadOnlyList<DirectoryCandidate> Collect(
        IWindowsEnvironmentAccessor accessor)
    {
        var candidates = new List<DirectoryCandidate>();
        var userProfile = accessor.GetFolderPath(
            Environment.SpecialFolder.UserProfile);
        var localAppData = accessor.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        var codexHome = accessor.GetEnvironmentVariable("CODEX_HOME");
        var ccSwitchHome = accessor.GetEnvironmentVariable("CC_SWITCH_HOME");
        var workBuddyHome = accessor.GetEnvironmentVariable("WORKBUDDY_HOME");
        var marvisHome = accessor.GetEnvironmentVariable("MARVIS_HOME");

        AddDirectory(
            accessor,
            candidates,
            new DirectoryCandidate(
                "Codex",
                codexHome ?? Path.Combine(userProfile, ".codex"),
                codexHome is null
                    ? DiscoverySourceInfo.AgentConfiguration
                    : new DiscoverySourceInfo(
                        DiscoverySource.AgentConfiguration,
                        "CODEX_HOME")));
        AddDirectory(
            accessor,
            candidates,
            new DirectoryCandidate(
                "CC Switch",
                ccSwitchHome ?? Path.Combine(userProfile, ".cc-switch"),
                ccSwitchHome is null
                    ? DiscoverySourceInfo.AgentConfiguration
                    : new DiscoverySourceInfo(
                        DiscoverySource.AgentConfiguration,
                        "CC_SWITCH_HOME")));
        AddDirectory(
            accessor,
            candidates,
            new DirectoryCandidate(
                "CC Switch",
                Path.Combine(userProfile, ".cc-switch"),
                DiscoverySourceInfo.AgentConfiguration));
        AddDirectory(
            accessor,
            candidates,
            new DirectoryCandidate(
                "CC Switch",
                Path.Combine(userProfile, ".ccswitch"),
                DiscoverySourceInfo.AgentConfiguration));
        AddDirectory(
            accessor,
            candidates,
            new DirectoryCandidate(
                "WorkBuddy",
                workBuddyHome ?? Path.Combine(userProfile, ".workbuddy"),
                workBuddyHome is null
                    ? DiscoverySourceInfo.AgentConfiguration
                    : new DiscoverySourceInfo(
                        DiscoverySource.AgentConfiguration,
                        "WORKBUDDY_HOME")));
        AddDirectory(
            accessor,
            candidates,
            new DirectoryCandidate(
                "WorkBuddy 工作区",
                Path.Combine(userProfile, "WorkBuddy"),
                DiscoverySourceInfo.AgentConfiguration));
        AddDirectory(
            accessor,
            candidates,
            new DirectoryCandidate(
                "Marvis",
                marvisHome ?? Path.Combine(userProfile, ".marvis"),
                marvisHome is null
                    ? DiscoverySourceInfo.AgentConfiguration
                    : new DiscoverySourceInfo(
                        DiscoverySource.AgentConfiguration,
                        "MARVIS_HOME")));
        AddDirectory(
            accessor,
            candidates,
            new DirectoryCandidate(
                "ChatGPT",
                Path.Combine(localAppData, "OpenAI", "ChatGPTLauncher"),
                new DiscoverySourceInfo(
                    DiscoverySource.AgentConfiguration,
                    "LocalAppData OpenAI")));
        AddDirectory(
            accessor,
            candidates,
            new DirectoryCandidate(
                "ChatGPT",
                Path.Combine(localAppData, "OpenAI", "ChatGPTRepair"),
                new DiscoverySourceInfo(
                    DiscoverySource.AgentConfiguration,
                    "LocalAppData OpenAI")));

        return candidates;
    }

    private static void AddDirectory(
        IWindowsEnvironmentAccessor accessor,
        ICollection<DirectoryCandidate> candidates,
        DirectoryCandidate candidate)
    {
        if (accessor.DirectoryExists(candidate.Path))
        {
            candidates.Add(candidate);
        }
    }
}
