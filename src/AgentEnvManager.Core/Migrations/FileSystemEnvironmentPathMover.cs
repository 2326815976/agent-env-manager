using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace AgentEnvManager.Core.Migrations;

public sealed class FileSystemEnvironmentPathMover : IEnvironmentPathMover
{
    public MigrationStrategy GetStrategy(
        string sourcePath,
        string destinationPath)
    {
        var source = Path.GetFullPath(sourcePath);
        var destination = Path.GetFullPath(destinationPath);
        var sourceVolume = GetVolumePath(source);
        var destinationVolume = GetVolumePath(destination);
        if (!string.Equals(
                sourceVolume,
                destinationVolume,
                StringComparison.OrdinalIgnoreCase))
        {
            return MigrationStrategy.CopyAndVerify;
        }

        return MigrationStrategy.AtomicRename;
    }

    public Task<MigrationPathStatistics> InspectAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        var source = Path.GetFullPath(sourcePath);
        if (!Directory.Exists(source))
        {
            throw new DirectoryNotFoundException(
                $"源环境目录不存在: {source}");
        }

        long fileCount = 0;
        long totalBytes = 0;
        foreach (var directory in Directory.EnumerateDirectories(
                     source,
                     "*",
                     SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.GetAttributes(directory)
                .HasFlag(FileAttributes.ReparsePoint))
            {
                throw new InvalidOperationException(
                    $"待迁移环境包含链接目录: {directory}");
            }
        }

        foreach (var filePath in Directory.EnumerateFiles(
                     source,
                     "*",
                     SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var attributes = File.GetAttributes(filePath);
            if (attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                throw new InvalidOperationException(
                    $"待迁移环境包含链接文件: {filePath}");
            }

            fileCount++;
            totalBytes = checked(
                totalBytes + new FileInfo(filePath).Length);
            try
            {
                using var stream = File.OpenRead(filePath);
            }
            catch (Exception exception)
                when (exception is IOException
                    or UnauthorizedAccessException)
            {
                throw new InvalidOperationException(
                    $"迁移前无法读取源文件: {filePath}",
                    exception);
            }
        }

        var destinationAncestor = FindExistingAncestor(
            Path.GetFullPath(destinationPath));
        if (!GetDiskFreeSpaceEx(
                destinationAncestor,
                out var availableBytes,
                out _,
                out _))
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "无法读取目标卷可用空间。");
        }

        return Task.FromResult(new MigrationPathStatistics(
            fileCount,
            totalBytes,
            checked((long)availableBytes)));
    }

    public Task MoveAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (GetStrategy(sourcePath, destinationPath)
            != MigrationStrategy.AtomicRename)
        {
            throw new InvalidOperationException(
                "跨卷路径不能使用原子重命名迁移。");
        }

        var source = Path.GetFullPath(sourcePath);
        var destination = Path.GetFullPath(destinationPath);
        if (!Directory.Exists(source))
        {
            throw new DirectoryNotFoundException(
                $"源环境目录不存在: {source}");
        }

        if (Directory.Exists(destination)
            || File.Exists(destination))
        {
            throw new IOException($"目标路径已存在: {destination}");
        }

        var parent = Path.GetDirectoryName(destination)
            ?? throw new InvalidOperationException("目标路径缺少父目录。");
        Directory.CreateDirectory(parent);
        Directory.Move(source, destination);
        return Task.CompletedTask;
    }

    public Task CopyAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        var source = Path.GetFullPath(sourcePath);
        var destination = Path.GetFullPath(destinationPath);
        if (!Directory.Exists(source))
        {
            throw new DirectoryNotFoundException(
                $"源环境目录不存在: {source}");
        }

        if (Directory.Exists(destination)
            || File.Exists(destination))
        {
            throw new IOException($"目标路径已存在: {destination}");
        }

        CopyDirectory(
            source,
            destination,
            cancellationToken);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fullPath = Path.GetFullPath(path);
        if (Directory.Exists(fullPath))
        {
            Directory.Delete(fullPath, recursive: true);
        }
        else if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }

        return Task.CompletedTask;
    }

    public Task MoveBackAsync(
        string currentPath,
        string originalPath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var current = Path.GetFullPath(currentPath);
        var original = Path.GetFullPath(originalPath);
        if (!Directory.Exists(current) && !File.Exists(current))
        {
            throw new DirectoryNotFoundException(
                $"待恢复的迁移目录不存在: {current}");
        }

        if (Directory.Exists(original) || File.Exists(original))
        {
            throw new IOException($"原路径已被占用: {original}");
        }

        var parent = Path.GetDirectoryName(original)
            ?? throw new InvalidOperationException("原路径缺少父目录。");
        Directory.CreateDirectory(parent);
        Directory.Move(current, original);
        return Task.CompletedTask;
    }

    private static string GetVolumePath(string path)
    {
        var existing = FindExistingAncestor(path);
        var buffer = new StringBuilder(260);
        if (!GetVolumePathName(existing, buffer, buffer.Capacity))
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "无法确定路径所在卷。");
        }

        return Path.GetFullPath(buffer.ToString()).TrimEnd('\\', '/');
    }

    private static string FindExistingAncestor(string path)
    {
        var current = Path.GetFullPath(path);
        while (!Directory.Exists(current)
            && !File.Exists(current))
        {
            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrWhiteSpace(parent)
                || string.Equals(
                    parent,
                    current,
                    StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetPathRoot(current) ?? current;
            }

            current = parent;
        }

        return current;
    }

    private static void CopyDirectory(
        string source,
        string destination,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(
                     source,
                     "*",
                     SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var attributes = File.GetAttributes(directory);
            if (attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                throw new InvalidOperationException(
                    $"待迁移环境包含链接目录: {directory}");
            }

            var relative = Path.GetRelativePath(source, directory);
            Directory.CreateDirectory(
                Path.Combine(destination, relative));
        }

        foreach (var filePath in Directory.EnumerateFiles(
                     source,
                     "*",
                     SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(source, filePath);
            var targetPath = Path.Combine(destination, relative);
            Directory.CreateDirectory(
                Path.GetDirectoryName(targetPath)!);
            File.Copy(filePath, targetPath, overwrite: false);
            File.SetLastWriteTimeUtc(
                targetPath,
                File.GetLastWriteTimeUtc(filePath));
        }

        foreach (var directory in Directory.EnumerateDirectories(
                     source,
                     "*",
                     SearchOption.AllDirectories)
                 .OrderByDescending(path => path.Length))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(source, directory);
            Directory.SetLastWriteTimeUtc(
                Path.Combine(destination, relative),
                Directory.GetLastWriteTimeUtc(directory));
        }

        Directory.SetLastWriteTimeUtc(
            destination,
            Directory.GetLastWriteTimeUtc(source));
    }

    [DllImport(
        "kernel32.dll",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumePathName(
        string fileName,
        StringBuilder volumePathName,
        int bufferLength);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetDiskFreeSpaceEx(
        string directoryName,
        out ulong freeBytesAvailable,
        out ulong totalNumberOfBytes,
        out ulong totalNumberOfFreeBytes);
}
