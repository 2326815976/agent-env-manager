using AgentEnvManager.Core.Adoption;
using AgentEnvManager.Core.Inspection;
using AgentEnvManager.Core.Migrations;
using AgentEnvManager.Core.Operations;
using AgentEnvManager.Wpf;

namespace AgentEnvManager.Wpf.Tests;

public sealed class MigrationCenterViewModelTests
{
    [Fact]
    public async Task PreviewAndMigrate_uses_selected_environment_and_refreshes()
    {
        var client = new StubMigrationClient();
        var refreshCount = 0;
        var viewModel = new MigrationCenterViewModel(
            client,
            () =>
            {
                refreshCount++;
                return Task.CompletedTask;
            });
        viewModel.SelectedEnvironment = CreateManagedEnvironment();
        viewModel.DestinationPath = @"D:\moved\node";

        await viewModel.PreviewMigrationAsync();

        Assert.Equal(
            "node-runtime-fingerprint",
            client.LastFingerprint?.Value);
        Assert.Equal(@"D:\moved\node", client.LastDestinationPath);
        Assert.Equal("迁移 Node.js。", viewModel.MigrationImpact);
        Assert.Equal(@"D:\moved\node", viewModel.PendingTarget);
        Assert.Equal("recovery-migrate", viewModel.PendingRecoveryPoint);
        Assert.Equal("迁移后健康检查通过。", viewModel.ExpectedResult);

        await viewModel.MigrateAsync();

        Assert.Equal(1, client.MigrateCalls);
        Assert.Equal(1, refreshCount);
        Assert.Contains("已迁移", viewModel.StatusMessage);
    }

    private static EnvironmentRowViewModel CreateManagedEnvironment()
    {
        return new EnvironmentRowViewModel(
            new ObservedEnvironment(
                new EnvironmentAsset(
                    EnvironmentAssetKind.ToolRuntime,
                    "Node.js",
                    "24.1.0",
                    @"D:\Runtimes\node",
                    IsSystemComponent: false,
                    DiscoverySourceInfo.PathCommand),
                new EnvironmentFingerprint("node-runtime-fingerprint"),
                ManagementState.Managed,
                HealthState.Unknown,
                new EnvironmentIdentity("node-runtime")));
    }

    private sealed class StubMigrationClient : IMigrationClient
    {
        public EnvironmentFingerprint? LastFingerprint { get; private set; }

        public string? LastDestinationPath { get; private set; }

        public int MigrateCalls { get; private set; }

        public Task<MigrationPreview> PreviewMigrationAsync(
            EnvironmentFingerprint fingerprint,
            string destinationPath,
            CancellationToken cancellationToken = default)
        {
            LastFingerprint = fingerprint;
            LastDestinationPath = destinationPath;
            var environment = CreateManagedEnvironment();
            var manifest = new EnvironmentManifest(
                new EnvironmentIdentity("node-runtime"),
                fingerprint,
                EnvironmentAssetKind.ToolRuntime,
                "Node.js",
                "24.1.0",
                DiscoverySourceInfo.PathCommand,
                environment.Location,
                @"D:\activation\node\current",
                "asset-hash",
                "recovery-migrate",
                "operation-migrate",
                IsSystemComponent: false,
                DateTimeOffset.UnixEpoch,
                "node-activation",
                @"D:\shims\node");
            return Task.FromResult(new MigrationPreview(
                manifest.Fingerprint,
                manifest,
                manifest.Location,
                destinationPath,
                manifest.StableActivationPath,
                manifest.ManagedEntryPath,
                manifest.Location,
                WasActive: true,
                MigrationStrategy.AtomicRename,
                new MigrationPathStatistics(120, 4096, 1_000_000),
                "迁移 Node.js。",
                "迁移后健康检查通过。",
                "operation-migrate",
                "recovery-migrate"));
        }

        public Task<OperationRecord> MigrateEnvironmentAsync(
            MigrationPreview preview,
            CancellationToken cancellationToken = default)
        {
            MigrateCalls++;
            return Task.FromResult(new OperationRecord(
                "operation-migrate",
                OperationType.Migrate,
                OperationState.Succeeded,
                DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch,
                "迁移 Node.js",
                RecoveryPointId: preview.RecoveryPointId,
                Target: preview.DestinationPath,
                Impact: preview.Impact));
        }
    }
}
