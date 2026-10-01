using AgentEnvManager.Core.Runtimes;

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
        var service = new GitConfigurationBackupService(
            userProfile,
            backupRoot);

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

            var result = await service.BackupAsync(
                preview,
                confirmed: true);

            Assert.True(File.Exists(result.BackupPath));
            Assert.Equal(
                await File.ReadAllTextAsync(preview.SourcePath),
                await File.ReadAllTextAsync(result.BackupPath));
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
}
