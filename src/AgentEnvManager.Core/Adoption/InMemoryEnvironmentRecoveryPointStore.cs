namespace AgentEnvManager.Core.Adoption;

internal sealed class InMemoryEnvironmentRecoveryPointStore
    : IEnvironmentRecoveryPointStore
{
    public static InMemoryEnvironmentRecoveryPointStore Instance { get; } = new();

    public Task<AdoptionRecoveryPoint> CreateAsync(
        AdoptionPreview preview,
        EnvironmentManifest? existingManifest,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new AdoptionRecoveryPoint(
            Guid.NewGuid().ToString("N"),
            preview.Fingerprint,
            existingManifest?.Identity,
            "纳管前记录 manifest 变更，原始环境文件不会被修改。",
            DateTimeOffset.UtcNow));
    }
}
