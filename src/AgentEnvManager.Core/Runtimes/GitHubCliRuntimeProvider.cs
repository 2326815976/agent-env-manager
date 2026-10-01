using AgentEnvManager.Core.Inspection;

namespace AgentEnvManager.Core.Runtimes;

public sealed class GitHubCliRuntimeProvider : IRuntimeProvider
{
    public const string ProviderId = "gh";
    public const string OfficialSource = "https://github.com/cli/cli";

    private const string Version = "2.102.0";
    private const string License = "MIT";
    private const string ArtifactSha256 =
        "ae64e556ecc240b200f7eba60d550e4bb60d78e860e69dd88c449405b86067f4";
    private const string DownloadUrl =
        "https://github.com/cli/cli/releases/download/v2.102.0/gh_2.102.0_windows_amd64.zip";

    public RuntimeProviderDescriptor Descriptor { get; } = new(
        ProviderId,
        "GitHub CLI",
        EnvironmentAssetKind.ToolRuntime,
        RuntimeProviderMode.Installable,
        DiscoverySourceInfo.RuntimeProvider,
        OfficialSource,
        License,
        RuntimeInstallStrategy.OfficialArchive,
        [
            new RuntimeArtifactDescriptor(
                Version,
                OfficialSource,
                License,
                ArtifactSha256,
                RuntimeInstallStrategy.OfficialArchive,
                ["win-x64"],
                DownloadUrl)
        ]);

    public RuntimeInstallCommand CreateInstallCommand(
        RuntimeInstallContext context)
    {
        return OfficialArchiveInstaller.CreateCommand(
            context.Artifact,
            context,
            OfficialArchiveFormat.Zip);
    }

    public string GetExecutableRelativePath(
        RuntimeArtifactDescriptor artifact,
        bool fromCache = false)
    {
        return Path.Combine("bin", "gh.exe");
    }

    public IReadOnlyList<RuntimeStateFile> CreateStateFiles(
        RuntimeStateBindingContext context)
    {
        return [];
    }
}
