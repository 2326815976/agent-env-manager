using AgentEnvManager.Core.Adoption;

namespace AgentEnvManager.Core.Storage;

public sealed class DefaultStableActivationPathFactory : IStableActivationPathFactory
{
    public static DefaultStableActivationPathFactory Instance { get; } = new();

    public string Create(EnvironmentIdentity identity)
    {
        return Path.Combine(
            "%LOCALAPPDATA%",
            "AgentEnvManager",
            "activations",
            identity.Value,
            "current");
    }
}
