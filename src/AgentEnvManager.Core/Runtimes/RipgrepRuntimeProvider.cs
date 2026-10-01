using AgentEnvManager.Core.Inspection;

namespace AgentEnvManager.Core.Runtimes;

public sealed class RipgrepRuntimeProvider : IRuntimeProvider
{
    public const string ProviderId = "ripgrep";
    public const string OfficialSource = "https://github.com/BurntSushi/ripgrep";

    private const string Version = "15.2.0";
    private const string License = "MIT OR Unlicense";
    private const string ArtifactSha256 =
        "71b2fef860abe467217a538ff31de02f5258807c0129f771846f87bd029aafc5";
    private const string DownloadUrl =
        "https://github.com/BurntSushi/ripgrep/releases/download/15.2.0/ripgrep-15.2.0-x86_64-pc-windows-msvc.zip";

    public RuntimeProviderDescriptor Descriptor { get; } = new(
        ProviderId,
        "ripgrep",
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
        return Path.Combine(
            $"ripgrep-{artifact.Version}-x86_64-pc-windows-msvc",
            "rg.exe");
    }

    public IReadOnlyList<RuntimeStateFile> CreateStateFiles(
        RuntimeStateBindingContext context)
    {
        return [];
    }
}
