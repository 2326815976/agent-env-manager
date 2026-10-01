using AgentEnvManager.Core.Adoption;
using AgentEnvManager.Core.Agents;
using AgentEnvManager.Core.Diagnostics;
using AgentEnvManager.Core.EnvironmentVariables;
using AgentEnvManager.Core.Inspection;
using AgentEnvManager.Core.Migrations;
using AgentEnvManager.Core.Operations;
using AgentEnvManager.Core.Runtimes;

namespace AgentEnvManager.Cli.Tests;

internal sealed class FakeCliEnvironmentManager : ICliEnvironmentManager
{
    public RuntimeInstallPreview RuntimeInstallPreview { get; set; } =
        null!;

    public AdoptionPreview AdoptionPreview { get; set; } = null!;

    public int AdoptCalls { get; private set; }

    public IReadOnlyList<RuntimeProviderDescriptor> RuntimeProviders { get; set; } =
        [];

    public InstalledRuntime InstalledRuntime { get; set; } = null!;

    public MigrationPreview MigrationPreview { get; set; } = null!;

    public int MigrateCalls { get; private set; }

    public OperationRecord MigratedOperation { get; set; } = null!;

    public OperationRollbackPlan RollbackPlan { get; set; } = null!;

    public int RollbackCalls { get; private set; }

    public OperationRecord RolledBackOperation { get; set; } = null!;

    public AgentDiscoveryResult AgentDiscovery { get; set; } = null!;

    public string? LastDiscoveryAgent { get; private set; }

    public AgentDiscoveryRequest? LastDiscoveryRequest { get; private set; }

    public AgentBindingPlan AgentBindingPlan { get; set; } = null!;

    public string? LastBindingAgent { get; private set; }

    public AgentBindingRequest? LastBindingRequest { get; private set; }

    public int BindCalls { get; private set; }

    public AgentBinding AgentBinding { get; set; } = null!;

    public IReadOnlyList<OperationRecord> Operations { get; set; } = [];

    public AgentHealthCheckResult AgentHealth { get; set; } = null!;

    public int HealthCalls { get; private set; }

    public string? LastHealthAgent { get; private set; }

    public AgentBinding? LastHealthBinding { get; private set; }

    public int InstallCalls { get; private set; }

    public string? LastRuntimeProviderId { get; private set; }

    public string? LastRuntimeVersion { get; private set; }

    public string? LastRuntimeMirrorUrl { get; private set; }

    public string? LastRuntimeInstallRoot { get; private set; }

    public int ImportCalls { get; private set; }

    public RuntimeArtifactCacheEntry ImportedArtifact { get; set; } =
        null!;

