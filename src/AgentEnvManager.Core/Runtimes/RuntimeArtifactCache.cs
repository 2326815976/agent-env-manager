using System.Net.Http;
using System.Security.Cryptography;

namespace AgentEnvManager.Core.Runtimes;

public enum RuntimeArtifactSource
{
    Cache,
    Official,
    Mirror,
    Imported
}

public sealed record RuntimeArtifactCacheEntry(
    string Path,
    string Sha256,
    RuntimeArtifactSource Source,
    string SourceUrl,
    string? MirrorUrl,
    string VerificationResult,
    string? OperationId = null);

public interface IArtifactDownloader
{
    Task<Stream> OpenReadAsync(
        string url,
        CancellationToken cancellationToken = default);
}

public interface IRuntimeArtifactCache
{
    Task<RuntimeArtifactCacheEntry?> TryGetCachedAsync(
        RuntimeArtifactDescriptor artifact,
        CancellationToken cancellationToken = default);

    Task<RuntimeArtifactCacheEntry> AcquireAsync(
        RuntimeArtifactDescriptor artifact,
        string? mirrorUrl,
        CancellationToken cancellationToken = default);

    Task<RuntimeArtifactCacheEntry> ImportAsync(
        RuntimeArtifactDescriptor artifact,
        string sourcePath,
        CancellationToken cancellationToken = default);
}

public sealed class HttpArtifactDownloader : IArtifactDownloader
{
    private static readonly HttpClient Client = new();

    public async Task<Stream> OpenReadAsync(
        string url,
        CancellationToken cancellationToken = default)
    {
        var response = await Client.GetAsync(
            url,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStreamAsync(cancellationToken);
    }
}

public sealed class RuntimeArtifactCache(
    string cacheRoot,
    IArtifactDownloader downloader) : IRuntimeArtifactCache
{
    private readonly string _cacheRoot = Path.GetFullPath(cacheRoot);
    private readonly IArtifactDownloader _downloader = downloader;

    public async Task<RuntimeArtifactCacheEntry?> TryGetCachedAsync(
        RuntimeArtifactDescriptor artifact,
        CancellationToken cancellationToken = default)
    {
        var cachePath = GetCachePath(artifact);
        if (!File.Exists(cachePath))
        {
            return null;
        }

        var cachedHash = await ComputeSha256Async(
            cachePath,
            cancellationToken);
        if (!string.Equals(
            cachedHash,
            artifact.Sha256,
            StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(cachePath);
            return null;
        }

        return new RuntimeArtifactCacheEntry(
            cachePath,
            artifact.Sha256,
            RuntimeArtifactSource.Cache,
            artifact.DownloadUrl,
            null,
            "缓存制品通过官方 SHA-256 校验。");
    }

    public async Task<RuntimeArtifactCacheEntry> AcquireAsync(
        RuntimeArtifactDescriptor artifact,
        string? mirrorUrl,
        CancellationToken cancellationToken = default)
    {
        var cached = await TryGetCachedAsync(
            artifact,
            cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        var cachePath = GetCachePath(artifact);
        var sourceUrl = string.IsNullOrWhiteSpace(mirrorUrl)
            ? artifact.DownloadUrl
            : mirrorUrl;
        var source = string.IsNullOrWhiteSpace(mirrorUrl)
            ? RuntimeArtifactSource.Official
            : RuntimeArtifactSource.Mirror;
        var temporaryPath = await DownloadAndVerifyAsync(
            sourceUrl,
            artifact,
            cancellationToken);
        return Promote(
            temporaryPath,
            cachePath,
            artifact,
            source,
            sourceUrl,
            mirrorUrl,
            source == RuntimeArtifactSource.Mirror
                ? "镜像制品通过官方 SHA-256 校验。"
                : "官方制品通过 SHA-256 校验。");
    }

    public async Task<RuntimeArtifactCacheEntry> ImportAsync(
        RuntimeArtifactDescriptor artifact,
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        var fullSourcePath = Path.GetFullPath(sourcePath);
        if (!File.Exists(fullSourcePath))
        {
            throw new FileNotFoundException(
                "未找到要导入的运行时制品。",
                fullSourcePath);
        }

        var actualHash = await ComputeSha256Async(
            fullSourcePath,
            cancellationToken);
        if (!string.Equals(
            actualHash,
            artifact.Sha256,
            StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"导入制品哈希不匹配: 期望 {artifact.Sha256}，实际 {actualHash}。");
        }

        var cachePath = GetCachePath(artifact);
        if (!File.Exists(cachePath)
            || !string.Equals(
                await ComputeSha256Async(cachePath, cancellationToken),
                artifact.Sha256,
                StringComparison.OrdinalIgnoreCase))
        {
            Directory.CreateDirectory(
                Path.GetDirectoryName(cachePath)!);
            File.Copy(fullSourcePath, cachePath, overwrite: true);
        }

        return new RuntimeArtifactCacheEntry(
            cachePath,
            artifact.Sha256,
            RuntimeArtifactSource.Imported,
            fullSourcePath,
            null,
            "离线导入制品通过官方 SHA-256 校验。");
    }

    private async Task<string> DownloadAndVerifyAsync(
        string url,
        RuntimeArtifactDescriptor artifact,
        CancellationToken cancellationToken)
    {
        var temporaryDirectory = Path.Combine(_cacheRoot, ".tmp");
        Directory.CreateDirectory(temporaryDirectory);
        var temporaryPath = Path.Combine(
            temporaryDirectory,
            $"{Guid.NewGuid():N}.artifact");
        try
        {
            await using (var source = await _downloader.OpenReadAsync(
                url,
                cancellationToken))
            await using (var destination = File.Create(temporaryPath))
            {
                await source.CopyToAsync(destination, cancellationToken);
            }

            var actualHash = await ComputeSha256Async(
                temporaryPath,
                cancellationToken);
            if (!string.Equals(
                actualHash,
                artifact.Sha256,
                StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"制品哈希不匹配: 期望 {artifact.Sha256}，实际 {actualHash}。");
            }

            return temporaryPath;
        }
        catch
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }

            throw;
        }
    }

    private RuntimeArtifactCacheEntry Promote(
        string temporaryPath,
        string cachePath,
        RuntimeArtifactDescriptor artifact,
        RuntimeArtifactSource source,
        string sourceUrl,
        string? mirrorUrl,
        string verificationResult)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
        if (File.Exists(cachePath))
        {
            File.Delete(temporaryPath);
        }
        else
        {
            File.Move(temporaryPath, cachePath);
        }

        return new RuntimeArtifactCacheEntry(
            cachePath,
            artifact.Sha256,
            source,
            sourceUrl,
            mirrorUrl,
            verificationResult);
    }

    private string GetCachePath(RuntimeArtifactDescriptor artifact)
    {
        if (artifact.Sha256.Length < 2)
        {
            throw new InvalidOperationException("制品 SHA-256 无效。");
        }

        var normalizedHash = artifact.Sha256.ToLowerInvariant();
        var fileName = Path.GetFileName(
            new Uri(artifact.DownloadUrl, UriKind.Absolute).LocalPath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = $"{artifact.Version}.artifact";
        }

        return Path.Combine(
            _cacheRoot,
            normalizedHash[..2],
            normalizedHash,
            fileName);
    }

    private static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
