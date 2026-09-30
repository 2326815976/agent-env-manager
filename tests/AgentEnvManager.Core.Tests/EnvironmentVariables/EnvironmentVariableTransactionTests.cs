using AgentEnvManager.Core.EnvironmentVariables;
using AgentEnvManager.Core.Inspection;
using AgentEnvManager.Core.Operations;
using AgentEnvManager.Core.Tests.TestSupport;

namespace AgentEnvManager.Core.Tests.EnvironmentVariables;

public sealed class EnvironmentVariableTransactionTests
{
    [Fact]
    public async Task ApplyEnvironmentVariableUpdateAsync_broadcasts_after_success()
    {
        var store = new RecordingUserEnvironmentVariableStore();
        var journal = new RecordingOperationJournal();
        var manager = CreateManager(store, journal);
        var preview = await manager.PreviewManagedVariableUpdateAsync(
            [
                new EnvironmentVariableChange(
                    "AGENT_ENV_MANAGER_MODE",
                    "managed")
            ]);

        var result = await manager.ApplyEnvironmentVariableUpdateAsync(
            preview);

        Assert.Equal(
            "managed",
            await store.GetAsync("AGENT_ENV_MANAGER_MODE"));
        Assert.Equal(1, store.BroadcastCount);
        Assert.Equal(OperationState.Succeeded, result.Operation.State);
        Assert.Null(
            result.RecoveryPoint.OriginalValues["AGENT_ENV_MANAGER_MODE"]);
    }

    [Fact]
    public async Task ApplyEnvironmentVariableUpdateAsync_restores_original_values_when_write_fails()
    {
        var store = new RecordingUserEnvironmentVariableStore(
            new Dictionary<string, string?>
            {
                ["AGENT_ENV_MANAGER_MODE"] = "original"
            });
        var journal = new RecordingOperationJournal();
        var manager = CreateManager(store, journal);
        store.FailOnSet = (name, value) =>
            name == "AGENT_ENV_MANAGER_BROKEN"
            && value == "fail"
                ? new InvalidOperationException("模拟写入失败。")
                : null;
        var preview = await manager.PreviewManagedVariableUpdateAsync(
            [
                new EnvironmentVariableChange(
                    "AGENT_ENV_MANAGER_MODE",
                    "changed"),
                new EnvironmentVariableChange(
                    "AGENT_ENV_MANAGER_BROKEN",
                    "fail")
            ]);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => manager.ApplyEnvironmentVariableUpdateAsync(preview));

