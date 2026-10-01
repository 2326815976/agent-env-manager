using AgentEnvManager.Core.Adoption;

namespace AgentEnvManager.Core.Activation;

internal interface IRuntimeStateBinder
{
    Task BindAsync(
        EnvironmentManifest manifest,
        CancellationToken cancellationToken = default);
}
