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
        var roamingAppData = accessor.GetFolderPath(
            Environment.SpecialFolder.ApplicationData);
        var codexHome = accessor.GetEnvironmentVariable("CODEX_HOME");
        var ccSwitchHome = accessor.GetEnvironmentVariable("CC_SWITCH_HOME");
        var workBuddyHome = accessor.GetEnvironmentVariable("WORKBUDDY_HOME");
        var marvisHome = accessor.GetEnvironmentVariable("MARVIS_HOME");

        AddDirectory(
            accessor,
            candidates,
            new DirectoryCandidate(
                "ChatGPT",
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
                "ChatGPT Launcher",
                Path.Combine(localAppData, "OpenAI", "ChatGPTLauncher"),
                new DiscoverySourceInfo(
                    DiscoverySource.AgentConfiguration,
                    "LocalAppData OpenAI")));
        AddDirectory(
            accessor,
            candidates,
            new DirectoryCandidate(
                "ChatGPT 修复目录",
                Path.Combine(localAppData, "OpenAI", "ChatGPTRepair"),
                new DiscoverySourceInfo(
                    DiscoverySource.AgentConfiguration,
                    "LocalAppData OpenAI")));
        // 文档确认的兼容 Junction：%APPDATA%\Codex 与 %LOCALAPPDATA%\Codex
        // 通常指向真实 Codex 配置环境；解析后若与 CODEX_HOME 或
        // %USERPROFILE%\.codex 指向同一目录，会由去重逻辑合并为一条。
        AddDirectory(
            accessor,
            candidates,
            new DirectoryCandidate(
                "ChatGPT",
                Path.Combine(roamingAppData, "Codex"),
                new DiscoverySourceInfo(
                    DiscoverySource.AgentConfiguration,
                    "%APPDATA%\\Codex 兼容 Junction")));
        AddDirectory(
            accessor,
            candidates,
            new DirectoryCandidate(
                "ChatGPT",
                Path.Combine(localAppData, "Codex"),
                new DiscoverySourceInfo(
                    DiscoverySource.AgentConfiguration,
                    "%LOCALAPPDATA%\\Codex 兼容 Junction")));

        return candidates;
    }

    private static void AddDirectory(
        IWindowsEnvironmentAccessor accessor,
        ICollection<DirectoryCandidate> candidates,
        DirectoryCandidate candidate)
    {
        if (accessor.DirectoryExists(candidate.Path))
        {
            // 兼容路径（Junction）要解析到真实存储位置，否则环境清单会
            // 显示 C:\Users\<user>\.cc-switch 这类链接路径而不是真实目录。
            var target = accessor.ResolveLinkTarget(candidate.Path);
            var resolved = string.IsNullOrWhiteSpace(target)
                || string.Equals(
                    Path.GetFullPath(target),
                    Path.GetFullPath(candidate.Path),
                    StringComparison.OrdinalIgnoreCase)
                ? candidate
                : candidate with
                {
                    Path = Path.GetFullPath(target),
                    Source = new DiscoverySourceInfo(
                        DiscoverySource.AgentConfiguration,
                        $"兼容 Junction → {candidate.Path}")
                };
            if (candidates.Any(existing =>
                    string.Equals(
                        existing.Name,
                        resolved.Name,
                        StringComparison.OrdinalIgnoreCase)
                    && string.Equals(
                        Path.GetFullPath(existing.Path),
                        Path.GetFullPath(resolved.Path),
                        StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            candidates.Add(resolved);
        }
    }
}
