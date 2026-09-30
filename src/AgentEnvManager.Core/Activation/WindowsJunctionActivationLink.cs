using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace AgentEnvManager.Core.Activation;

public sealed class WindowsJunctionActivationLink : IEnvironmentActivationLink
{
    public Task<string?> GetTargetAsync(
        string activationPath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(activationPath))
        {
            return Task.FromResult<string?>(null);
        }

        var normalizedPath = NormalizePath(activationPath);
        var directory = new DirectoryInfo(normalizedPath);
        if (!EntryExists(normalizedPath))
        {
            return Task.FromResult<string?>(null);
        }

        var target = directory.LinkTarget;
        return Task.FromResult(string.IsNullOrWhiteSpace(target)
            ? null
            : NormalizePath(target));
    }

    public Task<bool> TargetExistsAsync(
        string targetPath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(
            !string.IsNullOrWhiteSpace(targetPath)
            && (Directory.Exists(NormalizePath(targetPath))
                || File.Exists(NormalizePath(targetPath))));
    }

    public string GetActivationTarget(string location)
    {
        var fullPath = NormalizePath(location);
        if (Directory.Exists(fullPath))
        {
            return fullPath;
        }

        return File.Exists(fullPath)
            ? Path.GetDirectoryName(fullPath) ?? fullPath
            : fullPath;
    }

    public Task SetTargetAsync(
        string activationPath,
        string targetPath,
        CancellationToken cancellationToken = default)
    {
        var fullActivationPath = NormalizePath(activationPath);
        var fullTargetPath = GetActivationTarget(targetPath);
        var parent = Path.GetDirectoryName(fullActivationPath)
            ?? throw new InvalidOperationException("激活路径缺少父目录。");
        Directory.CreateDirectory(parent);

        var temporaryPath = $"{fullActivationPath}.switch-{Guid.NewGuid():N}";
        string? backupPath = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            CreateJunction(temporaryPath, fullTargetPath);

            if (EntryExists(fullActivationPath))
            {
                EnsureReparsePoint(fullActivationPath);
                backupPath =
                    $"{fullActivationPath}.previous-{Guid.NewGuid():N}";
                Directory.Move(fullActivationPath, backupPath);
            }

            Directory.Move(temporaryPath, fullActivationPath);
            if (backupPath is not null)
            {
                TryDeleteJunction(backupPath);
            }
        }
        catch
        {
            if (backupPath is not null
                && EntryExists(backupPath)
                && !EntryExists(fullActivationPath))
            {
                Directory.Move(backupPath, fullActivationPath);
            }

            if (EntryExists(temporaryPath))
            {
                Directory.Delete(temporaryPath);
            }

            throw;
        }

        return Task.CompletedTask;
    }

    public Task DeleteAsync(
        string activationPath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalizedPath = NormalizePath(activationPath);
        if (EntryExists(normalizedPath))
        {
            EnsureReparsePoint(normalizedPath);
            Directory.Delete(normalizedPath);
        }

        return Task.CompletedTask;
    }

    private static void CreateJunction(
        string linkPath,
        string targetPath)
    {
        Directory.CreateDirectory(linkPath);
        var substituteName = $@"\??\{targetPath}";
        var substituteBytes = Encoding.Unicode.GetBytes(substituteName);
        var printBytes = Encoding.Unicode.GetBytes(targetPath);
        var pathBufferLength =
            substituteBytes.Length + sizeof(char)
            + printBytes.Length + sizeof(char);
        var reparseDataLength =
            8 + pathBufferLength;
        var bufferSize = 8 + reparseDataLength;
        var buffer = Marshal.AllocHGlobal(bufferSize);
        try
        {
            Marshal.WriteInt32(
                buffer,
                0,
                unchecked((int)IoReparseTagMountPoint));
            Marshal.WriteInt16(buffer, 4, checked((short)reparseDataLength));
            Marshal.WriteInt16(buffer, 6, 0);
            Marshal.WriteInt16(buffer, 8, 0);
            Marshal.WriteInt16(
                buffer,
                10,
                checked((short)substituteBytes.Length));
            Marshal.WriteInt16(
                buffer,
                12,
                checked((short)(substituteBytes.Length + sizeof(char))));
            Marshal.WriteInt16(
                buffer,
                14,
                checked((short)printBytes.Length));
            var pathBuffer = IntPtr.Add(buffer, 16);
            Marshal.Copy(
                substituteBytes,
                0,
                pathBuffer,
                substituteBytes.Length);
            Marshal.WriteInt16(
                pathBuffer,
                substituteBytes.Length,
                0);
            var printNameOffset = substituteBytes.Length + sizeof(char);
            Marshal.Copy(
                printBytes,
                0,
                IntPtr.Add(pathBuffer, printNameOffset),
                printBytes.Length);
            Marshal.WriteInt16(
                pathBuffer,
                printNameOffset + printBytes.Length,
                0);

            using var handle = CreateFile(
                linkPath,
                GenericWrite,
                FileShareRead | FileShareWrite | FileShareDelete,
                IntPtr.Zero,
                OpenExisting,
                FileFlagBackupSemantics | FileFlagOpenReparsePoint,
                IntPtr.Zero);
            if (handle.IsInvalid)
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "无法打开 Junction 目标目录。");
            }

            if (!DeviceIoControl(
                    handle,
                    FsctlSetReparsePoint,
                    buffer,
                    bufferSize,
                    IntPtr.Zero,
                    0,
                    out _,
                    IntPtr.Zero))
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "无法设置 Junction reparse point。");
            }
        }
        catch
        {
            if (Directory.Exists(linkPath))
            {
                Directory.Delete(linkPath);
            }

            throw;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static void TryDeleteJunction(string path)
    {
        try
        {
            Directory.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void EnsureReparsePoint(string path)
    {
        var attributes = File.GetAttributes(path);
        if (!attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            throw new InvalidOperationException(
                $"拒绝替换非 Junction 路径: {path}");
        }
    }

    private static bool EntryExists(string path)
    {
        try
        {
            _ = File.GetAttributes(path);
            return true;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }
    }

    private static string NormalizePath(string path)
    {
        const string localAppDataToken = "%LOCALAPPDATA%";
        var expandedPath = path.StartsWith(
            localAppDataToken,
            StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                path[localAppDataToken.Length..].TrimStart('\\', '/'))
            : path;

        return Path.GetFullPath(expandedPath);
    }

    private const uint GenericWrite = 0x40000000;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint FileShareDelete = 0x00000004;
    private const uint OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private const uint FileFlagOpenReparsePoint = 0x00200000;
    private const uint FsctlSetReparsePoint = 0x000900A4;
    private const uint IoReparseTagMountPoint = 0xA0000003;

    [DllImport(
        "kernel32.dll",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(
        SafeFileHandle device,
        uint controlCode,
        IntPtr inBuffer,
        int inBufferSize,
        IntPtr outBuffer,
        int outBufferSize,
        out int bytesReturned,
        IntPtr overlapped);
}
