namespace AgentEnvManager.Core.Runtimes;

public sealed record GitConfigurationBackupPreview(
    string SourcePath,
    string BackupPath,
    bool IsSourcePresent,
    string Impact);

public sealed record GitConfigurationBackupResult(
    string SourcePath,
    string BackupPath,
    DateTimeOffset CreatedAtUtc);

public interface IGitConfigurationBackupService
{
    Task<GitConfigurationBackupPreview> PreviewAsync(
        CancellationToken cancellationToken = default);

    Task<GitConfigurationBackupResult> BackupAsync(
        GitConfigurationBackupPreview preview,
        bool confirmed,
        CancellationToken cancellationToken = default);
}

public sealed class GitConfigurationBackupService(
    string userProfile,
    string backupRoot,
    TimeProvider? timeProvider = null) : IGitConfigurationBackupService
{
    private readonly string _userProfile = Path.GetFullPath(userProfile);
    private readonly string _backupRoot = Path.GetFullPath(backupRoot);
    private readonly TimeProvider _timeProvider =
        timeProvider ?? TimeProvider.System;

    public Task<GitConfigurationBackupPreview> PreviewAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var sourcePath = Path.Combine(_userProfile, ".gitconfig");
        var backupPath = Path.Combine(
            _backupRoot,
            $"{Guid.NewGuid():N}.gitconfig");
        return Task.FromResult(new GitConfigurationBackupPreview(
            sourcePath,
            backupPath,
            File.Exists(sourcePath),
            "只备份 .gitconfig；.ssh、Git 凭据和令牌文件不读取、不复制、不进入诊断包。"));
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

        var backupPath = Path.GetFullPath(preview.BackupPath);
        if (!IsUnder(backupPath, _backupRoot))
        {
            throw new InvalidOperationException(
                "Git 配置备份路径无效。");
        }

        Directory.CreateDirectory(_backupRoot);
        await using (var source = File.OpenRead(expectedSource))
        await using (var destination = File.Create(backupPath))
        {
            await source.CopyToAsync(destination, cancellationToken);
        }

        return new GitConfigurationBackupResult(
            expectedSource,
            backupPath,
            _timeProvider.GetUtcNow());
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
