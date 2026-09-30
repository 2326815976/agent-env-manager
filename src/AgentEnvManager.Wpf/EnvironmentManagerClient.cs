using AgentEnvManager.Core.Adoption;
using AgentEnvManager.Core.Inspection;

namespace AgentEnvManager.Wpf;

public sealed class EnvironmentManagerClient(EnvironmentManager manager)
    : IEnvironmentManagerClient
{
    public Task<InspectionReport> InspectAsync(
        CancellationToken cancellationToken = default)
    {
        return manager.InspectAsync(cancellationToken);
    }

    public Task<AdoptionPreview> PreviewAdoptionAsync(
        EnvironmentFingerprint fingerprint,
        CancellationToken cancellationToken = default)
    {
        return manager.PreviewAdoptionAsync(fingerprint, cancellationToken);
    }

    public Task<ManagedEnvironment> AdoptAsync(
        AdoptionPreview preview,
        CancellationToken cancellationToken = default)
    {
        return manager.AdoptAsync(preview, cancellationToken);
    }
}
