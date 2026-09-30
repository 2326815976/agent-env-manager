namespace AgentEnvManager.Core.Adoption;

internal sealed class InMemoryEnvironmentRecoveryPointStore
    : IEnvironmentRecoveryPointStore
{
    public static InMemoryEnvironmentRecoveryPointStore Instance { get; } = new();
    private readonly Dictionary<string, AdoptionRecoveryPoint> _points = [];

    public Task<IReadOnlyList<AdoptionRecoveryPoint>> ReadAllAsync(
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<AdoptionRecoveryPoint>>(
            _points.Values
                .OrderByDescending(point => point.CreatedAtUtc)
                .ToArray());
    }

    public Task<AdoptionRecoveryPoint> CreateAsync(
        AdoptionPreview preview,
        string operationId,
        EnvironmentManifest? existingManifest,
        CancellationToken cancellationToken = default)
    {
        var point = new AdoptionRecoveryPoint(
            Guid.NewGuid().ToString("N"),
            operationId,
            preview.Fingerprint,
            existingManifest?.Identity,
            existingManifest,
            preview.Impact,
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
