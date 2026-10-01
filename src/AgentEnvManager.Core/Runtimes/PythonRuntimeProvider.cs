using AgentEnvManager.Core.Inspection;

namespace AgentEnvManager.Core.Runtimes;

public sealed class PythonRuntimeProvider : IRuntimeProvider
{
    public const string ProviderId = "python";
    public const string OfficialSource =
        "https://github.com/astral-sh/python-build-standalone";

    private const string BuildTag = "20250918";
    private const string ArtifactSha256 =
        "bc229e5364699a8456b6ceabde8348c75e62312ffd62631dd1e494a1755a45ed";
    private const string DownloadUrl =
        "https://github.com/astral-sh/python-build-standalone/releases/download/20250918/cpython-3.13.7%2B20250918-x86_64-pc-windows-msvc-install_only_stripped.tar.gz";

    public RuntimeProviderDescriptor Descriptor { get; } = new(
        ProviderId,
        "Python",
        EnvironmentAssetKind.ToolRuntime,
        RuntimeProviderMode.Installable,
        DiscoverySourceInfo.UvRuntime,
        OfficialSource,
        "PSF-2.0",
        RuntimeInstallStrategy.UvManagedDownload,
        [
            new RuntimeArtifactDescriptor(
                "3.13.7",
                $"{OfficialSource}/releases/tag/{BuildTag}",
                "PSF-2.0",
                ArtifactSha256,
                RuntimeInstallStrategy.UvManagedDownload,
                ["win-x64"],
                DownloadUrl)
        ]);

    public RuntimeInstallCommand CreateInstallCommand(
        RuntimeInstallContext context)
    {
        return new RuntimeInstallCommand(
            "uv",
            [
                "python",
                "install",
                context.Artifact.Version,
                "--install-dir",
                context.InstallRoot,
                "--no-bin",
                "--no-registry"
            ],
            context.InstallRoot);
    }

    public string GetExecutableRelativePath(
        RuntimeArtifactDescriptor artifact)
    {
        return Path.Combine(
            $"cpython-{artifact.Version}-windows-x86_64-none",
            "python.exe");
    }
}
