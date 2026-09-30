using AgentEnvManager.Core.Activation;
using AgentEnvManager.Core.Adoption;
using AgentEnvManager.Core.Inspection;
using AgentEnvManager.Core.Operations;
using AgentEnvManager.Core.Tests.TestSupport;

namespace AgentEnvManager.Core.Tests.Activation;

public sealed class VersionSwitchingTests
{
    [Fact]
    public async Task SwitchVersionAsync_updates_activation_point_and_records_success()
    {
        var probe = CreateProbe();
        var manifestStore = new InMemoryManifestStore();
        var link = new RecordingActivationLink();
        var healthCheck = new RecordingRuntimeHealthCheck(isHealthy: true);
        var journal = new RecordingOperationJournal();
        var manager = CreateManager(
            probe,
            manifestStore,
            link,
            healthCheck,
            journal);
        var inventory = await manager.InspectAsync();
        var firstObserved = inventory.Environments[0];
        var secondObserved = inventory.Environments[1];
        var first = await manager.AdoptAsync(firstObserved.Fingerprint);
        var second = await manager.AdoptAsync(secondObserved.Fingerprint);
        await link.SetTargetAsync(
            first.Manifest.StableActivationPath,
            first.Manifest.Location);

        var preview = await manager.PreviewVersionSwitchAsync(
            second.Manifest.Fingerprint);
        var operation = await manager.SwitchVersionAsync(
            second.Manifest.Fingerprint);

        Assert.Equal(first.Manifest.StableActivationPath, second.Manifest.StableActivationPath);
        Assert.Equal(first.Manifest.Identity, preview.Active?.Identity);
        Assert.False(preview.IsAlreadyActive);
        Assert.Equal(OperationType.Switch, operation.Type);
        Assert.Equal(OperationState.Succeeded, operation.State);
        Assert.Equal(
            second.Manifest.Location,
            await link.GetTargetAsync(second.Manifest.StableActivationPath));
        Assert.Equal(
            [
                OperationState.Draft,
                OperationState.Validated,
                OperationState.RecoveryReady,
                OperationState.Executing,
                OperationState.Verifying,
                OperationState.Succeeded
            ],
            journal.History
                .Where(record => record.Id == operation.Id)
                .Select(record => record.State));
    }

    [Fact]
    public async Task SwitchVersionAsync_restores_previous_version_when_health_check_fails()
    {
        var probe = CreateProbe();
        var manifestStore = new InMemoryManifestStore();
        var link = new RecordingActivationLink();
        var healthCheck = new RecordingRuntimeHealthCheck(isHealthy: false);
        var journal = new RecordingOperationJournal();
        var manager = CreateManager(
            probe,
            manifestStore,
            link,
            healthCheck,
            journal);
        var inventory = await manager.InspectAsync();
        var firstObserved = inventory.Environments[0];
        var secondObserved = inventory.Environments[1];
        var first = await manager.AdoptAsync(firstObserved.Fingerprint);
        var second = await manager.AdoptAsync(secondObserved.Fingerprint);
        await link.SetTargetAsync(
            first.Manifest.StableActivationPath,
            first.Manifest.Location);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => manager.SwitchVersionAsync(second.Manifest.Fingerprint));

