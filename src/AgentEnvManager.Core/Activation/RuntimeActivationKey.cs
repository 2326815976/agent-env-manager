using System.Security.Cryptography;
using System.Text;
using AgentEnvManager.Core.Adoption;
using AgentEnvManager.Core.Inspection;

namespace AgentEnvManager.Core.Activation;

internal static class RuntimeActivationKey
{
    public static ActivationKey Create(EnvironmentAsset asset)
    {
        var readableName = new string(asset.Name
            .Trim()
            .Select(character => char.IsLetterOrDigit(character)
                ? character
                : '-')
            .ToArray())
            .Trim('-')
            .ToLowerInvariant();
        var identityMaterial = $"{asset.Kind}\n{asset.Name.Trim()}";
        var hash = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(identityMaterial)))[..12].ToLowerInvariant();

        return new ActivationKey(new EnvironmentIdentity(
            $"runtime-activation-{asset.Kind.ToString().ToLowerInvariant()}-{readableName}-{hash}"));
    }
}
