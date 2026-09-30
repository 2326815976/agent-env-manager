using AgentEnvManager.Core.Activation;
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
    public string Create(ActivationKey key)
    {
        return $@"%LOCALAPPDATA%\AgentEnvManager\activations\{key.Value}\current";
    }

    public string CreateManagedEntry(ActivationKey key)
    {
        return $@"%LOCALAPPDATA%\AgentEnvManager\shims\{key.Value}";
    }
}
