using System.Diagnostics;
using AgentEnvManager.Core.Operations;

namespace AgentEnvManager.Core.Agents;

internal sealed class CcSwitchConfigurationService(
    IAgentConfigurationBackupStore backupStore,
    IOperationJournal operationJournal,
    TimeProvider timeProvider,
    Func<Environment.SpecialFolder, string>? getFolderPath = null,
    Func<string, bool>? isReparsePoint = null,
    Func<string, string?>? readEnvironmentVariable = null,
    Func<string, string?>? resolveShortcut = null)
{
    internal const string AgentName = "CC Switch";
    private const string SettingsFileName = "settings.json";
    private const string CodexConfigDirField = "codexConfigDir";
    private const string WebViewLocalName = "com.ccswitch.desktop";

    private readonly Func<Environment.SpecialFolder, string> _getFolderPath =
        getFolderPath ?? Environment.GetFolderPath;
    private readonly Func<string, bool> _isReparsePoint =
        isReparsePoint ?? IsReparsePoint;
    private readonly Func<string, string?> _readEnvironmentVariable =
        readEnvironmentVariable ?? Environment.GetEnvironmentVariable;
    private readonly Func<string, string?> _resolveShortcut =
        resolveShortcut ?? WindowsShortcutResolver.Resolve;

    public async Task<CcSwitchDiscovery> DiscoverAsync(
        CcSwitchDiscoveryRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        var executable = string.IsNullOrWhiteSpace(request?.Executable)
            ? FindExecutable()
            : Path.GetFullPath(request!.Executable!);
        var configRoot = ResolveConfigRoot(request?.ConfigRoot);
        var settingsFilePath = Path.Combine(configRoot, SettingsFileName);
        string? codexConfigDirectory = null;
        if (File.Exists(settingsFilePath))
        {
            codexConfigDirectory = JsonStringFieldEditor.ReadStringValue(
                await File.ReadAllTextAsync(
                    settingsFilePath,
                    cancellationToken),
                CodexConfigDirField);
        }

        var isInstalled = executable is not null;
        return new CcSwitchDiscovery(
            isInstalled,
            executable,
            ReadVersion(executable),
            configRoot,
            settingsFilePath,
            codexConfigDirectory,
            GetCompatibilityJunctions(request),
            isInstalled
                ? File.Exists(settingsFilePath)
                    ? $"已发现 CC Switch，codexConfigDir={codexConfigDirectory ?? "未设置"}。"
                    : "已发现 CC Switch 可执行文件，但缺少 settings.json。"
                : "未完整发现 CC Switch 可执行文件或 settings.json。");
    }

    public async Task<CcSwitchBindingPreview> PreviewAsync(
        string configRoot,
        string targetCodexConfigDirectory,
        CancellationToken cancellationToken = default)
    {
        if (!Path.IsPathFullyQualified(targetCodexConfigDirectory))
        {
            throw new InvalidOperationException(
                "Codex 配置环境必须是完全限定路径。");
        }

        var target = Path.GetFullPath(targetCodexConfigDirectory)
            .TrimEnd('\\', '/');
        var pathRoot = Path.GetPathRoot(target);
        if (string.IsNullOrWhiteSpace(pathRoot)
            || string.Equals(
                target,
                pathRoot.TrimEnd('\\', '/'),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Codex 配置环境不能是磁盘根目录。");
        }

        var settingsFilePath = Path.Combine(configRoot, SettingsFileName);
        if (!File.Exists(settingsFilePath))
        {
            throw new FileNotFoundException(
                "未找到 CC Switch settings.json。",
                settingsFilePath);
        }

        var content = await File.ReadAllTextAsync(
            settingsFilePath,
            cancellationToken);
        var current = JsonStringFieldEditor.ReadStringValue(
            content,
            CodexConfigDirField);
        var isAlreadyBound = string.Equals(
            current,
            target,
            StringComparison.OrdinalIgnoreCase);
        var impact =
            $"只改写 {settingsFilePath} 的 {CodexConfigDirField} 字段；" +
            "其余字段、注释与 provider 内容保持不变。";
        var operation = OperationStateMachine.Create(
            OperationType.AgentBinding,
            $"绑定 {AgentName} 的 Codex 配置环境",
            timeProvider,
            target: settingsFilePath,
            impact: impact,
            expectedResult: $"{CodexConfigDirField} 指向 {target}。");
        await operationJournal.SaveAsync(operation, cancellationToken);
        operation = await SaveTransitionAsync(
            OperationStateMachine.MarkValidated(operation, timeProvider),
            cancellationToken);
        var recoveryPoint = await backupStore.CreateAsync(
            AgentName,
            [settingsFilePath],
            cancellationToken);
        operation = await SaveTransitionAsync(
            OperationStateMachine.MarkRecoveryReady(
                operation,
                recoveryPoint.Id,
                timeProvider),
            cancellationToken);

        return new CcSwitchBindingPreview(
            settingsFilePath,
            CodexConfigDirField,
            current,
            target,
            impact,
            [
                "未知字段",
                "注释与格式",
                "provider 密钥与配置内容",
                "其他设置项"
            ],
            GetCompatibilityJunctions(request: null),
            operation.Id,
            recoveryPoint.Id,
            isAlreadyBound);
    }

    public async Task<CcSwitchBindingResult> ApplyAsync(
        CcSwitchBindingPreview preview,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(preview.OperationId)
            || string.IsNullOrWhiteSpace(preview.RecoveryPointId))
        {
            throw new InvalidOperationException("绑定计划缺少操作或恢复点。");
        }

        var operation = await operationJournal.GetAsync(
            preview.OperationId,
            cancellationToken)
            ?? throw new InvalidOperationException("绑定计划不存在。");
        if (operation.State != OperationState.RecoveryReady)
        {
            throw new InvalidOperationException("绑定计划状态无效或已过期。");
        }

        var content = await File.ReadAllTextAsync(
            preview.SettingsFilePath,
            cancellationToken);
        var current = JsonStringFieldEditor.ReadStringValue(
            content,
            preview.FieldName);
        if (!string.Equals(
                current,
                preview.CurrentValue,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "settings.json 已变化，请重新预览。");
        }

        var recoveryPoint = await backupStore.GetAsync(
            preview.RecoveryPointId,
            cancellationToken)
            ?? throw new InvalidOperationException(
                "恢复点不存在，请重新预览。");
        operation = await SaveTransitionAsync(
            OperationStateMachine.BeginExecution(operation, timeProvider),
            cancellationToken);
        var executed = false;
        try
        {
            if (!JsonStringFieldEditor.TryReplaceStringValue(
                    content,
                    preview.FieldName,
                    preview.TargetValue,
                    out var updated))
            {
                throw new InvalidOperationException(
                    $"settings.json 缺少 {preview.FieldName} 字段。");
            }

            executed = true;
            await WriteAtomicAsync(
                preview.SettingsFilePath,
                updated,
                cancellationToken);
            var verified = JsonStringFieldEditor.ReadStringValue(
                await File.ReadAllTextAsync(
                    preview.SettingsFilePath,
                    cancellationToken),
                preview.FieldName);
            if (!string.Equals(
                    verified,
                    preview.TargetValue,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"{preview.FieldName} 验证失败。");
            }

            operation = await SaveTransitionAsync(
                OperationStateMachine.BeginVerification(
                    operation,
                    timeProvider),
                cancellationToken);
            operation = OperationStateMachine.Complete(
                operation,
                timeProvider);
            await operationJournal.SaveAsync(operation, cancellationToken);
            return new CcSwitchBindingResult(
                operation,
                recoveryPoint,
                await DiscoverAsync(
                    new CcSwitchDiscoveryRequest(
                        ConfigRoot: Path.GetDirectoryName(
                            preview.SettingsFilePath)),
                    cancellationToken));
        }
        catch (Exception exception)
        {
            var failureReason = exception.Message;
            var restored = false;
            if (executed)
            {
                try
                {
                    await backupStore.RestoreAsync(
                        recoveryPoint,
                        CancellationToken.None);
                    restored = true;
                    failureReason =
                        $"{failureReason}；已恢复原配置。";
                }
                catch (Exception restoreException)
                {
                    failureReason =
                        $"{failureReason}；恢复原配置失败: " +
                        restoreException.Message;
                }
            }

            operation = OperationStateMachine.Fail(
                operation,
                failureReason,
                timeProvider);
            await operationJournal.SaveAsync(operation, CancellationToken.None);
            if (restored)
            {
                operation = OperationStateMachine.Rollback(
                    operation,
                    timeProvider);
                await operationJournal.SaveAsync(
                    operation,
                    CancellationToken.None);
            }

            throw new InvalidOperationException(failureReason, exception);
        }
    }

    private static async Task WriteAtomicAsync(
        string path,
        string content,
        CancellationToken cancellationToken)
    {
        var temporaryPath = $"{path}.agent-env-manager.tmp";
        await File.WriteAllTextAsync(
            temporaryPath,
            content,
            new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            cancellationToken);
        File.Move(temporaryPath, path, overwrite: true);
    }

    private string ResolveConfigRoot(string? configRootOverride)
    {
        if (!string.IsNullOrWhiteSpace(configRootOverride))
        {
            return Path.GetFullPath(configRootOverride);
        }

        var profile = _getFolderPath(
            Environment.SpecialFolder.UserProfile);
        return Path.GetFullPath(Path.Combine(profile, ".cc-switch"));
    }

    private string? FindExecutable()
    {
        var configured = _readEnvironmentVariable("CC_SWITCH_EXECUTABLE");
        if (IsExistingFile(configured))
        {
            return Path.GetFullPath(configured!);
        }

        foreach (var candidate in GetExecutableCandidates())
        {
            if (IsExistingFile(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        var shortcut = Path.Combine(
            _getFolderPath(Environment.SpecialFolder.ApplicationData),
            "Microsoft",
            "Windows",
            "Start Menu",
            "Programs",
            "CC Switch",
            "CC Switch.lnk");
        if (File.Exists(shortcut))
        {
            var target = _resolveShortcut(shortcut);
            if (IsExistingFile(target))
            {
                return Path.GetFullPath(target!);
            }
        }

        return null;
    }

    private IReadOnlyList<string> GetExecutableCandidates()
    {
        var localAppData = _getFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        var programFiles = _getFolderPath(
            Environment.SpecialFolder.ProgramFiles);
        return
        [
            Path.Combine(localAppData, "Programs", "CC Switch", "cc-switch.exe"),
            Path.Combine(programFiles, "CC Switch", "cc-switch.exe"),
            Path.Combine(programFiles, "cc-switch", "cc-switch.exe")
        ];
    }

    private IReadOnlyList<string> GetCompatibilityJunctions(
        CcSwitchDiscoveryRequest? request)
    {
        string[] candidates =
        [
            Path.Combine(
                request?.WebViewLocalRoot
                    ?? _getFolderPath(
                        Environment.SpecialFolder.LocalApplicationData),
                WebViewLocalName),
            Path.Combine(
                request?.WebViewRoamingRoot
                    ?? _getFolderPath(
                        Environment.SpecialFolder.ApplicationData),
                WebViewLocalName)
        ];
        return candidates
            .Where(candidate => _isReparsePoint(candidate))
            .ToArray();
    }

    private static string? ReadVersion(string? executable)
    {
        if (string.IsNullOrWhiteSpace(executable))
        {
            return null;
        }

        try
        {
            return FileVersionInfo.GetVersionInfo(executable).FileVersion;
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or ArgumentException)
        {
            return null;
        }
    }

    private static bool IsExistingFile(string? path)
    {
        return !string.IsNullOrWhiteSpace(path)
            && Path.IsPathFullyQualified(path)
            && File.Exists(path);
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            return (File.GetAttributes(path)
                & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException)
        {
            return false;
        }
    }

    private async Task<OperationRecord> SaveTransitionAsync(
        OperationRecord operation,
        CancellationToken cancellationToken)
    {
        await operationJournal.SaveAsync(operation, cancellationToken);
        return operation;
    }
}
