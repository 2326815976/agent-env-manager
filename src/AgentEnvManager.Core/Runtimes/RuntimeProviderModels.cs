using AgentEnvManager.Core.Inspection;
using AgentEnvManager.Core.Adoption;

namespace AgentEnvManager.Core.Runtimes;

public enum RuntimeProviderMode
{
    Installable,
    ObservedOnly
}

public enum RuntimeInstallStrategy
{
    UvManagedDownload,
    OfficialArchive,
    ObservedRebuild
}

public sealed record RuntimeArtifactDescriptor(
    string Version,
    string OfficialSource,
    string License,
    string Sha256,
    RuntimeInstallStrategy InstallStrategy,
    IReadOnlyList<string> SupportedArchitectures,
    string DownloadUrl);

public sealed record RuntimeProviderDescriptor(
    string Id,
    string Name,
    EnvironmentAssetKind Kind,
    RuntimeProviderMode Mode,
    string OfficialSource,
    string License,
    RuntimeInstallStrategy InstallStrategy,
    IReadOnlyList<RuntimeArtifactDescriptor> Artifacts);

public sealed record RuntimeInstallCommand(
    string Executable,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory);

public sealed record RuntimeInstallPreview(
    RuntimeProviderDescriptor Provider,
    RuntimeArtifactDescriptor Artifact,
    EnvironmentIdentity Identity,
    EnvironmentFingerprint Fingerprint,
    string InstallRoot,
    string Location,
    string ExecutablePath,
    string ActivationIdentity,
    string StableActivationPath,
    string ManagedEntryPath,
    string Impact,
    bool IsAlreadyInstalled,
    string? OperationId = null,
    string? RecoveryPointId = null);

public sealed record InstalledRuntime(
    RuntimeProviderDescriptor Provider,
    RuntimeArtifactDescriptor Artifact,
    EnvironmentManifest Manifest,
    string ExecutablePath,
    string ManagedEntryPath);

public interface IRuntimeProvider
{
    RuntimeProviderDescriptor Descriptor { get; }

    RuntimeInstallCommand CreateInstallCommand(
        RuntimeArtifactDescriptor artifact,
        string installRoot);

    string GetExecutableRelativePath(
        RuntimeArtifactDescriptor artifact);
}
