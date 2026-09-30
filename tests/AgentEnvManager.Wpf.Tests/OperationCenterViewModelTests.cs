using AgentEnvManager.Core.Adoption;
using AgentEnvManager.Core.Activation;
using AgentEnvManager.Core.EnvironmentVariables;
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

    private sealed class StubClient : IEnvironmentManagerClient
    {
        public string? RolledBackOperationId { get; private set; }

        public Task<IReadOnlyList<OperationRecord>> ListOperationsAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<OperationRecord>>(
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

        public Task<IReadOnlyList<AdoptionRecoveryPoint>>
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

        public Task<IReadOnlyList<EnvironmentVariableRecoveryPoint>>
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

        public Task<OperationRecord> RollbackOperationAsync(
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

        public Task<InspectionReport> InspectAsync(
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<AdoptionPreview> PreviewAdoptionAsync(
            EnvironmentFingerprint fingerprint,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<ManagedEnvironment> AdoptAsync(
            AdoptionPreview preview,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<VersionSwitchPreview> PreviewVersionSwitchAsync(
            EnvironmentFingerprint fingerprint,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<OperationRecord> SwitchVersionAsync(
            VersionSwitchPreview preview,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }
}
