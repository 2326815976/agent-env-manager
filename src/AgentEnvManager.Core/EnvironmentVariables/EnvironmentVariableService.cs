using AgentEnvManager.Core.Operations;
using AgentEnvManager.Core.Adoption;

namespace AgentEnvManager.Core.EnvironmentVariables;

internal sealed class EnvironmentVariableService(
    IUserEnvironmentVariableStore store,
    string managedPathRoot,
    IEnvironmentVariableRecoveryPointStore recoveryPointStore,
    IEnvironmentManifestStore environmentManifestStore,
    IOperationJournal operationJournal,
    TimeProvider timeProvider)
{
    private const string ManagedVariablePrefix = "AGENT_ENV_MANAGER_";
    private readonly ManagedPathEditor _pathEditor =
        new(managedPathRoot);

    public async Task<EnvironmentVariableUpdatePreview> PreviewManagedPathUpdateAsync(
        IReadOnlyList<string> managedEntries,
        CancellationToken cancellationToken = default)
    {
        var manifests = await environmentManifestStore.ReadAllAsync(
            cancellationToken);
        var allowedEntries = manifests
            .Where(manifest =>
                !string.IsNullOrWhiteSpace(manifest.ManagedEntryPath))
            .Select(manifest => Path.GetFullPath(
                manifest.ManagedEntryPath))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in managedEntries)
        {
            var fullPath = Path.GetFullPath(entry);
            if (!allowedEntries.Contains(fullPath))
            {
                throw new InvalidOperationException(
                    $"PATH 条目未绑定到已纳管环境: {entry}");
            }
        }

        var originalPath = await store.GetAsync(
            "Path",
            cancellationToken);
        var desiredPath = _pathEditor.Apply(
            originalPath,
            managedEntries);
        var change = new EnvironmentVariableChange("Path", desiredPath);

        return new EnvironmentVariableUpdatePreview(
            new Dictionary<string, string?>(
                StringComparer.OrdinalIgnoreCase)
            {
                ["Path"] = originalPath
            },
            new Dictionary<string, string?>(
                StringComparer.OrdinalIgnoreCase)
            {
                ["Path"] = desiredPath
            },
            [change],
            "只更新管理器 shims 根目录下的 PATH 条目，保留未知条目及相对顺序。");
    }

    public async Task<EnvironmentVariableUpdatePreview> PreviewManagedVariablesAsync(
        IReadOnlyList<EnvironmentVariableChange> changes,
        CancellationToken cancellationToken = default)
    {
        var uniqueChanges = NormalizeManagedVariableChanges(changes);
        var originalValues = new Dictionary<string, string?>(
            StringComparer.OrdinalIgnoreCase);
        var desiredValues = new Dictionary<string, string?>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var change in uniqueChanges)
        {
            originalValues[change.Name] = await store.GetAsync(
                change.Name,
                cancellationToken);
            desiredValues[change.Name] = change.Value;
        }

        return new EnvironmentVariableUpdatePreview(
            originalValues,
            desiredValues,
            uniqueChanges,
            "只修改以 AGENT_ENV_MANAGER_ 开头的受管变量。");
    }

    public async Task<EnvironmentVariableTransactionResult> ApplyAsync(
        EnvironmentVariableUpdatePreview preview,
        CancellationToken cancellationToken = default)
    {
        ValidatePreview(preview);
        foreach (var original in preview.OriginalValues)
        {
            var current = await store.GetAsync(
                original.Key,
                cancellationToken);
            if (!string.Equals(
                    current,
                    original.Value,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"环境变量 {original.Key} 已变化，请重新预览。");
            }
        }

        var operation = OperationStateMachine.Create(
            OperationType.EnvironmentVariables,
            $"更新 {preview.Changes.Count} 个环境变量",
            timeProvider);
        await operationJournal.SaveAsync(operation, cancellationToken);
        var executionStarted = false;

        try
        {
            operation = await SaveTransitionAsync(
                OperationStateMachine.MarkValidated(operation, timeProvider),
                cancellationToken);
            var recoveryPoint = await recoveryPointStore.CreateAsync(
                operation.Id,
                preview.OriginalValues,
                cancellationToken);
            operation = await SaveTransitionAsync(
                OperationStateMachine.MarkRecoveryReady(
                    operation,
                    recoveryPoint.Id,
                    timeProvider),
                cancellationToken);
            operation = await SaveTransitionAsync(
                OperationStateMachine.BeginExecution(operation, timeProvider),
                cancellationToken);
            executionStarted = true;

            foreach (var change in preview.Changes)
            {
                await store.SetAsync(
                    change.Name,
                    change.Value,
                    cancellationToken);
            }

            await store.BroadcastAsync(cancellationToken);
            operation = await SaveTransitionAsync(
                OperationStateMachine.BeginVerification(
                    operation,
                    timeProvider),
                cancellationToken);
            foreach (var desired in preview.DesiredValues)
            {
                var current = await store.GetAsync(
                    desired.Key,
                    cancellationToken);
                if (!string.Equals(
                        current,
                        desired.Value,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"环境变量 {desired.Key} 验证失败。");
                }
            }

            var completed = OperationStateMachine.Complete(
                operation,
                timeProvider);
            await operationJournal.SaveAsync(completed, cancellationToken);
            return new EnvironmentVariableTransactionResult(
                completed,
                recoveryPoint);
        }
        catch (Exception exception)
        {
            var failureReason = exception.Message;
            var rollbackSucceeded = true;
            if (executionStarted)
            {
                var recoveryPoint = operation.RecoveryPointId is null
                    ? null
                    : await recoveryPointStore.GetAsync(
                        operation.RecoveryPointId,
                        CancellationToken.None);
                if (recoveryPoint is null)
                {
                    rollbackSucceeded = false;
                    failureReason =
                        $"{failureReason}；恢复点不存在。";
                }
                else
                {
                    try
                    {
                        await RestoreAsync(
                            recoveryPoint,
                            CancellationToken.None);
                        failureReason =
                            $"{failureReason}；已恢复原环境变量。";
                    }
                    catch (Exception restoreException)
                    {
                        rollbackSucceeded = false;
                        failureReason =
                            $"{failureReason}；恢复原环境变量失败: {restoreException.Message}";
                    }
                }
            }

            operation = OperationStateMachine.Fail(
                operation,
                failureReason,
                timeProvider);
            await operationJournal.SaveAsync(operation, CancellationToken.None);
            if (rollbackSucceeded)
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

    public async Task<OperationRecord> RollbackAsync(
        string operationId,
        CancellationToken cancellationToken = default)
    {
        var operation = await operationJournal.GetAsync(
            operationId,
            cancellationToken)
            ?? throw new KeyNotFoundException("未找到操作记录。");
        if (operation.State is OperationState.Succeeded
            or OperationState.RolledBack)
        {
            operation = OperationStateMachine.Reject(
                operation,
                OperationState.RolledBack,
                "该操作当前状态不能回滚。",
                timeProvider);
            await operationJournal.SaveAsync(operation, CancellationToken.None);
            throw new InvalidOperationException("该操作当前状态不能回滚。");
        }

        if (operation.RecoveryPointId is null)
        {
            throw new InvalidOperationException("操作没有可用的恢复点。");
        }

        var recoveryPoint = await recoveryPointStore.GetAsync(
            operation.RecoveryPointId,
            cancellationToken)
            ?? throw new InvalidOperationException("恢复点不存在，无法回滚。");
        await RestoreAsync(recoveryPoint, cancellationToken);

        if (operation.State != OperationState.Failed)
        {
            operation = OperationStateMachine.Fail(
                operation,
                "用户请求从恢复点回滚。",
                timeProvider);
            await operationJournal.SaveAsync(operation, cancellationToken);
        }

        operation = OperationStateMachine.Rollback(operation, timeProvider);
        await operationJournal.SaveAsync(operation, cancellationToken);
        return operation;
    }

    private async Task RestoreAsync(
        EnvironmentVariableRecoveryPoint recoveryPoint,
        CancellationToken cancellationToken)
    {
        foreach (var original in recoveryPoint.OriginalValues)
        {
            await store.SetAsync(
                original.Key,
                original.Value,
                cancellationToken);
        }

        await store.BroadcastAsync(cancellationToken);
    }

    private void ValidatePreview(EnvironmentVariableUpdatePreview preview)
    {
        if (preview.Changes.Count == 0)
        {
            throw new InvalidOperationException("没有可应用的环境变量变更。");
        }

        var changeNames = preview.Changes
            .Select(change => change.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (preview.OriginalValues.Count != changeNames.Count
            || preview.DesiredValues.Count != changeNames.Count
            || preview.OriginalValues.Keys.Any(name => !changeNames.Contains(name))
            || preview.DesiredValues.Keys.Any(name => !changeNames.Contains(name)))
        {
            throw new InvalidOperationException(
                "环境变量预览包含未声明的变量。");
        }

        foreach (var change in preview.Changes)
        {
            if (!preview.OriginalValues.ContainsKey(change.Name)
                || !preview.DesiredValues.ContainsKey(change.Name))
            {
                throw new InvalidOperationException(
                    $"环境变量预览缺少 {change.Name} 的原值或目标值。");
            }

            if (string.Equals(
                    change.Name,
                    "Path",
                    StringComparison.OrdinalIgnoreCase))
            {
                var originalPath = preview.OriginalValues.GetValueOrDefault(
                    "Path");
                var managedEntries = _pathEditor.ExtractManagedEntries(
                    change.Value);
                var rebuiltPath = _pathEditor.Apply(
                    originalPath,
                    managedEntries);
                if (!string.Equals(
                        rebuiltPath,
                        change.Value,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "PATH 计划包含非管理器拥有的条目。");
                }

                continue;
            }

            if (!IsManagedVariableName(change.Name))
            {
                throw new InvalidOperationException(
                    $"变量 {change.Name} 不属于管理器。");
            }
        }
    }

    private static IReadOnlyList<EnvironmentVariableChange>
        NormalizeManagedVariableChanges(
            IReadOnlyList<EnvironmentVariableChange> changes)
    {
        if (changes.Count == 0)
        {
            throw new InvalidOperationException("没有可预览的环境变量变更。");
        }

        var duplicate = changes
            .GroupBy(change => change.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"环境变量 {duplicate.Key} 重复。");
        }

        foreach (var change in changes)
        {
            if (!IsManagedVariableName(change.Name))
            {
                throw new InvalidOperationException(
                    $"变量 {change.Name} 不属于管理器。");
            }
        }

        return changes;
    }

    private static bool IsManagedVariableName(string name)
    {
        return name.StartsWith(
            ManagedVariablePrefix,
            StringComparison.OrdinalIgnoreCase);
    }

    private async Task<OperationRecord> SaveTransitionAsync(
        OperationRecord operation,
        CancellationToken cancellationToken)
    {
        await operationJournal.SaveAsync(operation, cancellationToken);
        return operation;
    }
}
