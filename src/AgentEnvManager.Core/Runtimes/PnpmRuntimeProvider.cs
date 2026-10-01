using AgentEnvManager.Core.Inspection;

namespace AgentEnvManager.Core.Runtimes;

public sealed class PnpmRuntimeProvider : IRuntimeProvider
{
    public const string ProviderId = "pnpm";
    public const string OfficialSource = "https://github.com/pnpm/pnpm";

    private const string Version = "12.8.1";
    private const string License = "MIT";

    // pnpm 未发布制品 SHA-256，这里记录的是官方发布制品
    // pnpm-win32-x64.zip 的 SHA-256（2026-10-01 校验）。
    private const string ArtifactSha256 =
        "823fc131326afeee292e113e2299ca9851c3ee5148a7de921c897f0170de6f05";
    private const string DownloadUrl =
        "https://github.com/pnpm/pnpm/releases/download/v12.8.1/pnpm-win32-x64.zip";

    public RuntimeProviderDescriptor Descriptor { get; } = new(
        ProviderId,
        "pnpm",
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
        return "pnpm.exe";
    }

    public IReadOnlyList<RuntimeStateFile> CreateStateFiles(
        RuntimeStateBindingContext context)
    {
        return [];
    }
}
