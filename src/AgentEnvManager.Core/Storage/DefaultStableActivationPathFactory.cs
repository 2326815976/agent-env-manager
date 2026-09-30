using AgentEnvManager.Core.Adoption;
using AgentEnvManager.Core.Activation;

namespace AgentEnvManager.Core.Storage;

public sealed class DefaultStableActivationPathFactory : IStableActivationPathFactory
{
    public static DefaultStableActivationPathFactory Instance { get; } = new();

    public string Create(ActivationKey key)
    {
        return Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "AgentEnvManager",
            "activations",
            key.Value,
            "current");
    }

    public string CreateManagedEntry(ActivationKey key)
    {
        return Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "AgentEnvManager",
            "shims",
            key.Value);
    }
}
