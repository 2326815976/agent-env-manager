using System.Text;
using System.Text.Json;
using AgentEnvManager.Core.Inspection;

namespace AgentEnvManager.Core.Runtimes;

public sealed class PowerShellRuntimeProvider : IRuntimeProvider
{
    public const string ProviderId = "powershell";
    public const string OfficialSource =
        "https://github.com/PowerShell/PowerShell/releases/tag/v7.6.6";

    private const string ArtifactSha256 =
        "02fe458be20493fbdf43f61ea20610b811ee6c738ab1676c61b9cfcd1a33c860";
    private const string DownloadUrl =
        "https://github.com/PowerShell/PowerShell/releases/download/v7.6.6/PowerShell-7.6.6-win-x64.zip";

    public RuntimeProviderDescriptor Descriptor { get; } = new(
        ProviderId,
        "PowerShell 7",
        EnvironmentAssetKind.Shell,
        RuntimeProviderMode.Installable,
        DiscoverySourceInfo.RuntimeProvider,
        OfficialSource,
        "MIT",
        RuntimeInstallStrategy.OfficialArchive,
        [
            new RuntimeArtifactDescriptor(
                "7.6.6",
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
                $zipPath = Join-Path $PWD 'powershell.zip'
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
                throw "PowerShell 制品哈希不匹配: $actualHash"
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

    public string GetExecutableRelativePath(
        RuntimeArtifactDescriptor artifact,
        bool fromCache = false)
    {
        return "pwsh.exe";
    }

    public IReadOnlyList<RuntimeStateFile> CreateStateFiles(
        RuntimeStateBindingContext context)
    {
        var stateDirectory = Path.Combine(
            context.RuntimeStateDirectory,
            "PowerShell");
        var modulesDirectory = PowerShellRuntimeStateLayout
            .GetModulesDirectory(stateDirectory);
        var profilePath = PowerShellRuntimeStateLayout
            .GetProfilePath(stateDirectory);
        var configPath = Path.Combine(
            context.Manifest.Location,
            "powershell.config.json");
        var loaderPath = Path.Combine(
            context.Manifest.Location,
            "Profile.ps1");
        var configContent = JsonSerializer.Serialize(
            new Dictionary<string, string>
            {
                ["PSModulePath"] = modulesDirectory
            });
        return
        [
            new RuntimeStateFile(
                Path.Combine(modulesDirectory, ".keep"),
                string.Empty),
            new RuntimeStateFile(
                profilePath,
                "# AgentEnvManager managed PowerShell profile" +
                Environment.NewLine),
            new RuntimeStateFile(
                loaderPath,
                $". {PowerShellLiteral(profilePath)}{Environment.NewLine}"),
            new RuntimeStateFile(
                configPath,
                configContent)
        ];
    }

    private static string PowerShellLiteral(string value)
    {
        return $"'{value.Replace("'", "''")}'";
    }
}