    public Task<InspectionReport> InspectAsync(
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    public Task<AdoptionPreview> PreviewAdoptionAsync(
        EnvironmentFingerprint fingerprint,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(AdoptionPreview);
    }

    public Task<ManagedEnvironment> AdoptAsync(
        AdoptionPreview preview,
        CancellationToken cancellationToken = default)
    {
        AdoptCalls++;
        throw new NotSupportedException();
    }

    public Task<int> RebuildEnvironmentIndexAsync(
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    public Task<DiagnosticPackagePreview> PreviewDiagnosticPackageAsync(
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    public Task<DiagnosticPackageResult> ExportDiagnosticPackageAsync(
        DiagnosticPackagePreview preview,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    public IReadOnlyList<RuntimeProviderDescriptor>
        DescribeRuntimeProviders()
    {
        return RuntimeProviders;
    }

    public Task<RuntimeInstallPreview> PreviewRuntimeInstallAsync(
        string providerId,
        string version,
        string? mirrorUrl,
        string? installRoot,
        CancellationToken cancellationToken = default)
    {
        LastRuntimeProviderId = providerId;
        LastRuntimeVersion = version;
        LastRuntimeMirrorUrl = mirrorUrl;
        LastRuntimeInstallRoot = installRoot;
        return Task.FromResult(RuntimeInstallPreview);
    }

    public Task<InstalledRuntime> InstallRuntimeAsync(
        RuntimeInstallPreview preview,
        CancellationToken cancellationToken = default)
    {
        InstallCalls++;
        return Task.FromResult(InstalledRuntime);
    }

    public Task<RuntimeArtifactCacheEntry> ImportRuntimeArtifactAsync(
        string providerId,
        string version,
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        ImportCalls++;
        return Task.FromResult(ImportedArtifact);
    }

    public Task<MigrationPreview> PreviewMigrationAsync(
        string fingerprint,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(MigrationPreview);
    }

    public Task<OperationRecord> MigrateEnvironmentAsync(
        MigrationPreview preview,
        CancellationToken cancellationToken = default)
    {
        MigrateCalls++;
        return Task.FromResult(MigratedOperation);
    }

    public Task<OperationRollbackPlan> PreviewRollbackAsync(
        string operationId,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(RollbackPlan);
    }

    public Task<OperationRecord> RollbackOperationAsync(
        string operationId,
        CancellationToken cancellationToken = default)
    {
        RollbackCalls++;
        return Task.FromResult(RolledBackOperation);
    }

    public Task<AgentDiscoveryResult> DiscoverAgentAsync(
        string agentName,
        AgentDiscoveryRequest request,
        CancellationToken cancellationToken = default)
    {
        LastDiscoveryAgent = agentName;
        LastDiscoveryRequest = request;
        return Task.FromResult(AgentDiscovery);
    }

    public Task<AgentBindingPlan> CreateAgentBindingPlanAsync(
        string agentName,
        AgentBindingRequest request,
        CancellationToken cancellationToken = default)
    {
        LastBindingAgent = agentName;
        LastBindingRequest = request;
        return Task.FromResult(AgentBindingPlan);
    }

    public Task<AgentBinding> BindAgentAsync(
        string agentName,
        AgentBindingPlan plan,
        CancellationToken cancellationToken = default)
    {
        BindCalls++;
        return Task.FromResult(AgentBinding);
    }

    public Task<AgentHealthCheckResult> CheckAgentHealthAsync(
        string agentName,
        AgentBinding binding,
        CancellationToken cancellationToken = default)
    {
        HealthCalls++;
        LastHealthAgent = agentName;
        LastHealthBinding = binding;
        return Task.FromResult(AgentHealth);
    }

    public Task<IReadOnlyList<OperationRecord>> ListOperationsAsync(
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Operations);
    }

    public EnvironmentVariableEditorSnapshot EnvironmentVariableSnapshot
    { get; set; } = null!;

    public EnvironmentVariableUpdatePreview EnvironmentVariablePreview
    { get; set; } = null!;

    public EnvironmentVariableTransactionResult EnvironmentVariableResult
    { get; set; } = null!;

    public int VariableApplyCalls { get; private set; }

    public IReadOnlyList<string>? LastManagedEntries { get; private set; }

    public IReadOnlyList<EnvironmentVariableChange>? LastVariableChanges
    { get; private set; }

    public Task<EnvironmentVariableEditorSnapshot>
        InspectEnvironmentVariableEditorAsync(
            CancellationToken cancellationToken = default)
    {
        return Task.FromResult(EnvironmentVariableSnapshot);
    }

    public Task<EnvironmentVariableUpdatePreview>
        PreviewManagedEnvironmentUpdateAsync(
            IReadOnlyList<string>? managedEntries,
            IReadOnlyList<EnvironmentVariableChange>? variableChanges,
            CancellationToken cancellationToken = default)
    {
        LastManagedEntries = managedEntries;
        LastVariableChanges = variableChanges;
        return Task.FromResult(EnvironmentVariablePreview);
    }

    public Task<EnvironmentVariableTransactionResult>
        ApplyEnvironmentVariableUpdateAsync(
            EnvironmentVariableUpdatePreview preview,
            CancellationToken cancellationToken = default)
    {
        VariableApplyCalls++;
        return Task.FromResult(EnvironmentVariableResult);
    }
}