        Assert.Equal(
            "original",
            await store.GetAsync("AGENT_ENV_MANAGER_MODE"));
        Assert.Null(await store.GetAsync("AGENT_ENV_MANAGER_BROKEN"));
        var operation = Assert.Single(
            journal.History
                .Where(record =>
                    record.Type == OperationType.EnvironmentVariables)
                .GroupBy(record => record.Id)
                .Select(group => group.Last()));
        Assert.Equal(OperationState.RolledBack, operation.State);
    }

    [Fact]
    public async Task PreviewManagedVariableUpdateAsync_rejects_unmanaged_variable()
    {
        var manager = CreateManager(
            new RecordingUserEnvironmentVariableStore(),
            new RecordingOperationJournal());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => manager.PreviewManagedVariableUpdateAsync(
                [new EnvironmentVariableChange("JAVA_HOME", @"C:\Java")]));

        Assert.Contains("不属于管理器", exception.Message);
    }

    [Fact]
    public async Task ApplyEnvironmentVariableUpdateAsync_rejects_stale_preview()
    {
        var store = new RecordingUserEnvironmentVariableStore();
        var manager = CreateManager(
            store,
            new RecordingOperationJournal());
        var preview = await manager.PreviewManagedVariableUpdateAsync(
            [new EnvironmentVariableChange(
                "AGENT_ENV_MANAGER_MODE",
                "managed")]);
        await store.SetAsync("AGENT_ENV_MANAGER_MODE", "external");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => manager.ApplyEnvironmentVariableUpdateAsync(preview));

        Assert.Contains("已变化", exception.Message);
        Assert.Equal("external", await store.GetAsync("AGENT_ENV_MANAGER_MODE"));
    }

    [Fact]
    public async Task ApplyEnvironmentVariableUpdateAsync_rejects_incomplete_preview()
    {
        var manager = CreateManager(
            new RecordingUserEnvironmentVariableStore(),
            new RecordingOperationJournal());
        var preview = new EnvironmentVariableUpdatePreview(
            new Dictionary<string, string?>(),
            new Dictionary<string, string?>(),
            [new EnvironmentVariableChange(
                "AGENT_ENV_MANAGER_MODE",
                "managed")],
            "测试");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => manager.ApplyEnvironmentVariableUpdateAsync(preview));
    }

    [Fact]
    public async Task ApplyEnvironmentVariableUpdateAsync_restores_when_broadcast_fails()
    {
        var store = new RecordingUserEnvironmentVariableStore();
        var journal = new RecordingOperationJournal();
        var manager = CreateManager(store, journal);
        var preview = await manager.PreviewManagedVariableUpdateAsync(
            [new EnvironmentVariableChange(
                "AGENT_ENV_MANAGER_MODE",
                "managed")]);
        store.FailOnBroadcastOnce = true;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => manager.ApplyEnvironmentVariableUpdateAsync(preview));

        Assert.Null(await store.GetAsync("AGENT_ENV_MANAGER_MODE"));
        var operation = Assert.Single(
            journal.History
                .Where(record =>
                    record.Type == OperationType.EnvironmentVariables)
                .GroupBy(record => record.Id)
                .Select(group => group.Last()));
        Assert.Equal(OperationState.RolledBack, operation.State);
    }

    [Fact]
    public async Task ApplyEnvironmentVariableUpdateAsync_rejects_extra_recovery_values()
    {
        var manager = CreateManager(
            new RecordingUserEnvironmentVariableStore(),
            new RecordingOperationJournal());
        var preview = new EnvironmentVariableUpdatePreview(
            new Dictionary<string, string?>
            {
                ["AGENT_ENV_MANAGER_MODE"] = null,
                ["JAVA_HOME"] = @"C:\Java"
            },
            new Dictionary<string, string?>
            {
                ["AGENT_ENV_MANAGER_MODE"] = "managed"
            },
            [new EnvironmentVariableChange(
                "AGENT_ENV_MANAGER_MODE",
                "managed")],
            "测试");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => manager.ApplyEnvironmentVariableUpdateAsync(preview));

        Assert.Contains("未声明", exception.Message);
    }

    [Fact]
    public async Task ApplyEnvironmentVariableUpdateAsync_rejects_reused_preview()
    {
        var store = new RecordingUserEnvironmentVariableStore();
        var manager = CreateManager(
            store,
            new RecordingOperationJournal());
        var preview = await manager.PreviewManagedVariableUpdateAsync(
            [new EnvironmentVariableChange(
                "AGENT_ENV_MANAGER_MODE",
                "managed")]);
        await manager.ApplyEnvironmentVariableUpdateAsync(preview);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => manager.ApplyEnvironmentVariableUpdateAsync(preview));

        Assert.Contains("未经当前管理器授权", exception.Message);
    }

    [Fact]
    public async Task ApplyEnvironmentVariableUpdateAsync_rejects_tampered_authorized_preview()
    {
        var manager = CreateManager(
            new RecordingUserEnvironmentVariableStore(),
            new RecordingOperationJournal());
        var preview = await manager.PreviewManagedVariableUpdateAsync(
            [new EnvironmentVariableChange(
                "AGENT_ENV_MANAGER_MODE",
                "managed")]);
        var tampered = new EnvironmentVariableUpdatePreview(
            preview.OriginalValues,
            new Dictionary<string, string?>
            {
                ["AGENT_ENV_MANAGER_MODE"] = "tampered"
            },
            [new EnvironmentVariableChange(
                "AGENT_ENV_MANAGER_MODE",
                "tampered")],
            preview.Impact,
            preview.ExpandableValues,
            preview.AuthorizationToken);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => manager.ApplyEnvironmentVariableUpdateAsync(tampered));

        Assert.Contains("未经当前管理器授权", exception.Message);
    }

    [Fact]
    public async Task ApplyEnvironmentVariableUpdateAsync_preserves_expandable_value_kind()
    {
        var store = new RecordingUserEnvironmentVariableStore();
        await store.SetAsync(
            "AGENT_ENV_MANAGER_MODE",
            "%USERPROFILE%\\mode",
            isExpandable: true);
        var manager = CreateManager(
            store,
            new RecordingOperationJournal());
        var preview = await manager.PreviewManagedVariableUpdateAsync(
            [new EnvironmentVariableChange(
                "AGENT_ENV_MANAGER_MODE",
                "%USERPROFILE%\\next")]);

        await manager.ApplyEnvironmentVariableUpdateAsync(preview);

        Assert.True(await store.IsExpandableAsync("AGENT_ENV_MANAGER_MODE"));
    }

    [Fact]
    public async Task RollbackOperationAsync_restores_interrupted_environment_transaction()
    {
        var store = new RecordingUserEnvironmentVariableStore(
            new Dictionary<string, string?>
            {
                ["AGENT_ENV_MANAGER_MODE"] = "changed"
            });
        var recoveryStore =
            new RecordingEnvironmentVariableRecoveryPointStore();
        var journal = new RecordingOperationJournal();
        var manager = CreateManager(store, journal, recoveryStore);
        var recoveryPoint = await recoveryStore.CreateAsync(
            "environment-operation",
            new Dictionary<string, string?>
            {
                ["AGENT_ENV_MANAGER_MODE"] = "original"
            });
        await journal.SaveAsync(new OperationRecord(
            "environment-operation",
            OperationType.EnvironmentVariables,
            OperationState.Verifying,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            "模拟中断的环境变量事务",
            recoveryPoint.Id));

        var rolledBack = await manager.RollbackOperationAsync(
            "environment-operation");

        Assert.Equal(OperationState.RolledBack, rolledBack.State);
        Assert.Equal(
            "original",
            await store.GetAsync("AGENT_ENV_MANAGER_MODE"));
    }

    [Fact]
    public async Task FileEnvironmentVariableRecoveryPointStore_round_trips_original_values()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "AgentEnvManager.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var store = new FileEnvironmentVariableRecoveryPointStore(root);
            var created = await store.CreateAsync(
                "operation",
                new Dictionary<string, string?>
                {
                    ["AGENT_ENV_MANAGER_MODE"] = "original",
                    ["AGENT_ENV_MANAGER_EMPTY"] = null
                });

            var restored = await store.GetAsync(created.Id);

            Assert.NotNull(restored);
            Assert.Equal(
                "original",
                restored.OriginalValues["AGENT_ENV_MANAGER_MODE"]);
            Assert.Null(restored.OriginalValues["AGENT_ENV_MANAGER_EMPTY"]);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static EnvironmentManager CreateManager(
        IUserEnvironmentVariableStore store,
        RecordingOperationJournal journal,
        IEnvironmentVariableRecoveryPointStore? recoveryStore = null)
    {
        return new EnvironmentManager(
            new StubEnvironmentProbe(
                new EnvironmentProbeResult([], [])),
            assetHasher: new FixedAssetHasher("asset-hash"),
            operationJournal: journal,
            userEnvironmentVariableStore: store,
            environmentVariableRecoveryPointStore: recoveryStore);
    }

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
