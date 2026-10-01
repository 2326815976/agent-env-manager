using System.Reflection;
using AgentEnvManager.Core.Agents;
using AgentEnvManager.Core.EnvironmentVariables;
using AgentEnvManager.Core.Inspection;
using AgentEnvManager.Core.Migrations;
using AgentEnvManager.Core.Operations;
using AgentEnvManager.Core.Runtimes;
using AgentEnvManager.Wpf;

namespace AgentEnvManager.Wpf.Tests;

public sealed class ClientRoleBoundaryTests
{
    [Fact]
    public void Workflow_roles_are_narrower_than_the_app_facade()
    {
        Type[] roles =
        [
            typeof(IRuntimeCatalogClient),
            typeof(IRuntimeCenterClient),
            typeof(IEnvironmentVariablesClient),
            typeof(IMigrationClient),
            typeof(IAgentDiscoveryClient),
            typeof(IAgentBindingClient),
            typeof(IOperationJournalClient),
            typeof(IEnvironmentDeletionClient)
        ];

        foreach (var role in roles)
        {
            Assert.False(
                typeof(IEnvironmentManagerClient).IsAssignableFrom(role),
                $"{role.Name} 不应隐含整应用客户端。");
        }
    }

    [Fact]
    public void Runtime_install_preview_requires_an_explicit_install_root()
    {
        var withInstallRoot = typeof(IRuntimeCenterClient)
            .GetMethods()
            .Where(method => method.Name == nameof(
                IRuntimeCenterClient.PreviewRuntimeInstallAsync))
            .Where(method => method.GetParameters().Any(
                parameter => parameter.Name == "installRoot"))
            .ToArray();

        Assert.NotEmpty(withInstallRoot);
        Assert.All(
            withInstallRoot,
            overload => Assert.Null(overload.GetMethodBody()));
    }

    [Fact]
    public void Deletion_role_requires_quarantine_list_capability()
    {
        var method = typeof(IEnvironmentDeletionClient).GetMethod(
            nameof(IEnvironmentDeletionClient
                .ListQuarantinedEnvironmentsAsync),
            BindingFlags.Public
            | BindingFlags.Instance
            | BindingFlags.DeclaredOnly);

        Assert.NotNull(method);
        Assert.Null(method!.GetMethodBody());
    }

    [Fact]
    public void Discovery_role_is_separate_from_binding_role()
    {
        const BindingFlags Declared = BindingFlags.Public
            | BindingFlags.Instance
            | BindingFlags.DeclaredOnly;

        Assert.NotNull(typeof(IAgentDiscoveryClient).GetMethod(
            nameof(IAgentDiscoveryClient.DiscoverAgentAsync),
            Declared));
        Assert.Null(typeof(IAgentBindingClient).GetMethod(
            nameof(IAgentDiscoveryClient.DiscoverAgentAsync),
            Declared));
        Assert.Null(typeof(IAgentBindingClient).GetMethod(
            nameof(IAgentDiscoveryClient.DescribeAgentAdapters),
            Declared));
    }

    [Fact]
    public async Task New_capabilities_use_role_scoped_clients_without_app_client()
    {
        var runtime = new RoleScopedRuntimeCenterClient();
        var variables = new RoleScopedEnvironmentVariablesClient();
        var migration = new RoleScopedMigrationClient();
        var discovery = new RoleScopedDiscoveryClient();
        var binding = new RoleScopedBindingClient();
        var catalog = new RoleScopedRuntimeCatalogClient();

        var runtimeCenter = new RuntimeCenterViewModel(runtime);
        await runtimeCenter.LoadProvidersAsync();
        Assert.Single(runtimeCenter.Providers);

        var environmentVariables = new EnvironmentVariablesViewModel(
            variables);
        await environmentVariables.LoadAsync();
        Assert.Single(environmentVariables.Variables);

        var migrationCenter = new MigrationCenterViewModel(migration);
        var agentBinding = new AgentBindingViewModel(
            discovery,
            binding,
            catalog);

        Assert.NotNull(migrationCenter);
        Assert.Equal(
            ["ChatGPT", "CC Switch"],
            agentBinding.AvailableAgents);
        Assert.Single(agentBinding.AvailableRuntimeOptions);
    }

