using AgentEnvManager.Core.Adoption;

namespace AgentEnvManager.Core.Deletion;

public sealed class WindowsRuntimeStateCatalog : IRuntimeStateCatalog
{
    public Task<IReadOnlyList<string>> DescribeAsync(
        EnvironmentManifest manifest,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var userProfile = Environment.GetFolderPath(
            Environment.SpecialFolder.UserProfile);
        var localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        var roamingAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.ApplicationData);
        IReadOnlyList<string> state = manifest.Name switch
        {
            "Node.js" =>
            [
                $"全局包: {Path.Combine(roamingAppData, "npm")}",
                $"缓存: {Path.Combine(localAppData, "npm-cache")}"
            ],
            "Python" =>
            [
                $"用户包: {Path.Combine(roamingAppData, "Python")}",
                $"缓存: {Path.Combine(localAppData, "pip", "Cache")}"
            ],
            "Git" =>
            [
                $"用户配置: {Path.Combine(userProfile, ".gitconfig")}",
                $"SSH 目录: {Path.Combine(userProfile, ".ssh")}（不删除）"
            ],
            "PowerShell 7" =>
            [
                $"模块目录: {Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.MyDocuments),
                    "PowerShell",
                    "Modules")}",
                $"用户配置: {Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.MyDocuments),
                    "PowerShell",
                    "profile.ps1")}"
            ],
            _ => []
        };
        return Task.FromResult(state);
    }
}
