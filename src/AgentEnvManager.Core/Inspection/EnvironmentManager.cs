using AgentEnvManager.Core.Adoption;
using AgentEnvManager.Core.Operations;
using AgentEnvManager.Core.Storage;

namespace AgentEnvManager.Core.Inspection;

public sealed class EnvironmentManager
{
    private readonly EnvironmentInspector _inspector;
    private readonly EnvironmentAdopter _adopter;

    public EnvironmentManager(
        IEnvironmentProbe probe,
        TimeProvider? timeProvider = null,
        IEnvironmentManifestStore? manifestStore = null,
        IEnvironmentIndex? index = null,
        IEnvironmentAssetHasher? assetHasher = null,
        IEnvironmentRecoveryPointStore? recoveryPointStore = null,
        IOperationJournal? operationJournal = null,
        IStableActivationPathFactory? activationPathFactory = null)
    {
        var clock = timeProvider ?? TimeProvider.System;
        var store = manifestStore ?? new InMemoryEnvironmentManifestStore();
        var environmentIndex = index ?? new InMemoryEnvironmentIndex();
        var hasher = assetHasher ?? new FileSystemEnvironmentAssetHasher();
        var recoveryStore = recoveryPointStore
            ?? InMemoryEnvironmentRecoveryPointStore.Instance;
        var journal = operationJournal ?? new InMemoryOperationJournal();
        var pathFactory = activationPathFactory
            ?? DefaultStableActivationPathFactory.Instance;

        _inspector = new EnvironmentInspector(
            probe,
            store,
            clock);
        _adopter = new EnvironmentAdopter(
            _inspector,
            store,
            environmentIndex,
            hasher,
            recoveryStore,
            journal,
            pathFactory,
            clock);
    }

    public Task<InspectionReport> InspectAsync(
        CancellationToken cancellationToken = default)
    {
        return _inspector.InspectAsync(cancellationToken);
    }

    public Task<AdoptionPreview> PreviewAdoptionAsync(
        EnvironmentFingerprint fingerprint,
        CancellationToken cancellationToken = default)
    {
        return _adopter.PreviewAsync(fingerprint, cancellationToken);
    }

    public Task<ManagedEnvironment> AdoptAsync(
        EnvironmentFingerprint fingerprint,
        CancellationToken cancellationToken = default)
    {
        return _adopter.AdoptAsync(fingerprint, cancellationToken);
    }

    public Task<ManagedEnvironment> AdoptAsync(
        AdoptionPreview preview,
        CancellationToken cancellationToken = default)
    {
        return _adopter.AdoptAsync(preview, cancellationToken);
    }

    public Task<int> RebuildEnvironmentIndexAsync(
        CancellationToken cancellationToken = default)
    {
        return _adopter.RebuildIndexAsync(cancellationToken);
    }

    public Task<OperationRecord> RollbackOperationAsync(
        string operationId,
        CancellationToken cancellationToken = default)
    {
        return _adopter.RollbackOperationAsync(
            operationId,
            cancellationToken);
    }
}