    private class RoleScopedRuntimeCatalogClient : IRuntimeCatalogClient
    {
        public virtual IReadOnlyList<RuntimeProviderDescriptor>
            DescribeRuntimeProviders()
        {
            return [new PythonRuntimeProvider().Descriptor];
        }
    }

    private sealed class RoleScopedRuntimeCenterClient
        : RoleScopedRuntimeCatalogClient,
          IRuntimeCenterClient
    {
        public Task<IReadOnlyList<ManagedRuntimeStatus>>
            DescribeManagedRuntimesAsync(
                CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<ManagedRuntimeStatus>>([]);
        }

        public Task<RuntimeInstallPreview> PreviewRuntimeInstallAsync(
            string providerId,
            string version,
            string? mirrorUrl,
            string? installRoot,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException(
                "测试替身未提供运行时安装预览能力。");
        }

        public Task<InstalledRuntime> InstallRuntimeAsync(
            RuntimeInstallPreview preview,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException(
                "测试替身未提供运行时安装能力。");
        }

        public Task<RuntimeArtifactCacheEntry> ImportRuntimeArtifactAsync(
            string providerId,
            string version,
            string sourcePath,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException(
                "测试替身未提供离线制品导入能力。");
        }
    }

    private sealed class RoleScopedEnvironmentVariablesClient
        : IEnvironmentVariablesClient
    {
        public Task<EnvironmentVariableEditorSnapshot>
            InspectEnvironmentVariableEditorAsync(
                CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                new EnvironmentVariableEditorSnapshot(
                    @"C:\Tools",
                    [],
                    [
                        new EnvironmentVariableEditorVariable(
                            "AGENT_ENV_MANAGER_MODE",
                            "system",
                            IsExpandable: false)
                    ]));
        }

        public Task<EnvironmentVariableUpdatePreview>
            PreviewManagedEnvironmentUpdateAsync(
                IReadOnlyList<string>? managedEntries,
                IReadOnlyList<EnvironmentVariableChange>? variableChanges,
                CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException(
                "测试替身未提供环境变量预览能力。");
        }

        public Task<EnvironmentVariableTransactionResult>
            ApplyEnvironmentVariableUpdateAsync(
                EnvironmentVariableUpdatePreview preview,
                CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException(
                "测试替身未提供环境变量应用能力。");
        }
    }

    private sealed class RoleScopedMigrationClient : IMigrationClient
    {
        public Task<MigrationPreview> PreviewMigrationAsync(
            Core.Adoption.EnvironmentFingerprint fingerprint,
            string destinationPath,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException(
                "测试替身未提供迁移预览能力。");
        }

        public Task<OperationRecord> MigrateEnvironmentAsync(
            MigrationPreview preview,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException(
                "测试替身未提供迁移执行能力。");
        }
    }

    private sealed class RoleScopedDiscoveryClient : IAgentDiscoveryClient
    {
        public IReadOnlyList<string> DescribeAgentAdapters()
        {
            return ["ChatGPT", "CC Switch"];
        }

        public Task<AgentDiscoveryResult> DiscoverAgentAsync(
            string agentName,
            AgentDiscoveryRequest request,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException(
                "测试替身未提供 Agent 发现能力。");
        }
    }

    private sealed class RoleScopedBindingClient : IAgentBindingClient
    {
        public Task<AgentBindingPlan> CreateAgentBindingPlanAsync(
            string agentName,
            AgentBindingRequest request,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException(
                "测试替身未提供 Agent 绑定计划能力。");
        }

        public Task<AgentBinding> BindAgentAsync(
            string agentName,
            AgentBindingPlan plan,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException(
                "测试替身未提供 Agent 绑定能力。");
        }

        public Task<AgentHealthCheckResult> CheckAgentHealthAsync(
            string agentName,
            AgentBinding binding,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException(
                "测试替身未提供 Agent 健康检查能力。");
        }
    }
}
