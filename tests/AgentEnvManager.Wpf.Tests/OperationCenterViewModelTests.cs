using AgentEnvManager.Core.Adoption;
using AgentEnvManager.Core.Activation;
using AgentEnvManager.Core.EnvironmentVariables;
using AgentEnvManager.Core.Deletion;
using AgentEnvManager.Core.Inspection;
using AgentEnvManager.Core.Operations;

namespace AgentEnvManager.Wpf.Tests;

public sealed class OperationCenterViewModelTests
{
    [Fact]
    public async Task RefreshAsync_loads_operations_and_recovery_points()
    {
        var client = new StubClient();
        var viewModel = new OperationCenterViewModel(client);

        await viewModel.RefreshAsync();

        var operation = Assert.Single(viewModel.Operations);
        Assert.Equal("纳管", operation.TypeLabel);
        Assert.Equal("失败", operation.StateLabel);
        Assert.Equal(@"D:\Runtimes\node", operation.Target);
        Assert.Contains("不会移动", operation.Impact);
        Assert.Equal("recovery-1", operation.RecoveryPointId);
        Assert.Equal(2, viewModel.RecoveryPoints.Count);
    }

    [Fact]
    public async Task RollbackSelectedAsync_calls_core_and_refreshes()
    {
        var client = new StubClient();
        var viewModel = new OperationCenterViewModel(client);
        await viewModel.RefreshAsync();
        viewModel.SelectedOperation = Assert.Single(viewModel.Operations);

        await viewModel.PrepareRollbackAsync();

        Assert.Null(client.RolledBackOperationId);
        Assert.Equal(@"D:\Runtimes\node", viewModel.RollbackTarget);
        Assert.Equal("recovery-1", viewModel.RollbackRecoveryPoint);

        await viewModel.RollbackSelectedAsync();

        Assert.Equal("operation-1", client.RolledBackOperationId);
        Assert.Contains("已回滚", viewModel.StatusMessage);
    }

    [Fact]
    public async Task RefreshAsync_disables_rollback_when_plan_metadata_is_incomplete()
    {
        var client = new StubClient();
        client.Operations =
        [
            new OperationRecord(
                "operation-1",
                OperationType.Adopt,
                OperationState.Failed,
                DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch,
                "缺少元数据的旧操作",
                "recovery-1")
        ];
        var viewModel = new OperationCenterViewModel(client);

        await viewModel.RefreshAsync();

        Assert.False(Assert.Single(viewModel.Operations).CanRollback);
    }

    private sealed class StubClient : StubEnvironmentManagerClient
    {
        public string? RolledBackOperationId { get; private set; }

        public IReadOnlyList<OperationRecord>? Operations { get; set; }

        public override Task<IReadOnlyList<OperationRecord>> ListOperationsAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Operations ??
            [
                new OperationRecord(
                    "operation-1",
                    OperationType.Adopt,
                    OperationState.Failed,
                    DateTimeOffset.UnixEpoch,
                    DateTimeOffset.UnixEpoch,
                    "纳管 Node.js",
                    "recovery-1",
                    Target: @"D:\Runtimes\node",
                    Impact: "纳管不会移动原始文件。")
            ]);
        }

        public override Task<IReadOnlyList<AdoptionRecoveryPoint>>
            ListEnvironmentRecoveryPointsAsync(
                CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<AdoptionRecoveryPoint>>(
            [
                new AdoptionRecoveryPoint(
                    "recovery-1",
                    "operation-1",
                    new EnvironmentFingerprint("node-24"),
                    null,
                    null,
                    "纳管前记录 manifest 变更。",
                    DateTimeOffset.UnixEpoch)
            ]);
        }

        public override Task<IReadOnlyList<EnvironmentVariableRecoveryPoint>>
            ListEnvironmentVariableRecoveryPointsAsync(
                CancellationToken cancellationToken = default)
        {
            return Task.FromResult<
                IReadOnlyList<EnvironmentVariableRecoveryPoint>>(
            [
                new EnvironmentVariableRecoveryPoint(
                    "variable-recovery-1",
                    new Dictionary<string, string?>
                    {
                        ["AGENT_ENV_MANAGER_MODE"] = "original"
                    },
                    DateTimeOffset.UnixEpoch)
            ]);
        }

        public override Task<OperationRecord> RollbackOperationAsync(
            string operationId,
            CancellationToken cancellationToken = default)
        {
            RolledBackOperationId = operationId;
            return Task.FromResult(new OperationRecord(
                operationId,
                OperationType.Adopt,
                OperationState.RolledBack,
                DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch,
                "纳管 Node.js"));
        }

        public override Task<OperationRollbackPlan> PreviewRollbackAsync(
            string operationId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new OperationRollbackPlan(
                operationId,
                @"D:\Runtimes\node",
                "恢复纳管前状态。",
                "recovery-1",
                "环境资产恢复为操作前状态。"));
        }

        public override Task<InspectionReport> InspectAsync(
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public override Task<AdoptionPreview> PreviewAdoptionAsync(
            EnvironmentFingerprint fingerprint,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public override Task<ManagedEnvironment> AdoptAsync(
            AdoptionPreview preview,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public override Task<VersionSwitchPreview> PreviewVersionSwitchAsync(
            EnvironmentFingerprint fingerprint,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public override Task<OperationRecord> SwitchVersionAsync(
            VersionSwitchPreview preview,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<EnvironmentDeletionPreview>
            PreviewEnvironmentDeletionAsync(
                EnvironmentFingerprint fingerprint,
                IReadOnlyList<string>? associatedState = null,
                CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<OperationRecord> QuarantineEnvironmentAsync(
            EnvironmentDeletionPreview preview,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<QuarantinedEnvironment>>
            ListQuarantinedEnvironmentsAsync(
                CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<QuarantinedEnvironment>>([]);
        }

        public Task<EnvironmentRestorePreview>
            PreviewQuarantineRestoreAsync(
                string quarantineId,
                CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<OperationRecord> RestoreQuarantinedEnvironmentAsync(
            EnvironmentRestorePreview preview,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<PermanentDeletePreview> PreviewPermanentDeleteAsync(
            string quarantineId,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task PermanentDeleteAsync(
            PermanentDeletePreview preview,
            bool confirmed,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }
}
