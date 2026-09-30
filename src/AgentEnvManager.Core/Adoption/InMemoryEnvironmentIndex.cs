namespace AgentEnvManager.Core.Adoption;

internal sealed class InMemoryEnvironmentIndex : IEnvironmentIndex
{
    public Task RebuildAsync(
        IReadOnlyList<EnvironmentManifest> manifests,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
