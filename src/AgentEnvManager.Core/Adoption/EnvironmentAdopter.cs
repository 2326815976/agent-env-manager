using AgentEnvManager.Core.Inspection;

namespace AgentEnvManager.Core.Adoption;

internal sealed class EnvironmentAdopter(
    EnvironmentInspector inspector,
    IEnvironmentManifestStore manifestStore,
    IEnvironmentIndex index,
    IEnvironmentAssetHasher assetHasher,
    IEnvironmentRecoveryPointStore recoveryPointStore,
    IStableActivationPathFactory activationPathFactory,
    TimeProvider timeProvider)
{
    public async Task<AdoptionPreview> PreviewAsync(
        EnvironmentFingerprint fingerprint,
        CancellationToken cancellationToken = default)
    {
        var inventory = await inspector.InspectAsync(cancellationToken);
        var observed = inventory.Environments.SingleOrDefault(environment =>
            environment.Fingerprint == fingerprint)
            ?? throw new KeyNotFoundException("未找到要纳管的仅观测环境。");
        var existing = await manifestStore.FindByFingerprintAsync(
            fingerprint,
            cancellationToken);
        var assetHash = await assetHasher.ComputeHashAsync(
            observed.Asset,
            cancellationToken);
        var proposedIdentity = existing?.Identity
            ?? new EnvironmentIdentity(fingerprint.Value);

        return new AdoptionPreview(
            fingerprint,
            proposedIdentity,
            observed.Asset,
            assetHash,
            existing?.StableActivationPath
            ?? activationPathFactory.Create(proposedIdentity),
            "纳管会创建可移植 manifest 并重建本地索引，不会移动原始文件。",
            existing is not null,
            existing?.Identity);
    }

    public async Task<ManagedEnvironment> AdoptAsync(
        EnvironmentFingerprint fingerprint,
        CancellationToken cancellationToken = default)
    {
        var preview = await PreviewAsync(fingerprint, cancellationToken);
        return await AdoptAsync(preview, cancellationToken);
    }

    public async Task<ManagedEnvironment> AdoptAsync(
        AdoptionPreview preview,
        CancellationToken cancellationToken = default)
    {
        var existing = await manifestStore.FindByFingerprintAsync(
            preview.Fingerprint,
            cancellationToken);
        if (existing is not null && existing.AssetHash == preview.AssetHash)
        {
            await RebuildIndexAsync(cancellationToken);
            return new ManagedEnvironment(existing.Identity, existing);
        }

        var recoveryPoint = await recoveryPointStore.CreateAsync(
            preview,
            existing,
            cancellationToken);
        var manifest = await manifestStore.SaveAsync(
            new EnvironmentManifest(
                preview.ProposedIdentity,
                preview.Fingerprint,
                preview.Asset.Kind,
                preview.Asset.Name,
                preview.Asset.Version,
                preview.Asset.Source,
                preview.Asset.Location,
                preview.StableActivationPath,
                preview.AssetHash,
                recoveryPoint.Id,
                preview.Asset.IsSystemComponent,
                timeProvider.GetUtcNow()),
            cancellationToken);

        await RebuildIndexAsync(cancellationToken);
        return new ManagedEnvironment(manifest.Identity, manifest);
    }

    public async Task<int> RebuildIndexAsync(
        CancellationToken cancellationToken = default)
    {
        var manifests = await manifestStore.ReadAllAsync(cancellationToken);
        await index.RebuildAsync(manifests, cancellationToken);
        return manifests.Count;
    }
}
