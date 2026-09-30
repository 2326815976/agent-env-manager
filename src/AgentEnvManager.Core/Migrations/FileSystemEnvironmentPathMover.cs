using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace AgentEnvManager.Core.Migrations;

public sealed class FileSystemEnvironmentPathMover : IEnvironmentPathMover
{
    public void ValidateSameVolume(
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
            throw new InvalidOperationException(
                "同卷迁移要求源路径和目标路径位于同一卷。");
        }
    }

    public Task MoveAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateSameVolume(sourcePath, destinationPath);
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

    public Task MoveBackAsync(
        string currentPath,
        string originalPath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var current = Path.GetFullPath(currentPath);
        var original = Path.GetFullPath(originalPath);
        if (!Directory.Exists(current))
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

    [DllImport(
        "kernel32.dll",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumePathName(
        string fileName,
        StringBuilder volumePathName,
        int bufferLength);
}
