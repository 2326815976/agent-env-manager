using AgentEnvManager.Core.Agents;
using AgentEnvManager.Core.Adoption;
using AgentEnvManager.Core.Diagnostics;
using AgentEnvManager.Core.Inspection;
using AgentEnvManager.Core.Migrations;
using AgentEnvManager.Core.Operations;
using AgentEnvManager.Core.Runtimes;

namespace AgentEnvManager.Cli;

internal interface ICliEnvironmentManager
{
    Task<InspectionReport> InspectAsync(
        CancellationToken cancellationToken = default);

    Task<AdoptionPreview> PreviewAdoptionAsync(
        EnvironmentFingerprint fingerprint,
        CancellationToken cancellationToken = default);

    Task<ManagedEnvironment> AdoptAsync(
        AdoptionPreview preview,
        CancellationToken cancellationToken = default);

    Task<int> RebuildEnvironmentIndexAsync(
        CancellationToken cancellationToken = default);

    Task<DiagnosticPackagePreview> PreviewDiagnosticPackageAsync(
        CancellationToken cancellationToken = default);

    Task<DiagnosticPackageResult> ExportDiagnosticPackageAsync(
        DiagnosticPackagePreview preview,
        string destinationPath,
        CancellationToken cancellationToken = default);

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

    Task<MigrationPreview> PreviewMigrationAsync(
        string fingerprint,
        string destinationPath,
        CancellationToken cancellationToken = default);

    Task<OperationRecord> MigrateEnvironmentAsync(
        MigrationPreview preview,
        CancellationToken cancellationToken = default);

    Task<OperationRollbackPlan> PreviewRollbackAsync(
        string operationId,
        CancellationToken cancellationToken = default);

    Task<OperationRecord> RollbackOperationAsync(
        string operationId,
        CancellationToken cancellationToken = default);

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

    Task<IReadOnlyList<OperationRecord>> ListOperationsAsync(
        CancellationToken cancellationToken = default);
}

internal sealed class EnvironmentManagerCliAdapter(
    EnvironmentManager manager) : ICliEnvironmentManager
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
        return manager.PreviewAdoptionAsync(
            fingerprint,
            cancellationToken);
    }

    public Task<ManagedEnvironment> AdoptAsync(
        AdoptionPreview preview,
        CancellationToken cancellationToken = default)
    {
        return manager.AdoptAsync(preview, cancellationToken);
    }

    public Task<int> RebuildEnvironmentIndexAsync(
        CancellationToken cancellationToken = default)
    {
        return manager.RebuildEnvironmentIndexAsync(cancellationToken);
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

    public IReadOnlyList<RuntimeProviderDescriptor>
        DescribeRuntimeProviders()
    {
        return manager.DescribeRuntimeProviders();
    }

    public Task<RuntimeInstallPreview> PreviewRuntimeInstallAsync(
        string providerId,
        string version,
        string? mirrorUrl,
        CancellationToken cancellationToken = default)
    {
        return manager.PreviewRuntimeInstallAsync(
            providerId,
            version,
            mirrorUrl,
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

    public Task<MigrationPreview> PreviewMigrationAsync(
        string fingerprint,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        return manager.PreviewMigrationAsync(
            new EnvironmentFingerprint(fingerprint),
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

    public Task<OperationRollbackPlan> PreviewRollbackAsync(
        string operationId,
        CancellationToken cancellationToken = default)
    {
        return manager.PreviewRollbackAsync(
            operationId,
            cancellationToken);
    }

    public Task<OperationRecord> RollbackOperationAsync(
        string operationId,
        CancellationToken cancellationToken = default)
    {
        return manager.RollbackOperationAsync(
            operationId,
            cancellationToken);
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

    public Task<IReadOnlyList<OperationRecord>> ListOperationsAsync(
        CancellationToken cancellationToken = default)
    {
        return manager.ListOperationsAsync(cancellationToken);
    }
}
