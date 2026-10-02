using AgentEnvManager.Core.Activation;
using AgentEnvManager.Core.Adoption;
using AgentEnvManager.Core.Agents;
using AgentEnvManager.Core.Deletion;
using AgentEnvManager.Core.Diagnostics;
using AgentEnvManager.Core.EnvironmentVariables;
using AgentEnvManager.Core.Inspection;
using AgentEnvManager.Core.Migrations;
using AgentEnvManager.Core.Operations;
using AgentEnvManager.Core.Runtimes;

namespace AgentEnvManager.Wpf;

public interface IRuntimeCatalogClient
{
    IReadOnlyList<RuntimeProviderDescriptor> DescribeRuntimeProviders();
}

public interface IRuntimeCenterClient : IRuntimeCatalogClient
{
    Task<IReadOnlyList<ManagedRuntimeStatus>> DescribeManagedRuntimesAsync(
        CancellationToken cancellationToken = default);

    Task<RuntimeInstallPreview> PreviewRuntimeInstallAsync(
        string providerId,
        string version,
        string? mirrorUrl,
        string? installRoot,
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

public interface IEnvironmentVariablesClient
{
    Task<EnvironmentVariableEditorSnapshot>
        InspectEnvironmentVariableEditorAsync(
            CancellationToken cancellationToken = default);

    Task<EnvironmentVariableUpdatePreview>
        PreviewManagedEnvironmentUpdateAsync(
            IReadOnlyList<string>? managedEntries,
            IReadOnlyList<EnvironmentVariableChange>? variableChanges,
            CancellationToken cancellationToken = default);

    Task<EnvironmentVariableTransactionResult>
        ApplyEnvironmentVariableUpdateAsync(
            EnvironmentVariableUpdatePreview preview,
            CancellationToken cancellationToken = default);
}

public interface IAgentDiscoveryClient
{
    IReadOnlyList<string> DescribeAgentAdapters();

    Task<AgentDiscoveryResult> DiscoverAgentAsync(
        string agentName,
        AgentDiscoveryRequest request,
        CancellationToken cancellationToken = default);
}

public interface ICcSwitchClient
{
    Task<CcSwitchDiscovery> DiscoverCcSwitchAsync(
        CcSwitchDiscoveryRequest? request = null,
        CancellationToken cancellationToken = default);

    Task<CcSwitchBindingPreview> PreviewCcSwitchCodexConfigDirAsync(
        string configRoot,
        string targetCodexConfigDirectory,
        CancellationToken cancellationToken = default);

    Task<CcSwitchBindingResult> ApplyCcSwitchCodexConfigDirAsync(
        CcSwitchBindingPreview preview,
        CancellationToken cancellationToken = default);
}

public interface ICoordinatedMigrationClient
{
    Task<CoordinatedMigrationPreview> PreviewCoordinatedMigrationAsync(
        CoordinatedMigrationRequest request,
        CancellationToken cancellationToken = default);

    Task<CoordinatedMigrationResult> ApplyCoordinatedMigrationAsync(
        CoordinatedMigrationPreview preview,
        CancellationToken cancellationToken = default);
}

public interface IAgentBindingClient
{
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

public interface IOperationJournalClient
{
    Task<IReadOnlyList<OperationRecord>> ListOperationsAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AdoptionRecoveryPoint>>
        ListEnvironmentRecoveryPointsAsync(
            CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EnvironmentVariableRecoveryPoint>>
        ListEnvironmentVariableRecoveryPointsAsync(
            CancellationToken cancellationToken = default);

    Task<OperationRollbackPlan> PreviewRollbackAsync(
        string operationId,
        CancellationToken cancellationToken = default);

    Task<OperationRecord> RollbackOperationAsync(
        string operationId,
        CancellationToken cancellationToken = default);
}

public interface IEnvironmentDeletionClient
{
    Task<EnvironmentDeletionPreview> PreviewEnvironmentDeletionAsync(
        EnvironmentFingerprint fingerprint,
        IReadOnlyList<string>? associatedState = null,
        CancellationToken cancellationToken = default);

    Task<OperationRecord> QuarantineEnvironmentAsync(
        EnvironmentDeletionPreview preview,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<QuarantinedEnvironment>>
        ListQuarantinedEnvironmentsAsync(
            CancellationToken cancellationToken = default);

    Task<EnvironmentRestorePreview> PreviewQuarantineRestoreAsync(
        string quarantineId,
        CancellationToken cancellationToken = default);

    Task<OperationRecord> RestoreQuarantinedEnvironmentAsync(
        EnvironmentRestorePreview preview,
        CancellationToken cancellationToken = default);

    Task<PermanentDeletePreview> PreviewPermanentDeleteAsync(
        string quarantineId,
        CancellationToken cancellationToken = default);

    Task PermanentDeleteAsync(
        PermanentDeletePreview preview,
        bool confirmed,
        CancellationToken cancellationToken = default);
}

public interface IEnvironmentManagerClient
    : IRuntimeCenterClient,
      IDiagnosticsPackageClient,
      IEnvironmentVariablesClient,
      IMigrationClient,
      IAgentDiscoveryClient,
      ICcSwitchClient,
      ICoordinatedMigrationClient,
      IAgentBindingClient,
      IOperationJournalClient,
      IEnvironmentDeletionClient
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
}
