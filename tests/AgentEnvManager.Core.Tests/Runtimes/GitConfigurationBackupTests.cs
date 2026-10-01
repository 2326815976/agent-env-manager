using AgentEnvManager.Core.Runtimes;
using AgentEnvManager.Core.Operations;
using AgentEnvManager.Core.Inspection;
using AgentEnvManager.Core.Storage;
using AgentEnvManager.Core.Tests.TestSupport;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace AgentEnvManager.Core.Tests.Runtimes;

public sealed class GitConfigurationBackupTests
{
    [Fact]
    public async Task BackupAsync_requires_explicit_confirmation_and_excludes_sensitive_files()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"agent-env-manager-git-backup-tests-{Guid.NewGuid():N}");
        var userProfile = Path.Combine(root, "user");
        var backupRoot = Path.Combine(root, "backups");
        Directory.CreateDirectory(userProfile);
        File.WriteAllText(
            Path.Combine(userProfile, ".gitconfig"),
            "[user]\n\tname = Test\n");
        File.WriteAllText(
            Path.Combine(userProfile, ".git-credentials"),
            "https://user:secret@example.test\n");
        Directory.CreateDirectory(Path.Combine(userProfile, ".ssh"));
        File.WriteAllText(
            Path.Combine(userProfile, ".ssh", "id_rsa"),
            "PRIVATE KEY");
        File.WriteAllText(
            Path.Combine(userProfile, ".netrc"),
            "machine example.test password secret");
        var journal = new RecordingOperationJournal();
        var service = new GitConfigurationBackupService(
            userProfile,
            backupRoot,
            journal);

        try
        {
            var preview = await service.PreviewAsync();
            Assert.True(preview.IsSourcePresent);
            Assert.Equal(
                Path.Combine(userProfile, ".gitconfig"),
                preview.SourcePath);
            Assert.Contains(
                "不读取",
                preview.Impact,
                StringComparison.Ordinal);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.BackupAsync(
                    preview,
                    confirmed: false));
            Assert.False(Directory.Exists(backupRoot));
            var planned = await journal.GetAsync(preview.OperationId!);
            Assert.Equal(OperationState.RecoveryReady, planned!.State);

            var result = await service.BackupAsync(
                preview,
                confirmed: true);

            Assert.True(File.Exists(result.BackupPath));
            var plainBytes = await File.ReadAllBytesAsync(preview.SourcePath);
            var protectedBytes = await File.ReadAllBytesAsync(result.BackupPath);
            Assert.False(protectedBytes.AsSpan().SequenceEqual(plainBytes));
            var unprotectedBytes = ProtectedData.Unprotect(
                protectedBytes,
                ProtectorEntropy,
                DataProtectionScope.CurrentUser);
            Assert.Equal(
                plainBytes,
                unprotectedBytes);
            var backupFiles = Directory.GetFiles(
                backupRoot,
                "*",
                SearchOption.AllDirectories);
            Assert.Single(backupFiles);
            Assert.DoesNotContain(
                backupFiles,
                path => path.Contains(
                    "id_rsa",
                    StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(
                backupFiles,
                path => path.Contains(
                    ".git-credentials",
                    StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(
                backupFiles,
                path => path.Contains(
                    ".netrc",
                    StringComparison.OrdinalIgnoreCase));
            var completed = await journal.GetAsync(preview.OperationId!);
            Assert.Equal(OperationState.Succeeded, completed!.State);
            var previousBackup = await File.ReadAllBytesAsync(result.BackupPath);
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.BackupAsync(
                    preview,
                    confirmed: true));
            Assert.Equal(
                previousBackup,
                await File.ReadAllBytesAsync(result.BackupPath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SensitivePolicy_marks_credentials_and_ssh_paths()
    {
        var userProfile = @"C:\Users\Example";

        var protectedPaths = GitSensitiveDataPolicy.GetProtectedPaths(
            userProfile);

        Assert.Contains(
            Path.Combine(userProfile, ".ssh"),
            protectedPaths);
        Assert.Contains(
            Path.Combine(userProfile, ".git-credentials"),
            protectedPaths);
        Assert.Contains(
            Path.Combine(userProfile, ".netrc"),
            protectedPaths);
        Assert.True(GitSensitiveDataPolicy.IsProtectedPath(
            Path.Combine(userProfile, ".ssh", "id_rsa")));
        Assert.True(GitSensitiveDataPolicy.IsProtectedPath(
            Path.Combine(userProfile, ".git-credentials")));
        Assert.False(GitSensitiveDataPolicy.IsProtectedPath(
            Path.Combine(userProfile, ".gitconfig")));
    }

    [Fact]
    public async Task PreviewAsync_rejects_hard_link_to_private_key()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"agent-env-manager-git-hardlink-tests-{Guid.NewGuid():N}");
        var userProfile = Path.Combine(root, "user");
        Directory.CreateDirectory(Path.Combine(userProfile, ".ssh"));
        var privateKey = Path.Combine(
            userProfile,
            ".ssh",
            "id_rsa");
        File.WriteAllText(privateKey, "PRIVATE KEY");
        var gitConfig = Path.Combine(userProfile, ".gitconfig");
        Assert.True(CreateHardLink(
            gitConfig,
            privateKey,
            IntPtr.Zero));
        var service = new GitConfigurationBackupService(
            userProfile,
            Path.Combine(root, "backups"),
            new RecordingOperationJournal());

        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.PreviewAsync());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task PreviewAsync_rejects_inline_secret()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"agent-env-manager-git-secret-tests-{Guid.NewGuid():N}");
        var userProfile = Path.Combine(root, "user");
        Directory.CreateDirectory(userProfile);
        File.WriteAllText(
            Path.Combine(userProfile, ".gitconfig"),
            "[http]\n\textraheader = Authorization: Bearer secret\n");
        var service = new GitConfigurationBackupService(
            userProfile,
            Path.Combine(root, "backups"),
            new RecordingOperationJournal());

        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.PreviewAsync());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task PreviewRollbackAsync_creates_git_backup_rollback_plan()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"agent-env-manager-git-rollback-tests-{Guid.NewGuid():N}");
        var userProfile = Path.Combine(root, "user");
        var backupRoot = Path.Combine(root, "backups");
        Directory.CreateDirectory(userProfile);
        File.WriteAllText(
            Path.Combine(userProfile, ".gitconfig"),
            "[user]\n\tname = Test\n");
        var journal = new RecordingOperationJournal();
        var service = new GitConfigurationBackupService(
            userProfile,
            backupRoot,
            journal);
        var preview = await service.PreviewAsync();
        Directory.CreateDirectory(backupRoot);
        await File.WriteAllBytesAsync(preview.BackupPath, [1, 2, 3]);
        await journal.SaveAsync(new OperationRecord(
            preview.OperationId!,
            OperationType.GitConfigurationBackup,
            OperationState.Failed,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            "模拟失败的 Git 配置备份",
            preview.RecoveryPointId,
            "模拟失败",
            preview.BackupPath,
            "模拟 Git 配置备份影响范围"));
        var manager = new EnvironmentManager(
            new StubEnvironmentProbe(
                new EnvironmentProbeResult([], [])),
            operationJournal: journal,
            managerPaths: ManagerPaths.Resolve(root),
            gitConfigurationBackupService: service);

        try
        {
            var plan = await manager.PreviewRollbackAsync(
                preview.OperationId!);

            Assert.Equal(preview.BackupPath, plan.Target);
            Assert.Equal(preview.RecoveryPointId, plan.RecoveryPointId);
            Assert.Contains(
                "Git 配置备份",
                plan.Impact,
                StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateHardLink(
        string fileName,
        string existingFileName,
        IntPtr securityAttributes);

    private static readonly byte[] ProtectorEntropy =
        Encoding.UTF8.GetBytes("AgentEnvManager.GitConfigurationBackup");

    private sealed class StubEnvironmentProbe(EnvironmentProbeResult result)
        : IEnvironmentProbe
    {
        public Task<EnvironmentProbeResult> ProbeAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(result);
        }
    }
}
