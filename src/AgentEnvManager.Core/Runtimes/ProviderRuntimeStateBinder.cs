using AgentEnvManager.Core.Activation;
using AgentEnvManager.Core.Adoption;

namespace AgentEnvManager.Core.Runtimes;

internal sealed class ProviderRuntimeStateBinder(
    IReadOnlyDictionary<string, IRuntimeProvider> providers,
    string runtimeStateRoot) : IRuntimeStateBinder
{
    public async Task BindAsync(
        EnvironmentManifest manifest,
        CancellationToken cancellationToken = default)
    {
        var provider = providers.Values.FirstOrDefault(candidate =>
            string.Equals(
                candidate.Descriptor.Name,
                manifest.Name,
                StringComparison.Ordinal));
        if (provider is null)
        {
            return;
        }

        if (manifest.Source != provider.Descriptor.Source)
        {
            return;
        }

        var stateDirectory = RuntimeStateLayout.GetVersionStateDirectory(
            runtimeStateRoot,
            manifest.Identity.Value);
        foreach (var file in provider.CreateStateFiles(
            new RuntimeStateBindingContext(manifest, stateDirectory)))
        {
            var directory = Path.GetDirectoryName(file.Path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(
                file.Path,
                file.Content,
                cancellationToken);
        }
    }
}
