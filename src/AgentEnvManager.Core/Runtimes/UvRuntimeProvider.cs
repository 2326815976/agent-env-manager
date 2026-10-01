using AgentEnvManager.Core.Inspection;

namespace AgentEnvManager.Core.Runtimes;

public sealed class UvRuntimeProvider : IRuntimeProvider
{
    public const string ProviderId = "uv";
    public const string OfficialSource = "https://github.com/astral-sh/uv";

    private const string Version = "0.12.21";
    private const string License = "MIT OR Apache-2.0";
    private const string ArtifactSha256 =
        "5d223efa0bf00208c3853246af09420419dfbd352536aa6bb8163d6170e23890";
    private const string DownloadUrl =
        "https://github.com/astral-sh/uv/releases/download/0.12.21/uv-x86_64-pc-windows-msvc.zip";

    public RuntimeProviderDescriptor Descriptor { get; } = new(
        ProviderId,
        "uv",
        EnvironmentAssetKind.PackageManager,
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
        return "uv.exe";
    }

    public IReadOnlyList<RuntimeStateFile> CreateStateFiles(
        RuntimeStateBindingContext context)
    {
        return [];
    }
}
