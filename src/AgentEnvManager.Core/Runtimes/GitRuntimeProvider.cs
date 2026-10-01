using System.Text;
using AgentEnvManager.Core.Inspection;

namespace AgentEnvManager.Core.Runtimes;

public sealed class GitRuntimeProvider : IRuntimeProvider
{
    public const string ProviderId = "git";
    public const string OfficialSource =
        "https://github.com/git-for-windows/git/releases/tag/v2.56.0.windows.1";

    private const string ArtifactSha256 =
        "064b440ff870ed5198527e8f3a92cdf5bd2fd0fedf5e718af95e3fdaddeff718";
    private const string DownloadUrl =
        "https://github.com/git-for-windows/git/releases/download/v2.56.0.windows.1/MinGit-2.56.0-64-bit.zip";

    public RuntimeProviderDescriptor Descriptor { get; } = new(
        ProviderId,
        "Git",
        EnvironmentAssetKind.ToolRuntime,
        RuntimeProviderMode.Installable,
        DiscoverySourceInfo.RuntimeProvider,
        OfficialSource,
        "GPL-2.0",
        RuntimeInstallStrategy.OfficialArchive,
        [
            new RuntimeArtifactDescriptor(
                "2.56.0",
                OfficialSource,
                "GPL-2.0",
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
            $zipPath = Join-Path $PWD 'git.zip'
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
                throw "Git 制品哈希不匹配: $actualHash"
            }

            Expand-Archive -LiteralPath $zipPath -DestinationPath $PWD -Force
            Remove-Item -LiteralPath $zipPath -Force
            $content = "@echo off`r`n`"%~dp0cmd\git.exe`" %*`r`n"
            [IO.File]::WriteAllText(
                (Join-Path $PWD 'git.cmd'),
                $content,
                (New-Object Text.UTF8Encoding($false)))
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
        return "git.cmd";
    }

    public IReadOnlyList<RuntimeStateFile> CreateStateFiles(
        RuntimeStateBindingContext context)
    {
        return [];
    }

    private static string PowerShellLiteral(string value)
    {
        return $"'{value.Replace("'", "''")}'";
    }
}
