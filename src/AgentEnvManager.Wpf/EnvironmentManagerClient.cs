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

public sealed class EnvironmentManagerClient(EnvironmentManager manager)
    : IEnvironmentManagerClient
{
    public Task<InspectionReport> InspectAsync(
        CancellationToken cancellationToken = default)
    {
        return manager.InspectAsync(cancellationToken);
    }

    public IReadOnlyList<RuntimeProviderDescriptor>
        DescribeRuntimeProviders()
    {
        return manager.DescribeRuntimeProviders();
    }

    public Task<IReadOnlyList<ManagedRuntimeStatus>>
        DescribeManagedRuntimesAsync(
            CancellationToken cancellationToken = default)
    {
        return manager.DescribeManagedRuntimesAsync(cancellationToken);
    }

    public Task<EnvironmentVariableEditorSnapshot>
        InspectEnvironmentVariableEditorAsync(
            CancellationToken cancellationToken = default)
    {
        return manager.InspectEnvironmentVariableEditorAsync(
            cancellationToken);
    }

    public Task<EnvironmentVariableUpdatePreview>
        PreviewManagedEnvironmentUpdateAsync(
            IReadOnlyList<string>? managedEntries,
            IReadOnlyList<EnvironmentVariableChange>? variableChanges,
            CancellationToken cancellationToken = default)
    {
        return manager.PreviewManagedEnvironmentUpdateAsync(
            managedEntries,
            variableChanges,
            cancellationToken);
    }

    public Task<EnvironmentVariableTransactionResult>
        ApplyEnvironmentVariableUpdateAsync(
            EnvironmentVariableUpdatePreview preview,
            CancellationToken cancellationToken = default)
    {
        return manager.ApplyEnvironmentVariableUpdateAsync(
            preview,
            cancellationToken);
    }

    public Task<RuntimeInstallPreview> PreviewRuntimeInstallAsync(
        string providerId,
        string version,
        string? mirrorUrl,
        string? installRoot,
        CancellationToken cancellationToken = default)
    {
        return manager.PreviewRuntimeInstallAsync(
            providerId,
            version,
            mirrorUrl,
            installRoot,
            cancellationToken);
    }

    public Task<InstalledRuntime> InstallRuntimeAsync(
        RuntimeInstallPreview preview,
        CancellationToken cancellationToken = default)
    {
        return manager.InstallRuntimeAsync(preview, cancellationToken);
    }

    public Task<RuntimeArtifactCacheEntry> ImportRuntimeArtifactAsync(
        string providerId,
        string version,
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        return manager.ImportRuntimeArtifactAsync(
            providerId,
            version,
            sourcePath,
            cancellationToken);
    }

    public Task<DiagnosticPackagePreview> PreviewDiagnosticPackageAsync(
        CancellationToken cancellationToken = default)
    {
        return manager.PreviewDiagnosticPackageAsync(cancellationToken);
    }

    public Task<DiagnosticPackageResult> ExportDiagnosticPackageAsync(
        DiagnosticPackagePreview preview,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        return manager.ExportDiagnosticPackageAsync(
            preview,
            destinationPath,
            cancellationToken);
    }

    public Task<MigrationPreview> PreviewMigrationAsync(
        EnvironmentFingerprint fingerprint,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        return manager.PreviewMigrationAsync(
            fingerprint,
            destinationPath,
            cancellationToken);
    }

    public Task<OperationRecord> MigrateEnvironmentAsync(
        MigrationPreview preview,
        CancellationToken cancellationToken = default)
    {
        return manager.MigrateEnvironmentAsync(
            preview,
            cancellationToken);
    }

    public IReadOnlyList<string> DescribeAgentAdapters()
    {
        return manager.DescribeAgentAdapters();
    }

    public Task<AgentDiscoveryResult> DiscoverAgentAsync(
        string agentName,
        AgentDiscoveryRequest request,
        CancellationToken cancellationToken = default)
    {
        return manager.DiscoverAgentAsync(
            agentName,
            request,
            cancellationToken);
    }

    public Task<AgentBindingPlan> CreateAgentBindingPlanAsync(
        string agentName,
        AgentBindingRequest request,
        CancellationToken cancellationToken = default)
    {
        return manager.CreateAgentBindingPlanAsync(
            agentName,
            request,
            cancellationToken);
    }

    public Task<AgentBinding> BindAgentAsync(
        string agentName,
        AgentBindingPlan plan,
        CancellationToken cancellationToken = default)
    {
        return manager.BindAgentAsync(
            agentName,
            plan,
            cancellationToken);
    }

    public Task<AgentHealthCheckResult> CheckAgentHealthAsync(
        string agentName,
        AgentBinding binding,
        CancellationToken cancellationToken = default)
    {
        return manager.CheckAgentHealthAsync(
            agentName,
            binding,
            cancellationToken);
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

    public Task<VersionSwitchPreview> PreviewVersionSwitchAsync(
        EnvironmentFingerprint fingerprint,
        CancellationToken cancellationToken = default)
    {
        return manager.PreviewVersionSwitchAsync(
            fingerprint,
            cancellationToken);
    }

    public Task<OperationRecord> SwitchVersionAsync(
        VersionSwitchPreview preview,
        CancellationToken cancellationToken = default)
    {
        return manager.SwitchVersionAsync(preview, cancellationToken);
    }

    public Task<IReadOnlyList<OperationRecord>> ListOperationsAsync(
        CancellationToken cancellationToken = default)
    {
        return manager.ListOperationsAsync(cancellationToken);
    }

    public Task<IReadOnlyList<AdoptionRecoveryPoint>>
        ListEnvironmentRecoveryPointsAsync(
            CancellationToken cancellationToken = default)
    {
        return manager.ListEnvironmentRecoveryPointsAsync(cancellationToken);
    }

    public Task<IReadOnlyList<EnvironmentVariableRecoveryPoint>>
        ListEnvironmentVariableRecoveryPointsAsync(
            CancellationToken cancellationToken = default)
    {
        return manager.ListEnvironmentVariableRecoveryPointsAsync(
            cancellationToken);
    }

    public Task<OperationRecord> RollbackOperationAsync(
        string operationId,
        CancellationToken cancellationToken = default)
    {
        return manager.RollbackOperationAsync(operationId, cancellationToken);
    }

    public Task<OperationRollbackPlan> PreviewRollbackAsync(
        string operationId,
        CancellationToken cancellationToken = default)
    {
        return manager.PreviewRollbackAsync(operationId, cancellationToken);
    }

    public Task<EnvironmentDeletionPreview> PreviewEnvironmentDeletionAsync(
        EnvironmentFingerprint fingerprint,
        IReadOnlyList<string>? associatedState = null,
        CancellationToken cancellationToken = default)
    {
        return manager.PreviewEnvironmentDeletionAsync(
            fingerprint,
            associatedState,
            cancellationToken);
    }

    public Task<OperationRecord> QuarantineEnvironmentAsync(
        EnvironmentDeletionPreview preview,
        CancellationToken cancellationToken = default)
    {
        return manager.QuarantineEnvironmentAsync(
            preview,
            cancellationToken);
    }

    public Task<IReadOnlyList<QuarantinedEnvironment>>
        ListQuarantinedEnvironmentsAsync(
            CancellationToken cancellationToken = default)
    {
        return manager.ListQuarantinedEnvironmentsAsync(cancellationToken);
    }

    public Task<EnvironmentRestorePreview> PreviewQuarantineRestoreAsync(
        string quarantineId,
        CancellationToken cancellationToken = default)
    {
        return manager.PreviewQuarantineRestoreAsync(
            quarantineId,
            cancellationToken);
    }

    public Task<OperationRecord> RestoreQuarantinedEnvironmentAsync(
        EnvironmentRestorePreview preview,
        CancellationToken cancellationToken = default)
    {
        return manager.RestoreQuarantinedEnvironmentAsync(
            preview,
            cancellationToken);
    }

    public Task<PermanentDeletePreview> PreviewPermanentDeleteAsync(
        string quarantineId,
        CancellationToken cancellationToken = default)
    {
        return manager.PreviewPermanentDeleteAsync(
            quarantineId,
            cancellationToken);
    }

    public Task PermanentDeleteAsync(
        PermanentDeletePreview preview,
        bool confirmed,
        CancellationToken cancellationToken = default)
    {
        return manager.PermanentDeleteAsync(
            preview,
            confirmed,
            cancellationToken);
    }
}
