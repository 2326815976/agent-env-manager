using System.Text;
using AgentEnvManager.Core.Inspection;

namespace AgentEnvManager.Core.Runtimes;

public sealed class NodeRuntimeProvider : IRuntimeProvider
{
    public const string ProviderId = "node";
    public const string OfficialSource = "https://nodejs.org/dist/v24.1.0/";

    private const string ArtifactSha256 =
        "81d6774f5c1581c7ddd32fb25cf6138f68755dfbb245025d05a249aafa35ea9d";
    private const string DownloadUrl =
        "https://nodejs.org/dist/v24.1.0/node-v24.1.0-win-x64.zip";

    public RuntimeProviderDescriptor Descriptor { get; } = new(
        ProviderId,
        "Node.js",
        EnvironmentAssetKind.ToolRuntime,
        RuntimeProviderMode.Installable,
        DiscoverySourceInfo.RuntimeProvider,
        OfficialSource,
        "MIT",
        RuntimeInstallStrategy.OfficialArchive,
        [
            new RuntimeArtifactDescriptor(
                "24.1.0",
                OfficialSource,
                "MIT",
                ArtifactSha256,
                RuntimeInstallStrategy.OfficialArchive,
                ["win-x64"],
                DownloadUrl)
        ]);

    public RuntimeInstallCommand CreateInstallCommand(
        RuntimeInstallContext context)
    {
        var artifact = context.Artifact;
        var globalPrefix = Path.Combine(
            context.RuntimeStateDirectory,
            "npm-global").Replace('\\', '/');
        var cacheDirectory = Path.Combine(
            context.RuntimeStateDirectory,
            "npm-cache").Replace('\\', '/');
        var npmrcPath = Path.Combine(
            context.InstallRoot,
            $"node-v{artifact.Version}-win-x64",
            "node_modules",
            "npm",
            "npmrc").Replace('\\', '/');
        var script = $$"""
            $ErrorActionPreference = 'Stop'
            $ProgressPreference = 'SilentlyContinue'
            [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
            $zipPath = Join-Path $PWD 'node.zip'
            Invoke-WebRequest -UseBasicParsing -Uri {{PowerShellLiteral(artifact.DownloadUrl)}} -OutFile $zipPath
            $sha256 = [Security.Cryptography.SHA256]::Create()
            $stream = [IO.File]::OpenRead($zipPath)
            try {
                $hashBytes = $sha256.ComputeHash($stream)
            }
            finally {
                $stream.Dispose()
                $sha256.Dispose()
            }
            $actualHash = [BitConverter]::ToString($hashBytes).Replace('-', '').ToLowerInvariant()
            if ($actualHash -ne {{PowerShellLiteral(artifact.Sha256)}}) {
                throw "Node.js 制品哈希不匹配: $actualHash"
            }

            Expand-Archive -LiteralPath $zipPath -DestinationPath $PWD -Force
            Remove-Item -LiteralPath $zipPath -Force

            $globalPrefix = {{PowerShellLiteral(globalPrefix)}}
            $cacheDirectory = {{PowerShellLiteral(cacheDirectory)}}
            New-Item -ItemType Directory -Force -Path $globalPrefix, $cacheDirectory | Out-Null
            $npmrcPath = {{PowerShellLiteral(npmrcPath)}}
            $content = "prefix=$globalPrefix`ncache=$cacheDirectory`n"
            [IO.File]::WriteAllText($npmrcPath, $content, (New-Object Text.UTF8Encoding($false)))
            """;
        var encodedCommand = Convert.ToBase64String(
            Encoding.Unicode.GetBytes(script));
        return new RuntimeInstallCommand(
            "powershell.exe",
            [
                "-NoLogo",
                "-NoProfile",
                "-NonInteractive",
                "-EncodedCommand",
                encodedCommand
            ],
            context.InstallRoot);
    }

    public string GetExecutableRelativePath(
        RuntimeArtifactDescriptor artifact)
    {
        return Path.Combine(
            $"node-v{artifact.Version}-win-x64",
            "node.exe");
    }

    private static string PowerShellLiteral(string value)
    {
        return $"'{value.Replace("'", "''")}'";
    }
}
