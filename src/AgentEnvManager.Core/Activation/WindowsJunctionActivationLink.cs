using System.Diagnostics;

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

        var directory = new DirectoryInfo(activationPath);
        if (!EntryExists(activationPath))
        {
            return Task.FromResult<string?>(null);
        }

        var target = directory.LinkTarget;
        return Task.FromResult(string.IsNullOrWhiteSpace(target)
            ? null
            : Path.GetFullPath(target));
    }

    public Task<bool> TargetExistsAsync(
        string targetPath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(
            !string.IsNullOrWhiteSpace(targetPath)
            && (Directory.Exists(targetPath) || File.Exists(targetPath)));
    }

    public string GetActivationTarget(string location)
    {
        var fullPath = Path.GetFullPath(location);
        if (Directory.Exists(fullPath))
        {
            return fullPath;
        }

        return File.Exists(fullPath)
            ? Path.GetDirectoryName(fullPath) ?? fullPath
            : fullPath;
    }

    public async Task SetTargetAsync(
        string activationPath,
        string targetPath,
        CancellationToken cancellationToken = default)
    {
        var fullActivationPath = Path.GetFullPath(activationPath);
        var fullTargetPath = GetActivationTarget(targetPath);
        var parent = Path.GetDirectoryName(fullActivationPath)
            ?? throw new InvalidOperationException("激活路径缺少父目录。");
        Directory.CreateDirectory(parent);

        var temporaryPath = $"{fullActivationPath}.switch-{Guid.NewGuid():N}";
        string? backupPath = null;
        try
        {
            await CreateJunctionAsync(
                temporaryPath,
                fullTargetPath,
                cancellationToken);

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
                Directory.Delete(backupPath);
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
    }

    public Task DeleteAsync(
        string activationPath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (EntryExists(activationPath))
        {
            EnsureReparsePoint(activationPath);
            Directory.Delete(activationPath);
        }

        return Task.CompletedTask;
    }

    private static async Task CreateJunctionAsync(
        string linkPath,
        string targetPath,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(
            Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe")
        {
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("/d");
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add("mklink");
        startInfo.ArgumentList.Add("/J");
        startInfo.ArgumentList.Add(linkPath);
        startInfo.ArgumentList.Add(targetPath);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("无法启动 Junction 创建进程。");
        var output = await process.StandardOutput.ReadToEndAsync(
            cancellationToken);
        var error = await process.StandardError.ReadToEndAsync(
            cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"创建 Junction 失败: {error}{output}");
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
}
