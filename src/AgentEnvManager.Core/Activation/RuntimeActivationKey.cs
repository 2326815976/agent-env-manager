using AgentEnvManager.Core.Inspection;

namespace AgentEnvManager.Core.Activation;

internal static class RuntimeActivationKey
{
    public static string Create(EnvironmentAsset asset)
    {
        var name = new string(asset.Name
            .Trim()
            .Select(character => char.IsLetterOrDigit(character)
                ? character
                : '-')
            .ToArray())
            .Trim('-');
        return $"{asset.Kind}-{name}";
    }
}
