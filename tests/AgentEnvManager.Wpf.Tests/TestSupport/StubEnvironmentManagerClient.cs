using AgentEnvManager.Core.Activation;
using AgentEnvManager.Core.Adoption;
using AgentEnvManager.Core.Agents;
using AgentEnvManager.Core.EnvironmentVariables;
using AgentEnvManager.Core.Diagnostics;
using AgentEnvManager.Core.Inspection;
using AgentEnvManager.Core.Migrations;
using AgentEnvManager.Core.Operations;
using AgentEnvManager.Core.Runtimes;

namespace AgentEnvManager.Wpf.Tests;

internal class StubEnvironmentManagerClient : IEnvironmentManagerClient
{
    public virtual Task<InspectionReport> InspectAsync(
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    public virtual Task<AdoptionPreview> PreviewAdoptionAsync(
        EnvironmentFingerprint fingerprint,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    public virtual Task<ManagedEnvironment> AdoptAsync(
        AdoptionPreview preview,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    public virtual Task<VersionSwitchPreview> PreviewVersionSwitchAsync(
        EnvironmentFingerprint fingerprint,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    public virtual Task<OperationRecord> SwitchVersionAsync(
        VersionSwitchPreview preview,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    public virtual Task<IReadOnlyList<OperationRecord>> ListOperationsAsync(
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    public virtual Task<IReadOnlyList<AdoptionRecoveryPoint>>
        ListEnvironmentRecoveryPointsAsync(
            CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    public virtual Task<IReadOnlyList<EnvironmentVariableRecoveryPoint>>
        ListEnvironmentVariableRecoveryPointsAsync(
            CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    public virtual Task<OperationRecord> RollbackOperationAsync(
        string operationId,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    public virtual Task<OperationRollbackPlan> PreviewRollbackAsync(
        string operationId,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    public virtual IReadOnlyList<RuntimeProviderDescriptor>
        DescribeRuntimeProviders()
    {
        return [];
    }

    public virtual Task<RuntimeInstallPreview> PreviewRuntimeInstallAsync(
        string providerId,
        string version,
        string? mirrorUrl,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    public virtual Task<InstalledRuntime> InstallRuntimeAsync(
        RuntimeInstallPreview preview,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    public virtual Task<RuntimeArtifactCacheEntry> ImportRuntimeArtifactAsync(
        string providerId,
        string version,
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    public virtual Task<DiagnosticPackagePreview>
        PreviewDiagnosticPackageAsync(
            CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    public virtual Task<DiagnosticPackageResult> ExportDiagnosticPackageAsync(
        DiagnosticPackagePreview preview,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    public virtual Task<MigrationPreview> PreviewMigrationAsync(
        EnvironmentFingerprint fingerprint,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    public virtual Task<OperationRecord> MigrateEnvironmentAsync(
        MigrationPreview preview,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    public virtual IReadOnlyList<string> DescribeAgentAdapters()
    {
        return [];
    }

    public virtual Task<AgentDiscoveryResult> DiscoverAgentAsync(
        string agentName,
        AgentDiscoveryRequest request,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    public virtual Task<AgentBindingPlan> CreateAgentBindingPlanAsync(
        string agentName,
        AgentBindingRequest request,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    public virtual Task<AgentBinding> BindAgentAsync(
        string agentName,
        AgentBindingPlan plan,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    public virtual Task<AgentHealthCheckResult> CheckAgentHealthAsync(
        string agentName,
        AgentBinding binding,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }
}
