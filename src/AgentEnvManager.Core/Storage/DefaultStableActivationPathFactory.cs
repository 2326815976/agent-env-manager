using AgentEnvManager.Core.Adoption;
using AgentEnvManager.Core.Activation;

namespace AgentEnvManager.Core.Storage;

public sealed class DefaultStableActivationPathFactory(
    string? stateRoot = null)
    : IStableActivationPathFactory
{
    public static DefaultStableActivationPathFactory Instance { get; } = new();

    private readonly string _stateRoot = string.IsNullOrWhiteSpace(stateRoot)
        ? Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "AgentEnvManager")
        : Path.GetFullPath(stateRoot);

    public string Create(ActivationKey key)
    {
        return Path.Combine(
            _stateRoot,
            "activations",
            key.Value,
            "current");
    }

    public string CreateManagedEntry(ActivationKey key)
    {
        return Path.Combine(
            _stateRoot,
            "shims",
            key.Value);
    }
}
