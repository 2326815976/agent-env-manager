using AgentEnvManager.Core.Adoption;
using AgentEnvManager.Core.Runtimes;

namespace AgentEnvManager.Core.Deletion;

public sealed class WindowsRuntimeStateCatalog(
    string runtimeStateRoot) : IRuntimeStateCatalog
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
            "Node.js" => DescribeNodeState(manifest),
            "Python" =>
            [
                $"用户包: {Path.Combine(roamingAppData, "Python")}",
                $"缓存: {Path.Combine(localAppData, "pip", "Cache")}"
            ],
            "Git" =>
            [
                $"用户配置: {Path.Combine(userProfile, ".gitconfig")}（确认后才单独备份）",
                $"SSH 目录: {Path.Combine(userProfile, ".ssh")}（敏感，不迁移、不删除、不进入日志或诊断包）",
                $"凭据文件: {Path.Combine(userProfile, ".git-credentials")}（敏感，不读取、不迁移、不备份）",
                "凭据存储: Windows 凭据管理器（不读取、不迁移、不备份）"
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

    private IReadOnlyList<string> DescribeNodeState(
        EnvironmentManifest manifest)
    {
        var versionStateRoot = RuntimeStateLayout.GetVersionStateDirectory(
            runtimeStateRoot,
            manifest.Identity.Value);
        var globalPrefix = NodeRuntimeStateLayout.GetGlobalPrefix(
            versionStateRoot);
        var cacheDirectory = NodeRuntimeStateLayout.GetCacheDirectory(
            versionStateRoot);
        var packages = EnumerateGlobalPackages(globalPrefix);
        var packageSummary = packages.Count == 0
            ? "无"
            : string.Join(", ", packages);
        return
        [
            $"全局包: {globalPrefix}（受影响包: {packageSummary}）",
            $"缓存: {cacheDirectory}"
        ];
    }

    private static IReadOnlyList<string> EnumerateGlobalPackages(
        string globalPrefix)
    {
        var nodeModules = Path.Combine(globalPrefix, "node_modules");
        if (!Directory.Exists(nodeModules))
        {
            return [];
        }

        var packages = new List<string>();
        foreach (var directory in Directory.EnumerateDirectories(nodeModules))
        {
            var name = Path.GetFileName(directory);
            if (string.Equals(
                name,
                ".bin",
                StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (name.StartsWith('@'))
            {
                foreach (var scopedPackage in Directory.EnumerateDirectories(
                    directory))
                {
                    packages.Add(
                        $"{name}/{Path.GetFileName(scopedPackage)}");
                }

                continue;
            }

            packages.Add(name);
        }

        return packages
            .OrderBy(package => package, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
