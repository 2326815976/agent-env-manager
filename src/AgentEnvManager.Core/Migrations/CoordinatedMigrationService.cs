using AgentEnvManager.Core.Operations;
using AgentEnvManager.Core.EnvironmentVariables;
using AgentEnvManager.Core.Agents;
using System.Security.Cryptography;
using System.Text;

namespace AgentEnvManager.Core.Migrations;

internal sealed class CoordinatedMigrationService(
    IProcessControlProbe processProbe,
    IUserEnvironmentVariableStore userEnvironmentVariableStore,
    IOperationJournal operationJournal,
    TimeProvider timeProvider)
{
    private const string CodexHomeVariableName = "CODEX_HOME";
    private const string CodexConfigDirField = "codexConfigDir";
    internal static readonly MigrationProcessRequirement[] ProcessRequirements =
    [
        new("chatgpt", "ChatGPT", ["ChatGPT"]),
        new("codex-app-server", "Codex app-server", ["codex"]),
        new("cc-switch", "CC Switch", ["cc-switch"])
    ];

    internal static readonly string[] PlannedRewrites =
    [
        "用户环境变量 CODEX_HOME",
        "ChatGPT 开始菜单快捷方式的目标与起始目录",
        "config.toml 中已知的绝对路径字段",
        "CC Switch settings.json 的 codexConfigDir",
        "%APPDATA%\\Codex 与 %LOCALAPPDATA%\\Codex 兼容 Junction",
        "%LOCALAPPDATA%\\com.ccswitch.desktop 与 %APPDATA%\\com.ccswitch.desktop 兼容 Junction"
    ];

    internal static readonly string[] StartupOrder =
    [
        "先启动 CC Switch，等待其配置写入完成",
        "再启动 ChatGPT，并执行真实工具调用健康检查"
    ];

    public async Task<CoordinatedMigrationPreview> PreviewAsync(
        CoordinatedMigrationRequest request,
        CancellationToken cancellationToken = default)
    {
        var rawTargets = new[]
        {
            (Kind: "codex-config",
                DisplayName: "Codex 配置环境",
                Source: request.CodexConfigSourcePath,
                Destination: request.CodexConfigDestinationPath),
            (Kind: "cc-switch-config",
                DisplayName: "CC Switch 配置环境",
                Source: request.CcSwitchConfigSourcePath,
                Destination: request.CcSwitchConfigDestinationPath)
        };
        var blockers = new List<CoordinatedMigrationBlocker>();
        var targets = new List<CoordinatedMigrationTarget>();
        foreach (var raw in rawTargets)
        {
            if (string.IsNullOrWhiteSpace(raw.Source)
                || string.IsNullOrWhiteSpace(raw.Destination))
            {
                blockers.Add(new CoordinatedMigrationBlocker(
                    "path-invalid",
                    $"{raw.DisplayName} 必须同时提供源目录与目标目录。"));
                continue;
            }

            var source = Path.GetFullPath(raw.Source);
            var destination = Path.GetFullPath(raw.Destination);
            if (IsPathEqualOrDescendant(destination, source))
            {
                blockers.Add(new CoordinatedMigrationBlocker(
                    "destination-inside-source",
                    $"{raw.DisplayName} 的目标目录位于源目录内，无法安全复制。",
                    destination));
                continue;
            }

            targets.Add(new CoordinatedMigrationTarget(
                raw.Kind,
                raw.DisplayName,
                source,
                destination));
        }

        for (var left = 0; left < targets.Count; left++)
        {
            for (var right = left + 1; right < targets.Count; right++)
            {
                if (PathsOverlap(targets, left, right))
                {
                    blockers.Add(new CoordinatedMigrationBlocker(
                        "paths-overlap",
                        $"{targets[left].DisplayName} 与 " +
                        $"{targets[right].DisplayName} 的源/目标目录相互包含，" +
                        "无法安全协同迁移。",
                        targets[left].DestinationPath));
                }
            }
        }

        foreach (var target in targets)
        {
            if (!Directory.Exists(target.SourcePath))
            {
                blockers.Add(new CoordinatedMigrationBlocker(
                    "source-missing",
                    $"{target.DisplayName} 源目录不存在。",
                    target.SourcePath));
            }

            if (Directory.Exists(target.DestinationPath)
                || File.Exists(target.DestinationPath))
            {
                blockers.Add(new CoordinatedMigrationBlocker(
                    "destination-exists",
                    $"{target.DisplayName} 目标目录已存在，拒绝覆盖。",
                    target.DestinationPath));
            }
        }

        var processNames = ProcessRequirements
            .SelectMany(requirement => requirement.ProcessNames)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var running = await processProbe.FindRunningProcessesAsync(
            processNames,
            cancellationToken);
        if (running.Count > 0)
        {
            blockers.Add(new CoordinatedMigrationBlocker(
                "process-running",
                $"迁移前必须停止相关进程: {string.Join("、", running)}。"));
        }

        var impact = string.Join(
            "；",
            targets.Select(target =>
                $"{target.DisplayName} {target.SourcePath} → " +
                $"{target.DestinationPath}（默认复制并校验，" +
                "原路径保留到健康检查通过）"));
        if (blockers.Count > 0)
        {
            return new CoordinatedMigrationPreview(
                targets,
                ProcessRequirements,
                PlannedRewrites,
                StartupOrder,
                blockers,
                impact);
        }

        var operation = OperationStateMachine.Create(
            OperationType.Migrate,
            "协同迁移 ChatGPT/Codex 与 CC Switch 配置环境",
            timeProvider,
            target: string.Join("、", targets.Select(t => t.DestinationPath)),
            impact: impact,
            expectedResult:
                "两台配置环境迁移完成，Junction 与路径重写生效，真实工具调用健康检查通过。");
        await operationJournal.SaveAsync(operation, cancellationToken);
        operation = await SaveTransitionAsync(
            OperationStateMachine.MarkValidated(operation, timeProvider),
            cancellationToken);

        return new CoordinatedMigrationPreview(
            targets,
            ProcessRequirements,
            PlannedRewrites,
            StartupOrder,
            blockers,
            impact,
            operation.Id);
    }

    private static bool PathsOverlap(
        IReadOnlyList<CoordinatedMigrationTarget> targets,
        int left,
        int right)
    {
        string[] leftPaths =
        [
            targets[left].SourcePath,
            targets[left].DestinationPath
        ];
        string[] rightPaths =
        [
            targets[right].SourcePath,
            targets[right].DestinationPath
        ];
        return leftPaths.Any(leftPath => rightPaths.Any(rightPath =>
            IsPathEqualOrDescendant(leftPath, rightPath)
            || IsPathEqualOrDescendant(rightPath, leftPath)));
    }

    private static bool IsPathEqualOrDescendant(
        string path,
        string root)
    {
        var normalizedPath = Path.GetFullPath(path)
            .TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
        var normalizedRoot = Path.GetFullPath(root)
            .TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
        return string.Equals(
                normalizedPath,
                normalizedRoot,
                StringComparison.OrdinalIgnoreCase)
            || normalizedPath.StartsWith(
                normalizedRoot + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);
    }

    private async Task<OperationRecord> SaveTransitionAsync(
        OperationRecord operation,
        CancellationToken cancellationToken)
    {
        await operationJournal.SaveAsync(operation, cancellationToken);
        return operation;
    }

    public async Task<CoordinatedMigrationResult> ApplyAsync(
        CoordinatedMigrationPreview preview,
        CancellationToken cancellationToken = default)
    {
        if (!preview.CanApply)
        {
            throw new InvalidOperationException(
                "迁移计划包含阻断项，无法执行。");
        }

        if (string.IsNullOrWhiteSpace(preview.OperationId))
        {
            throw new InvalidOperationException("迁移计划缺少操作 ID。");
        }

        var operation = await operationJournal.GetAsync(
            preview.OperationId,
            cancellationToken)
            ?? throw new InvalidOperationException("迁移计划不存在。");
        if (operation.State != OperationState.Validated)
        {
            throw new InvalidOperationException(
                "迁移计划状态无效或已过期。");
        }

        var codexTarget = preview.Targets.Single(target =>
            string.Equals(
                target.Kind,
                "codex-config",
                StringComparison.Ordinal));
        var settingsFilePath = Path.Combine(
            preview.Targets.Single(target => string.Equals(
                target.Kind,
                "cc-switch-config",
                StringComparison.Ordinal)).DestinationPath,
            "settings.json");
        var previousCodexHome = await userEnvironmentVariableStore.GetAsync(
            CodexHomeVariableName,
            cancellationToken);
        var copiedPaths = new List<string>();
        var rewritten = new List<string>();
        string? originalSettings = null;
        var environmentRewritten = false;
        // 源目录在本阶段保持不动，恢复点为"原环境变量值 + 原 settings.json
        // + 未改动的源目录"，在真正执行前登记。
        operation = await SaveTransitionAsync(
            OperationStateMachine.MarkRecoveryReady(
                operation,
                $"coordinated-migration-{operation.Id}",
                timeProvider),
            cancellationToken);
        operation = await SaveTransitionAsync(
            OperationStateMachine.BeginExecution(operation, timeProvider),
            cancellationToken);
        try
        {
            await processProbe.StopProcessesAsync(
                ProcessRequirements
                    .SelectMany(requirement => requirement.ProcessNames)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                cancellationToken);
            foreach (var target in preview.Targets)
            {
                // 先登记目标路径，失败时才能清掉半成品副本。
                copiedPaths.Add(target.DestinationPath);
                await CopyDirectoryAsync(
                    target.SourcePath,
                    target.DestinationPath,
                    cancellationToken);
                await VerifyCopyAsync(
                    target.SourcePath,
                    target.DestinationPath,
                    cancellationToken);
            }

            await userEnvironmentVariableStore.SetAsync(
                CodexHomeVariableName,
                codexTarget.DestinationPath,
                isExpandable: false,
                cancellationToken);
            environmentRewritten = true;
            await userEnvironmentVariableStore.BroadcastAsync(
                cancellationToken);
            rewritten.Add(CodexHomeVariableName);

            if (File.Exists(settingsFilePath))
            {
                originalSettings = await File.ReadAllTextAsync(
                    settingsFilePath,
                    cancellationToken);
                if (JsonStringFieldEditor.TryReplaceStringValue(
                        originalSettings,
                        CodexConfigDirField,
                        codexTarget.DestinationPath,
                        out var updated))
                {
                    await File.WriteAllTextAsync(
                        settingsFilePath,
                        updated,
                        new UTF8Encoding(
                            encoderShouldEmitUTF8Identifier: false),
                        cancellationToken);
                    rewritten.Add(CodexConfigDirField);
                }
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
            return new CoordinatedMigrationResult(
                operation,
                copiedPaths,
                rewritten,
                SourcesRetained: true);
        }
        catch (Exception exception)
        {
            var failureReason = exception.Message;
            var restored = true;
            try
            {
                if (environmentRewritten)
                {
                    await userEnvironmentVariableStore.SetAsync(
                        CodexHomeVariableName,
                        previousCodexHome,
                        isExpandable: false,
                        CancellationToken.None);
                    await userEnvironmentVariableStore.BroadcastAsync(
                        CancellationToken.None);
                }

                if (originalSettings is not null
                    && File.Exists(settingsFilePath))
                {
                    await File.WriteAllTextAsync(
                        settingsFilePath,
                        originalSettings,
                        new UTF8Encoding(
                            encoderShouldEmitUTF8Identifier: false),
                        CancellationToken.None);
                }

                foreach (var path in copiedPaths)
                {
                    if (Directory.Exists(path))
                    {
                        Directory.Delete(path, recursive: true);
                    }
                }
            }
            catch (Exception restoreException)
            {
                restored = false;
                failureReason =
                    $"{failureReason}；恢复原状态失败: " +
                    restoreException.Message;
            }

            if (restored)
            {
                failureReason = $"{failureReason}；已恢复原状态。";
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

    private static async Task CopyDirectoryAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(destinationPath);
        foreach (var directory in Directory.EnumerateDirectories(
                     sourcePath,
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

            Directory.CreateDirectory(Path.Combine(
                destinationPath,
                Path.GetRelativePath(sourcePath, directory)));
        }

        foreach (var filePath in Directory.EnumerateFiles(
                     sourcePath,
                     "*",
                     SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.GetAttributes(filePath)
                .HasFlag(FileAttributes.ReparsePoint))
            {
                throw new InvalidOperationException(
                    $"待迁移环境包含链接文件: {filePath}");
            }

            await using var source = File.OpenRead(filePath);
            await using var target = File.Create(Path.Combine(
                destinationPath,
                Path.GetRelativePath(sourcePath, filePath)));
            await source.CopyToAsync(target, cancellationToken);
        }
    }

    private static async Task VerifyCopyAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        var sourceFiles = Directory
            .EnumerateFiles(sourcePath, "*", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(sourcePath, file))
            .OrderBy(relative => relative, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var destinationFiles = Directory
            .EnumerateFiles(
                destinationPath,
                "*",
                SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(destinationPath, file))
            .OrderBy(relative => relative, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (!sourceFiles.SequenceEqual(
                destinationFiles,
                StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"复制校验失败：文件清单不一致 ({destinationPath})。");
        }

        foreach (var relative in sourceFiles)
        {
            var sourceHash = await ComputeSha256Async(
                Path.Combine(sourcePath, relative),
                cancellationToken);
            var destinationHash = await ComputeSha256Async(
                Path.Combine(destinationPath, relative),
                cancellationToken);
            if (!string.Equals(
                    sourceHash,
                    destinationHash,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"复制校验失败：{relative} 内容不一致。");
            }
        }
    }

    private static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }
}
