using System.Text;

namespace AgentEnvManager.Core.Runtimes;

internal enum OfficialArchiveFormat
{
    Zip,
    TarGz
}

internal static class OfficialArchiveInstaller
{
    public static RuntimeInstallCommand CreateCommand(
        RuntimeArtifactDescriptor artifact,
        RuntimeInstallContext context,
        OfficialArchiveFormat format,
        string? postExtractScript = null)
    {
        var archiveName = Path.GetFileName(
            new Uri(artifact.DownloadUrl).AbsolutePath);
        var extract = format == OfficialArchiveFormat.Zip
            ? $"Expand-Archive -LiteralPath $artifactPath " +
                $"-DestinationPath {Literal(context.InstallRoot)} -Force"
            : $"& tar.exe -xzf $artifactPath -C " +
                $"{Literal(context.InstallRoot)}{Environment.NewLine}" +
                "if ($LASTEXITCODE -ne 0) { throw \"解压制品失败。\" }";
        var script = $$"""
            $ErrorActionPreference = 'Stop'
            $ProgressPreference = 'SilentlyContinue'
            [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
            $downloaded = $false
            $artifactPath = {{Literal(context.CachedArtifactPath ?? string.Empty)}}
            if ([string]::IsNullOrWhiteSpace($artifactPath)) {
                $artifactPath = Join-Path $env:TEMP ("aem-" + [Guid]::NewGuid().ToString('N') + "-" + {{Literal(archiveName)}})
                Invoke-WebRequest -UseBasicParsing -Uri {{Literal(artifact.DownloadUrl)}} -OutFile $artifactPath
                $downloaded = $true
            }
            $sha256 = [Security.Cryptography.SHA256]::Create()
            $stream = [IO.File]::OpenRead($artifactPath)
            try {
                $hashBytes = $sha256.ComputeHash($stream)
            }
            finally {
                $stream.Dispose()
                $sha256.Dispose()
            }
            $actualHash = [BitConverter]::ToString($hashBytes).Replace('-', '').ToLowerInvariant()
            if ($actualHash -ne {{Literal(artifact.Sha256)}}) {
                throw "制品哈希不匹配: $actualHash"
            }

            {{extract}}
            {{postExtractScript ?? string.Empty}}
            if ($downloaded) {
                Remove-Item -LiteralPath $artifactPath -Force
            }
            """;
        return new RuntimeInstallCommand(
            "powershell.exe",
            [
                "-NoLogo",
                "-NoProfile",
                "-NonInteractive",
                "-EncodedCommand",
                Convert.ToBase64String(
                    Encoding.Unicode.GetBytes(script))
            ],
            context.InstallRoot);
    }

    internal static string Literal(string value)
    {
        return $"'{value.Replace("'", "''")}'";
    }
}
