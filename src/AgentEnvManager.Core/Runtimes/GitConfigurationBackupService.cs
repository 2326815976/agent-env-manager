using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;
using AgentEnvManager.Core.Operations;

namespace AgentEnvManager.Core.Runtimes;

public sealed record GitConfigurationBackupPreview(
    string SourcePath,
    string BackupPath,
    bool IsSourcePresent,
    string Impact,
    string? OperationId = null,
    string? RecoveryPointId = null);

public sealed record GitConfigurationBackupResult(
    string SourcePath,
    string BackupPath,
    DateTimeOffset CreatedAtUtc,
    string OperationId);

public interface IGitConfigurationBackupService
{
    Task<GitConfigurationBackupPreview> PreviewAsync(
        CancellationToken cancellationToken = default);

    Task<GitConfigurationBackupResult> BackupAsync(
        GitConfigurationBackupPreview preview,
        bool confirmed,
        CancellationToken cancellationToken = default);

    Task<OperationRecord> RollbackAsync(
        string operationId,
        CancellationToken cancellationToken = default);
}

public sealed class GitConfigurationBackupService(
    string userProfile,
    string backupRoot,
    IOperationJournal operationJournal,
    TimeProvider? timeProvider = null) : IGitConfigurationBackupService
{
    private readonly string _userProfile = Path.GetFullPath(userProfile);
    private readonly string _backupRoot = Path.GetFullPath(backupRoot);
    private readonly TimeProvider _timeProvider =
        timeProvider ?? TimeProvider.System;

    private const uint FileAttributeReparsePoint = 0x00000400;
    private static readonly byte[] ProtectorEntropy =
        Encoding.UTF8.GetBytes("AgentEnvManager.GitConfigurationBackup");

    public async Task<GitConfigurationBackupPreview> PreviewAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var sourcePath = Path.Combine(_userProfile, ".gitconfig");
        var backupPath = Path.Combine(
            _backupRoot,
            $"{Guid.NewGuid():N}.gitconfig");
        if (!File.Exists(sourcePath))
        {
            return new GitConfigurationBackupPreview(
                sourcePath,
                backupPath,
                IsSourcePresent: false,
                "未找到 .gitconfig，无需备份。");
        }

        ValidateSourcePath(sourcePath);
        await ValidateSourceContentAsync(sourcePath, cancellationToken);
        var impact =
            "只备份 .gitconfig 并使用 Windows DPAPI 加密；" +
            ".ssh、Git 凭据和令牌文件不读取、不复制、不进入诊断包。";
        var operation = OperationStateMachine.Create(
            OperationType.GitConfigurationBackup,
            "备份 Git 配置",
            _timeProvider,
            target: backupPath,
            impact: impact,
            expectedResult: "Git 配置备份创建成功且可回滚。");
        await operationJournal.SaveAsync(operation, cancellationToken);
        operation = await SaveTransitionAsync(
                OperationStateMachine.MarkValidated(
                    operation,
                    _timeProvider),
                cancellationToken);
        var recoveryPointId = $"git-config-backup-{operation.Id}";
        operation = await SaveTransitionAsync(
                OperationStateMachine.MarkRecoveryReady(
                    operation,
                    recoveryPointId,
                    _timeProvider),
                cancellationToken);
        return new GitConfigurationBackupPreview(
            sourcePath,
            backupPath,
            IsSourcePresent: true,
            impact,
            operation.Id,
            recoveryPointId);
    }

    public async Task<GitConfigurationBackupResult> BackupAsync(
        GitConfigurationBackupPreview preview,
        bool confirmed,
        CancellationToken cancellationToken = default)
    {
        if (!confirmed)
        {
            throw new InvalidOperationException(
                "备份 Git 配置需要用户明确确认。");
        }

        if (!preview.IsSourcePresent
            || string.IsNullOrWhiteSpace(preview.OperationId)
            || string.IsNullOrWhiteSpace(preview.RecoveryPointId))
        {
            throw new InvalidOperationException(
                "Git 配置备份计划无效或缺少源文件。");
        }

        var expectedSource = Path.GetFullPath(
            Path.Combine(_userProfile, ".gitconfig"));
        if (!PathsEqual(preview.SourcePath, expectedSource)
            || GitSensitiveDataPolicy.IsProtectedPath(preview.SourcePath))
        {
            throw new InvalidOperationException(
                "Git 配置备份预览已过期或包含敏感路径。");
        }

        if (!File.Exists(expectedSource))
        {
            throw new FileNotFoundException(
                "未找到要备份的 Git 配置。",
                expectedSource);
        }

        ValidateSourcePath(expectedSource);
        var sourceBytes = await File.ReadAllBytesAsync(
            expectedSource,
            cancellationToken);
        ValidateSourceContent(sourceBytes);
        var operation = await operationJournal.GetAsync(
            preview.OperationId,
            cancellationToken)
            ?? throw new InvalidOperationException(
                "Git 配置备份计划不存在。");
        if (operation.Type != OperationType.GitConfigurationBackup
            || operation.State != OperationState.RecoveryReady
            || !string.Equals(
                operation.RecoveryPointId,
                preview.RecoveryPointId,
                StringComparison.Ordinal)
            || !PathsEqual(
                operation.Target
                    ?? throw new InvalidOperationException(
                        "Git 配置备份计划缺少目标。"),
                preview.BackupPath))
        {
            throw new InvalidOperationException(
                "Git 配置备份计划状态无效或已过期。");
        }

        var backupPath = Path.GetFullPath(preview.BackupPath);
        if (!IsUnder(backupPath, _backupRoot))
        {
            throw new InvalidOperationException(
                "Git 配置备份路径无效。");
        }

        Directory.CreateDirectory(_backupRoot);
        operation = await SaveTransitionAsync(
            OperationStateMachine.BeginExecution(
                operation,
                _timeProvider),
            cancellationToken);
        var backupCreated = false;
        try
        {
            var protectedBytes = ProtectedData.Protect(
                sourceBytes,
                ProtectorEntropy,
                DataProtectionScope.CurrentUser);
            await using (var destination = new FileStream(
                backupPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None))
            {
                backupCreated = true;
                await destination.WriteAsync(
                    protectedBytes,
                    cancellationToken);
            }

            operation = await SaveTransitionAsync(
                OperationStateMachine.BeginVerification(
                    operation,
                    _timeProvider),
                cancellationToken);
            if (!File.Exists(backupPath))
            {
                throw new InvalidOperationException(
                    "Git 配置备份验证失败。");
            }

            var writtenBytes = await File.ReadAllBytesAsync(
                backupPath,
                cancellationToken);
            var unprotectedBytes = ProtectedData.Unprotect(
                writtenBytes,
                ProtectorEntropy,
                DataProtectionScope.CurrentUser);
            if (!unprotectedBytes.AsSpan().SequenceEqual(sourceBytes))
            {
                throw new InvalidOperationException(
                    "Git 配置备份验证失败。");
            }

            operation = OperationStateMachine.Complete(
                operation,
                _timeProvider);
            await operationJournal.SaveAsync(
                operation,
                cancellationToken);
        }
        catch (Exception exception)
        {
            if (backupCreated && File.Exists(backupPath))
            {
                File.Delete(backupPath);
            }

            operation = OperationStateMachine.Fail(
                operation,
                exception.Message,
                _timeProvider);
            await operationJournal.SaveAsync(
                operation,
                CancellationToken.None);
            operation = OperationStateMachine.Rollback(
                operation,
                _timeProvider);
            await operationJournal.SaveAsync(
                operation,
                CancellationToken.None);
            throw;
        }

        return new GitConfigurationBackupResult(
            expectedSource,
            backupPath,
            _timeProvider.GetUtcNow(),
            operation.Id);
    }

    public async Task<OperationRecord> RollbackAsync(
        string operationId,
        CancellationToken cancellationToken = default)
    {
        var operation = await operationJournal.GetAsync(
            operationId,
            cancellationToken)
            ?? throw new KeyNotFoundException("未找到操作记录。");
        if (operation.Type != OperationType.GitConfigurationBackup)
        {
            throw new InvalidOperationException(
                "该操作不是 Git 配置备份操作。");
        }

        if (operation.State is OperationState.Succeeded
            or OperationState.RolledBack)
        {
            operation = OperationStateMachine.Reject(
                operation,
                OperationState.RolledBack,
                "该操作当前状态不能回滚。",
                _timeProvider);
            await operationJournal.SaveAsync(
                operation,
                CancellationToken.None);
            throw new InvalidOperationException(
                "该操作当前状态不能回滚。");
        }

        if (!string.IsNullOrWhiteSpace(operation.Target)
            && File.Exists(operation.Target))
        {
            var backupPath = Path.GetFullPath(operation.Target);
            if (!IsUnder(backupPath, _backupRoot))
            {
                throw new InvalidOperationException(
                    "Git 配置备份路径无效。");
            }

            File.Delete(backupPath);
        }

        if (operation.State != OperationState.Failed)
        {
            operation = OperationStateMachine.Fail(
                operation,
                "用户请求回滚 Git 配置备份。",
                _timeProvider);
            await operationJournal.SaveAsync(
                operation,
                cancellationToken);
        }

        operation = OperationStateMachine.Rollback(
            operation,
            _timeProvider);
        await operationJournal.SaveAsync(
            operation,
            cancellationToken);
        return operation;
    }

    private async Task<OperationRecord> SaveTransitionAsync(
        OperationRecord operation,
        CancellationToken cancellationToken)
    {
        await operationJournal.SaveAsync(operation, cancellationToken);
        return operation;
    }

    private static void ValidateSourcePath(string sourcePath)
    {
        var attributes = File.GetAttributes(sourcePath);
        if ((attributes & FileAttributes.ReparsePoint) != 0
            || HasMultipleLinks(sourcePath))
        {
            throw new InvalidOperationException(
                "拒绝备份链接形式的 Git 配置。");
        }
    }

    private static async Task ValidateSourceContentAsync(
        string sourcePath,
        CancellationToken cancellationToken)
    {
        var bytes = await File.ReadAllBytesAsync(
            sourcePath,
            cancellationToken);
        ValidateSourceContent(bytes);
    }

    private static void ValidateSourceContent(byte[] bytes)
    {
        var content = Encoding.Latin1.GetString(bytes);
        if (GitSensitiveDataPolicy.ContainsPotentialSecret(content))
        {
            throw new InvalidOperationException(
                "Git 配置可能包含明文凭据或令牌，拒绝备份。");
        }
    }

    private static bool HasMultipleLinks(string path)
    {
        using var handle = File.OpenHandle(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        if (!GetFileInformationByHandle(
            handle,
            out var information))
        {
            throw new IOException(
                $"无法读取 Git 配置链接信息: {path}");
        }

        if ((information.FileAttributes & FileAttributeReparsePoint) != 0)
        {
            return true;
        }

        return information.NumberOfLinks > 1;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileInformationByHandle(
        SafeFileHandle fileHandle,
        out ByHandleFileInformation fileInformation);

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    private static bool IsUnder(string path, string root)
    {
        var normalizedRoot = root.EndsWith(
            Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        return path.StartsWith(
            normalizedRoot,
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            StringComparison.OrdinalIgnoreCase);
    }
}
