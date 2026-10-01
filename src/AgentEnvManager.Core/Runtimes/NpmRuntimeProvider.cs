using AgentEnvManager.Core.Inspection;

namespace AgentEnvManager.Core.Runtimes;

public sealed class NpmRuntimeProvider : IRuntimeProvider
{
    public const string ProviderId = "npm";
    public const string OfficialSource = "https://registry.npmjs.org/npm";

    private const string Version = "11.21.0";
    private const string License = "Artistic-2.0";

    // npm 未发布制品 SHA-256，这里记录的是官方 registry 制品
    // npm-11.21.0.tgz 的 SHA-256（2026-10-02 校验）。该版本 engines 为
    // ^20.17.0 || >=22.9.0，与目录内 Node.js 24.1.0 兼容。
    private const string ArtifactSha256 =
        "783e7c92bf73b442fb800c2d6ef3921e86da8894a700fed45140e37916877482";
    private const string DownloadUrl =
        "https://registry.npmjs.org/npm/-/npm-11.21.0.tgz";

    public RuntimeProviderDescriptor Descriptor { get; } = new(
        ProviderId,
        "npm",
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
        // npm 制品内的 npm.cmd 是给全局安装布局用的模板，直接解压后无法定位
        // npm-cli.js。这里在解压后写入以 node 调用 CLI 的可运行 shim。
        var binDirectory = Path.Combine(
            context.InstallRoot,
            "package",
            "bin");
        return OfficialArchiveInstaller.CreateCommand(
            context.Artifact,
            context,
            OfficialArchiveFormat.TarGz,
            CreateShimScript(binDirectory));
    }

    private static string CreateShimScript(string binDirectory)
    {
        return string.Join(
            Environment.NewLine,
            CreateShim(
                Path.Combine(binDirectory, "npm.cmd"),
                "npm-cli.js"),
            CreateShim(
                Path.Combine(binDirectory, "npx.cmd"),
                "npx-cli.js"));
    }

    private static string CreateShim(
        string shimPath,
        string cliFileName)
    {
        return string.Join(
            Environment.NewLine,
            $"Set-Content -LiteralPath {OfficialArchiveInstaller.Literal(shimPath)} -Value @(",
            "    '@ECHO OFF',",
            "    'SETLOCAL',",
            "    'SET \"NODE_EXE=%~dp0node.exe\"',",
            "    'IF NOT EXIST \"%NODE_EXE%\" SET \"NODE_EXE=node\"',",
            $"    '\"%NODE_EXE%\" \"%~dp0{cliFileName}\" %*'",
            ") -Encoding ASCII");
    }

    public string GetExecutableRelativePath(
        RuntimeArtifactDescriptor artifact,
        bool fromCache = false)
    {
        return Path.Combine("package", "bin", "npm.cmd");
    }

    public IReadOnlyList<RuntimeStateFile> CreateStateFiles(
        RuntimeStateBindingContext context)
    {
        return [];
    }
}
