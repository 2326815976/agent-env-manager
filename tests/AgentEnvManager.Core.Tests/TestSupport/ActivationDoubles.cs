using AgentEnvManager.Core.Activation;
using AgentEnvManager.Core.Adoption;
using AgentEnvManager.Core.Operations;

namespace AgentEnvManager.Core.Tests.TestSupport;

internal sealed class InMemoryManifestStore : IEnvironmentManifestStore
{
    private readonly List<EnvironmentManifest> _manifests = [];

    public Task<IReadOnlyList<EnvironmentManifest>> ReadAllAsync(
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<EnvironmentManifest>>(_manifests);
    }

    public Task<EnvironmentManifest?> FindByFingerprintAsync(
        EnvironmentFingerprint fingerprint,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_manifests.SingleOrDefault(
            manifest => manifest.Fingerprint == fingerprint));
    }

    public Task<EnvironmentManifest> SaveAsync(
        EnvironmentManifest manifest,
        CancellationToken cancellationToken = default)
    {
        _manifests.RemoveAll(item =>
            item.Identity == manifest.Identity
            || item.Fingerprint == manifest.Fingerprint);
        _manifests.Add(manifest);
        return Task.FromResult(manifest);
    }

    public Task DeleteAsync(
        EnvironmentIdentity identity,
        CancellationToken cancellationToken = default)
    {
        _manifests.RemoveAll(item => item.Identity == identity);
        return Task.CompletedTask;
    }
}

internal sealed class RecordingActivationLink : IEnvironmentActivationLink
{
    private readonly Dictionary<string, string> _targets =
        new(StringComparer.OrdinalIgnoreCase);

    public Task<string?> GetTargetAsync(
        string activationPath,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_targets.GetValueOrDefault(activationPath));
    }

    public Task SetTargetAsync(
        string activationPath,
        string targetPath,
        CancellationToken cancellationToken = default)
    {
        _targets[activationPath] = GetActivationTarget(targetPath);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(
        string activationPath,
        CancellationToken cancellationToken = default)
    {
        _targets.Remove(activationPath);
        return Task.CompletedTask;
    }

    public Task<bool> TargetExistsAsync(
        string targetPath,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(true);
    }

    public string GetActivationTarget(string location)
    {
        return string.Equals(
            Path.GetExtension(location),
            ".exe",
            StringComparison.OrdinalIgnoreCase)
            ? Path.GetDirectoryName(location) ?? location
            : location;
    }
}

internal sealed class RecordingRuntimeHealthCheck(bool isHealthy)
    : IRuntimeHealthCheck
{
    public Task<RuntimeHealthCheckResult> CheckAsync(
        EnvironmentManifest manifest,
        string activationPath,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new RuntimeHealthCheckResult(
            isHealthy,
            isHealthy ? "健康" : "目标版本健康检查失败"));
    }
}

internal sealed class RecordingRecoveryPointStore
    : IEnvironmentRecoveryPointStore
{
    private readonly List<AdoptionRecoveryPoint> _points = [];

    public Task<AdoptionRecoveryPoint> CreateAsync(
        AdoptionPreview preview,
        string operationId,
        EnvironmentManifest? existingManifest,
        CancellationToken cancellationToken = default)
    {
        var point = new AdoptionRecoveryPoint(
            $"recovery-{_points.Count + 1}",
            operationId,
            preview.Fingerprint,
            existingManifest?.Identity,
            existingManifest,
            preview.Impact,
            DateTimeOffset.UnixEpoch);
        _points.Add(point);
        return Task.FromResult(point);
    }

    public Task<AdoptionRecoveryPoint?> GetAsync(
        string recoveryPointId,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_points.FirstOrDefault(
            point => point.Id == recoveryPointId));
    }
}

internal sealed class RecordingOperationJournal : IOperationJournal
{
    public IReadOnlyList<OperationRecord> History { get; private set; } = [];

    public Task SaveAsync(
        OperationRecord operation,
        CancellationToken cancellationToken = default)
    {
        History = [.. History, operation];
        return Task.CompletedTask;
    }

    public Task<OperationRecord?> GetAsync(
        string operationId,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(History.LastOrDefault(
            operation => operation.Id == operationId));
    }
}
