namespace AgentEnvManager.Core.Adoption;

internal sealed class InMemoryEnvironmentRecoveryPointStore
    : IEnvironmentRecoveryPointStore
{
    public static InMemoryEnvironmentRecoveryPointStore Instance { get; } = new();
    private readonly Dictionary<string, AdoptionRecoveryPoint> _points = [];

    public Task<AdoptionRecoveryPoint> CreateAsync(
        AdoptionPreview preview,
        EnvironmentManifest? existingManifest,
        CancellationToken cancellationToken = default)
    {
        var point = new AdoptionRecoveryPoint(
            Guid.NewGuid().ToString("N"),
            preview.Fingerprint,
            existingManifest?.Identity,
            existingManifest,
            "纳管前记录 manifest 变更，原始环境文件不会被修改。",
            DateTimeOffset.UtcNow);
        _points[point.Id] = point;
        return Task.FromResult(point);
    }

    public Task<AdoptionRecoveryPoint?> GetAsync(
        string recoveryPointId,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_points.GetValueOrDefault(recoveryPointId));
    }
}
