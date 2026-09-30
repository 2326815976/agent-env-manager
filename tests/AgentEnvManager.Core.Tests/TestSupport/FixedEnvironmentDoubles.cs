using AgentEnvManager.Core.Adoption;
using AgentEnvManager.Core.Inspection;

namespace AgentEnvManager.Core.Tests.TestSupport;

internal sealed class FixedAssetHasher(string value) : IEnvironmentAssetHasher
{
    public Task<string> ComputeHashAsync(
        EnvironmentAsset asset,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(value);
    }
}

internal sealed class FixedActivationPathFactory : IStableActivationPathFactory
{
    public string Create(EnvironmentIdentity identity)
    {
        return $@"%LOCALAPPDATA%\AgentEnvManager\activations\{identity.Value}\current";
    }
}
