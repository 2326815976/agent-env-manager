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
        var script = $$"""
            $ErrorActionPreference = 'Stop'
            $ProgressPreference = 'SilentlyContinue'
            [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
            $cachedArtifact = {{PowerShellLiteral(context.CachedArtifactPath ?? string.Empty)}}
            $downloaded = $false
            if ([string]::IsNullOrWhiteSpace($cachedArtifact)) {
                $zipPath = Join-Path $PWD 'node.zip'
                Invoke-WebRequest -UseBasicParsing -Uri {{PowerShellLiteral(artifact.DownloadUrl)}} -OutFile $zipPath
                $downloaded = $true
            }
            else {
                $zipPath = $cachedArtifact
            }
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
            if ($downloaded) {
                Remove-Item -LiteralPath $zipPath -Force
            }
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

    public IReadOnlyList<RuntimeStateFile> CreateStateFiles(
        RuntimeStateBindingContext context)
    {
        var stateDirectory = context.RuntimeStateDirectory;
        var globalPrefix = NodeRuntimeStateLayout
            .GetGlobalPrefix(stateDirectory)
            .Replace('\\', '/');
        var cacheDirectory = NodeRuntimeStateLayout
            .GetCacheDirectory(stateDirectory)
            .Replace('\\', '/');
        var nodeLocation = GetNodeDirectory(
            context.Manifest.Location);
        var npmrcContent =
            $"prefix={globalPrefix}{Environment.NewLine}" +
            $"cache={cacheDirectory}{Environment.NewLine}";
        return
        [
            new RuntimeStateFile(
                NodeRuntimeStateLayout.GetNpmrcPath(nodeLocation),
                npmrcContent),
            new RuntimeStateFile(
                NodeRuntimeStateLayout.GetNpmCommandPath(nodeLocation),
                CreateCommandShim(
                    "npm-cli.js",
                    globalPrefix,
                    cacheDirectory)),
            new RuntimeStateFile(
                NodeRuntimeStateLayout.GetNpxCommandPath(nodeLocation),
                CreateCommandShim(
                    "npx-cli.js",
                    globalPrefix,
                    cacheDirectory))
        ];
    }

    public string GetExecutableRelativePath(
        RuntimeArtifactDescriptor artifact,
        bool fromCache = false)
    {
        return Path.Combine(
            $"node-v{artifact.Version}-win-x64",
            "node.exe");
    }

    private static string PowerShellLiteral(string value)
    {
        return $"'{value.Replace("'", "''")}'";
    }

    private static string GetNodeDirectory(string location)
    {
        if (Directory.Exists(location))
        {
            return location;
        }

        var extension = Path.GetExtension(location);
        return File.Exists(location)
            || string.Equals(
                extension,
                ".exe",
                StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                extension,
                ".cmd",
                StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                extension,
                ".bat",
                StringComparison.OrdinalIgnoreCase)
            ? Path.GetDirectoryName(location) ?? location
            : location;
    }

    private static string CreateCommandShim(
        string cliFileName,
        string globalPrefix,
        string cacheDirectory)
    {
        return string.Join(
            Environment.NewLine,
            "@echo off",
            $"set \"NPM_CONFIG_PREFIX={globalPrefix}\"",
            $"set \"NPM_CONFIG_CACHE={cacheDirectory}\"",
            $"\"%~dp0node.exe\" \"%~dp0node_modules\\npm\\bin\\{cliFileName}\" %*",
            string.Empty);
    }
}