        Assert.Contains("健康检查失败", exception.Message);
        Assert.Equal(
            first.Manifest.Location,
            await link.GetTargetAsync(first.Manifest.StableActivationPath));
        var switchOperation = Assert.Single(
            journal.History
                .Where(record => record.Type == OperationType.Switch)
                .GroupBy(record => record.Id)
                .Select(group => group.Last()));
        Assert.Equal(OperationState.RolledBack, switchOperation.State);
        Assert.Equal(
            [
                OperationState.Draft,
                OperationState.Validated,
                OperationState.RecoveryReady,
                OperationState.Executing,
                OperationState.Verifying,
                OperationState.Failed,
                OperationState.RolledBack
            ],
            journal.History
                .Where(record => record.Id == switchOperation.Id)
                .Select(record => record.State));
    }

    [Fact]
    public async Task RollbackOperationAsync_restores_previous_activation_for_interrupted_switch()
    {
        var probe = CreateProbe();
        var manifestStore = new InMemoryManifestStore();
        var link = new RecordingActivationLink();
        var healthCheck = new RecordingRuntimeHealthCheck(isHealthy: true);
        var journal = new RecordingOperationJournal();
        var recoveryStore = new RecordingRecoveryPointStore();
        var manager = CreateManager(
            probe,
            manifestStore,
            link,
            healthCheck,
            journal,
            recoveryStore);
        var inventory = await manager.InspectAsync();
        var first = await manager.AdoptAsync(
            inventory.Environments[0].Fingerprint);
        var second = await manager.AdoptAsync(
            inventory.Environments[1].Fingerprint);
        var recoveryPoint = await recoveryStore.CreateAsync(
            CreateSwitchPreview(second.Manifest),
            "interrupted-switch",
            first.Manifest);
        await journal.SaveAsync(new OperationRecord(
            "interrupted-switch",
            OperationType.Switch,
            OperationState.Verifying,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            "模拟中断的版本切换",
            recoveryPoint.Id));
        await link.SetTargetAsync(
            second.Manifest.StableActivationPath,
            second.Manifest.Location);

        var rolledBack = await manager.RollbackOperationAsync(
            "interrupted-switch");

        Assert.Equal(OperationState.RolledBack, rolledBack.State);
        Assert.Equal(
            first.Manifest.Location,
            await link.GetTargetAsync(first.Manifest.StableActivationPath));
        Assert.Equal(2, (await manifestStore.ReadAllAsync()).Count);
    }

    [Fact]
    public async Task SwitchVersionAsync_uses_runtime_directory_for_executable_location()
    {
        var probe = new StubEnvironmentProbe(
            new EnvironmentProbeResult(
                [
                    new EnvironmentAsset(
                        EnvironmentAssetKind.ToolRuntime,
                        "Node.js",
                        "22.22.0",
                        @"D:\Runtimes\node-22\node.exe",
                        IsSystemComponent: false,
                        Source: DiscoverySourceInfo.PathCommand),
                    new EnvironmentAsset(
                        EnvironmentAssetKind.ToolRuntime,
                        "Node.js",
                        "24.1.0",
                        @"D:\Runtimes\node-24\node.exe",
                        IsSystemComponent: false,
                        Source: DiscoverySourceInfo.PathCommand)
                ],
                []));
        var manifestStore = new InMemoryManifestStore();
        var link = new RecordingActivationLink();
        var manager = CreateManager(
            probe,
            manifestStore,
            link,
            new RecordingRuntimeHealthCheck(isHealthy: true),
            new RecordingOperationJournal());
        var inventory = await manager.InspectAsync();

        var first = await manager.AdoptAsync(
            inventory.Environments[0].Fingerprint);
        await manager.SwitchVersionAsync(first.Manifest.Fingerprint);
        var second = await manager.AdoptAsync(
            inventory.Environments[1].Fingerprint);
        await manager.SwitchVersionAsync(second.Manifest.Fingerprint);

        Assert.Equal(
            @"D:\Runtimes\node-24",
            await link.GetTargetAsync(second.Manifest.StableActivationPath));
    }

    private static EnvironmentManager CreateManager(
        IEnvironmentProbe probe,
        IEnvironmentManifestStore manifestStore,
        IEnvironmentActivationLink activationLink,
        IRuntimeHealthCheck healthCheck,
        IOperationJournal journal,
        IEnvironmentRecoveryPointStore? recoveryStore = null)
    {
        return new EnvironmentManager(
            probe,
            manifestStore: manifestStore,
            assetHasher: new FixedAssetHasher("asset-hash"),
            recoveryPointStore: recoveryStore
                ?? new RecordingRecoveryPointStore(),
            operationJournal: journal,
            activationPathFactory: new FixedActivationPathFactory(),
            activationLink: activationLink,
            healthCheck: healthCheck);
    }

    private static AdoptionPreview CreateSwitchPreview(
        EnvironmentManifest manifest)
    {
        return new AdoptionPreview(
            manifest.Fingerprint,
            manifest.Identity,
            new EnvironmentAsset(
                manifest.Kind,
                manifest.Name,
                manifest.Version,
                manifest.Location,
                manifest.IsSystemComponent,
                manifest.Source),
            manifest.AssetHash,
            manifest.StableActivationPath,
            "切换版本前记录当前激活点。",
            IsAlreadyManaged: true,
            ExistingIdentity: manifest.Identity);
    }

    private static StubEnvironmentProbe CreateProbe()
    {
        return new StubEnvironmentProbe(
            new EnvironmentProbeResult(
                [
                    new EnvironmentAsset(
                        EnvironmentAssetKind.ToolRuntime,
                        "Node.js",
                        "22.22.0",
                        @"D:\Runtimes\node-22",
                        IsSystemComponent: false,
                        Source: DiscoverySourceInfo.PathCommand),
                    new EnvironmentAsset(
                        EnvironmentAssetKind.ToolRuntime,
                        "Node.js",
                        "24.1.0",
                        @"D:\Runtimes\node-24",
                        IsSystemComponent: false,
                        Source: DiscoverySourceInfo.PathCommand)
                ],
                []));
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
