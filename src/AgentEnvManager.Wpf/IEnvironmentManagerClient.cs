using AgentEnvManager.Core.Adoption;
using AgentEnvManager.Core.Activation;
using AgentEnvManager.Core.EnvironmentVariables;
using AgentEnvManager.Core.Agents;
using AgentEnvManager.Core.Deletion;
using AgentEnvManager.Core.Diagnostics;
using AgentEnvManager.Core.Inspection;
using AgentEnvManager.Core.Migrations;
using AgentEnvManager.Core.Operations;
using AgentEnvManager.Core.Runtimes;

namespace AgentEnvManager.Wpf;

public interface IRuntimeCenterClient
{
    IReadOnlyList<RuntimeProviderDescriptor> DescribeRuntimeProviders();

    Task<RuntimeInstallPreview> PreviewRuntimeInstallAsync(
        string providerId,
        string version,
        string? mirrorUrl,
        CancellationToken cancellationToken = default);

    Task<InstalledRuntime> InstallRuntimeAsync(
        RuntimeInstallPreview preview,
        CancellationToken cancellationToken = default);

    Task<RuntimeArtifactCacheEntry> ImportRuntimeArtifactAsync(
        string providerId,
        string version,
        string sourcePath,
        CancellationToken cancellationToken = default);
}

public interface IDiagnosticsPackageClient
{
    Task<DiagnosticPackagePreview> PreviewDiagnosticPackageAsync(
        CancellationToken cancellationToken = default);

    Task<DiagnosticPackageResult> ExportDiagnosticPackageAsync(
        DiagnosticPackagePreview preview,
        string destinationPath,
        CancellationToken cancellationToken = default);
}

public interface IMigrationClient
{
    Task<MigrationPreview> PreviewMigrationAsync(
        EnvironmentFingerprint fingerprint,
        string destinationPath,
        CancellationToken cancellationToken = default);

    Task<OperationRecord> MigrateEnvironmentAsync(
        MigrationPreview preview,
        CancellationToken cancellationToken = default);
}

public interface IAgentBindingClient
{
    IReadOnlyList<string> DescribeAgentAdapters();

    Task<AgentDiscoveryResult> DiscoverAgentAsync(
        string agentName,
        AgentDiscoveryRequest request,
        CancellationToken cancellationToken = default);

    Task<AgentBindingPlan> CreateAgentBindingPlanAsync(
        string agentName,
        AgentBindingRequest request,
        CancellationToken cancellationToken = default);

    Task<AgentBinding> BindAgentAsync(
        string agentName,
        AgentBindingPlan plan,
        CancellationToken cancellationToken = default);

    Task<AgentHealthCheckResult> CheckAgentHealthAsync(
        string agentName,
        AgentBinding binding,
        CancellationToken cancellationToken = default);
}

public interface IEnvironmentManagerClient
    : IRuntimeCenterClient,
      IDiagnosticsPackageClient,
      IMigrationClient,
      IAgentBindingClient
{
    Task<InspectionReport> InspectAsync(
        CancellationToken cancellationToken = default);

    Task<AdoptionPreview> PreviewAdoptionAsync(
        EnvironmentFingerprint fingerprint,
        CancellationToken cancellationToken = default);

    Task<ManagedEnvironment> AdoptAsync(
        AdoptionPreview preview,
        CancellationToken cancellationToken = default);

    Task<VersionSwitchPreview> PreviewVersionSwitchAsync(
        EnvironmentFingerprint fingerprint,
        CancellationToken cancellationToken = default);

    Task<OperationRecord> SwitchVersionAsync(
        VersionSwitchPreview preview,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OperationRecord>> ListOperationsAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AdoptionRecoveryPoint>>
        ListEnvironmentRecoveryPointsAsync(
            CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EnvironmentVariableRecoveryPoint>>
        ListEnvironmentVariableRecoveryPointsAsync(
            CancellationToken cancellationToken = default);

    Task<OperationRecord> RollbackOperationAsync(
        string operationId,
        CancellationToken cancellationToken = default);

    Task<EnvironmentDeletionPreview> PreviewEnvironmentDeletionAsync(
        EnvironmentFingerprint fingerprint,
        IReadOnlyList<string>? associatedState = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    Task<OperationRecord> QuarantineEnvironmentAsync(
        EnvironmentDeletionPreview preview,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    Task<IReadOnlyList<QuarantinedEnvironment>>
        ListQuarantinedEnvironmentsAsync(
            CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<QuarantinedEnvironment>>([]);
    }

    Task<EnvironmentRestorePreview> PreviewQuarantineRestoreAsync(
        string quarantineId,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    Task<OperationRecord> RestoreQuarantinedEnvironmentAsync(
        EnvironmentRestorePreview preview,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    Task<PermanentDeletePreview> PreviewPermanentDeleteAsync(
        string quarantineId,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    Task PermanentDeleteAsync(
        PermanentDeletePreview preview,
        bool confirmed,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    Task<OperationRollbackPlan> PreviewRollbackAsync(
        string operationId,
        CancellationToken cancellationToken = default);
}
