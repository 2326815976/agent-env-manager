using System.Security.Cryptography;
using System.Text;
using AgentEnvManager.Core.Adoption;
using AgentEnvManager.Core.Inspection;

namespace AgentEnvManager.Core.Storage;

public sealed class FileSystemEnvironmentAssetHasher : IEnvironmentAssetHasher
{
    public async Task<string> ComputeHashAsync(
        EnvironmentAsset asset,
        CancellationToken cancellationToken = default)
    {
        var path = Path.GetFullPath(asset.Location);
        if (File.Exists(path))
        {
            return await HashFileAsync(path, cancellationToken);
        }

        if (Directory.Exists(path))
        {
            return await HashDirectoryAsync(path, cancellationToken);
        }

        throw new FileNotFoundException(
            "无法计算不存在的环境资产哈希。",
            path);
    }

    private static async Task<string> HashFileAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            useAsync: true);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }

    private static Task<string> HashDirectoryAsync(
        string root,
        CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var entries = Directory.EnumerateFileSystemEntries(
                root,
                "*",
                SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);

        foreach (var path in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entryName = Path.GetFileName(path)
                .Replace('/', '\\')
                .ToUpperInvariant();
            hash.AppendData(Encoding.UTF8.GetBytes(entryName));
            hash.AppendData([0]);
            var attributes = File.GetAttributes(path);
            hash.AppendData(Encoding.UTF8.GetBytes(
                attributes.HasFlag(FileAttributes.Directory)
                    ? "directory"
                    : "file"));
            hash.AppendData([0]);

            if (!attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                var info = new FileInfo(path);
                if (!attributes.HasFlag(FileAttributes.Directory))
                {
                    hash.AppendData(Encoding.UTF8.GetBytes(
                        info.Length.ToString()));
                    hash.AppendData([0]);
                }

                hash.AppendData(Encoding.UTF8.GetBytes(
                    info.LastWriteTimeUtc.Ticks.ToString()));
                hash.AppendData([0]);
            }
        }

        return Task.FromResult(
            Convert.ToHexString(hash.GetHashAndReset()));
    }
}
