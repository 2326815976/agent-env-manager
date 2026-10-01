using AgentEnvManager.Core.Adoption;
using AgentEnvManager.Core.Agents;
using AgentEnvManager.Core.Operations;
using AgentEnvManager.Core.Storage;

namespace AgentEnvManager.Cli;

public static class CliApplication
{
    public static Task<int> RunAsync(
        string[] args,
        CancellationToken cancellationToken = default)
    {
        return RunWithManagerAsync(
            args,
            () => new EnvironmentManagerCliAdapter(
                EnvironmentManagerFactory.CreateDefault()),
            Console.Out,
            Console.Error,
            cancellationToken);
    }

    internal static async Task<int> RunWithManagerAsync(
        string[] args,
        Func<ICliEnvironmentManager> managerFactory,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken = default)
    {
        var request = CliArguments.Parse(args);
        if (request.Action == CliAction.Help)
        {
            CliMessages.WriteUsage(output);
            return 0;
        }

        if (request.Action == CliAction.Invalid)
        {
            error.WriteLine($"命令参数无效: {request.Error}");
            CliMessages.WriteUsage(error);
            return 2;
        }

        if (request.Action == CliAction.Unknown)
        {
            error.WriteLine($"未知命令: {request.UnknownCommand}");
            CliMessages.WriteUsage(error);
            return 2;
        }

        try
        {
            var manager = managerFactory();
            return await ExecuteAsync(
                request,
                manager,
                output,
                error,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            error.WriteLine("操作已取消。");
            return 130;
        }
        catch (Exception exception)
        {
            error.WriteLine($"操作失败: {exception.Message}");
            return 1;
        }
    }

    private static async Task<int> ExecuteAsync(
        CliRequest request,
        ICliEnvironmentManager manager,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        switch (request.Action)
        {
            case CliAction.Inspect:
                var report = await manager.InspectAsync(cancellationToken);
                InspectionReportRenderer.Write(report, output);
                return 0;

            case CliAction.PreviewAdoption:
                var preview = await manager.PreviewAdoptionAsync(
                    new EnvironmentFingerprint(request.Fingerprint!),
                    cancellationToken);
                AdoptionReportRenderer.Write(preview, output);
                return 0;

            case CliAction.Adopt:
                var fingerprint = new EnvironmentFingerprint(
                    request.Fingerprint!);
                var adoptionPreview = await manager.PreviewAdoptionAsync(
                    fingerprint,
                    cancellationToken);
                return await ExecuteConfirmedPlanAsync(
                    request.Confirmed,
                    adoptionPreview,
                    (preview, writer) =>
                        AdoptionReportRenderer.Write(preview, writer),
                    async (preview, token) =>
                    {
                        var managed = await manager.AdoptAsync(
                            preview,
                            token);
                        AdoptionReportRenderer.Write(managed, output);
                    },
                    output,
                    error,
                    cancellationToken);

            case CliAction.RebuildIndex:
                var count = await manager.RebuildEnvironmentIndexAsync(
                    cancellationToken);
                CliMessages.WriteIndexRebuild(count, output);
                return 0;

            case CliAction.RollbackPreview:
                var rollbackPlan = await manager.PreviewRollbackAsync(
                    request.OperationId!,
                    cancellationToken);
                RollbackReportRenderer.WritePreview(rollbackPlan, output);
                return 0;

            case CliAction.Rollback:
                var plannedRollback = await manager.PreviewRollbackAsync(
                    request.OperationId!,
                    cancellationToken);
                return await ExecuteConfirmedPlanAsync(
                    request.Confirmed,
                    plannedRollback,
                    (plan, writer) =>
                        RollbackReportRenderer.WritePreview(plan, writer),
                    async (_, token) =>
                    {
                        var rolledBack =
                            await manager.RollbackOperationAsync(
                                request.OperationId!,
                                token);
                        CliMessages.WriteRollback(rolledBack, output);
                    },
                    output,
                    error,
                    cancellationToken);

            case CliAction.DiagnosticsPreview:
                var diagnosticPreview =
                    await manager.PreviewDiagnosticPackageAsync(
                        cancellationToken);
                CliMessages.WriteDiagnosticPreview(
                    diagnosticPreview,
                    output);
                return 0;

            case CliAction.DiagnosticsExport:
                var reviewedPreview =
                    await manager.PreviewDiagnosticPackageAsync(
                        cancellationToken);
                var diagnosticResult =
                    await manager.ExportDiagnosticPackageAsync(
                        reviewedPreview,
                        request.DestinationPath!,
                        cancellationToken);
                CliMessages.WriteDiagnosticExport(
                    diagnosticResult,
                    output);
                return 0;

            case CliAction.RuntimeList:
                RuntimeReportRenderer.WriteProviders(
                    manager.DescribeRuntimeProviders(),
                    output);
                return 0;

            case CliAction.RuntimeInstallPreview:
                var installPreview =
                    await manager.PreviewRuntimeInstallAsync(
                        request.ProviderId!,
                        request.Version!,
                        request.MirrorUrl,
                        request.InstallRoot,
                        cancellationToken);
                RuntimeReportRenderer.WritePreview(installPreview, output);
                return 0;

            case CliAction.RuntimeInstall:
                var plannedInstall =
                    await manager.PreviewRuntimeInstallAsync(
                        request.ProviderId!,
                        request.Version!,
                        request.MirrorUrl,
                        request.InstallRoot,
                        cancellationToken);
                return await ExecuteConfirmedPlanAsync(
                    request.Confirmed,
                    plannedInstall,
                    (preview, writer) =>
                        RuntimeReportRenderer.WritePreview(preview, writer),
                    async (preview, token) =>
                    {
                        var installed = await manager.InstallRuntimeAsync(
                            preview,
                            token);
                        RuntimeReportRenderer.WriteInstalled(
                            installed,
                            preview,
                            output);
                    },
                    output,
                    error,
                    cancellationToken);

            case CliAction.RuntimeImport:
                if (!request.Confirmed)
                {
                    error.WriteLine(
                        "危险操作需要显式 --confirm 参数。");
                    return 2;
                }

                var importedArtifact =
                    await manager.ImportRuntimeArtifactAsync(
                        request.ProviderId!,
                        request.Version!,
                        request.ArtifactPath!,
                        cancellationToken);
                var importOperation = await FindOperationAsync(
                    manager,
                    importedArtifact.OperationId,
                    cancellationToken);
                RuntimeReportRenderer.WriteImportResult(
                    importedArtifact,
                    importOperation,
                    request.ProviderId!,
                    request.Version!,
                    request.ArtifactPath!,
                    output);
                return 0;

            case CliAction.MigrationPreview:
                var migrationPreview =
                    await manager.PreviewMigrationAsync(
                        request.Fingerprint!,
                        request.DestinationPath!,
                        cancellationToken);
                MigrationReportRenderer.WritePreview(
                    migrationPreview,
                    output);
                return 0;

            case CliAction.Migrate:
                var plannedMigration =
                    await manager.PreviewMigrationAsync(
                        request.Fingerprint!,
                        request.DestinationPath!,
                        cancellationToken);
                return await ExecuteConfirmedPlanAsync(
                    request.Confirmed,
                    plannedMigration,
                    (preview, writer) =>
                        MigrationReportRenderer.WritePreview(
                            preview,
                            writer),
                    async (preview, token) =>
                    {
                        var migrationOperation =
                            await manager.MigrateEnvironmentAsync(
                                preview,
                                token);
                        MigrationReportRenderer.WriteResult(
                            migrationOperation,
                            output);
                    },
                    output,
                    error,
                    cancellationToken);

            case CliAction.AgentDiscover:
                var discovery = await manager.DiscoverAgentAsync(
                    request.AgentName!,
                    new AgentDiscoveryRequest(
                        request.ConfigurationDirectory,
                        request.ExecutablePath),
                    cancellationToken);
                AgentReportRenderer.WriteDiscovery(
                    request.AgentName!,
                    discovery,
                    output);
                return 0;

            case CliAction.AgentBindPreview:
                var bindingPlan =
                    await manager.CreateAgentBindingPlanAsync(
                        request.AgentName!,
                        CreateAgentBindingRequest(request),
                        cancellationToken);
                AgentReportRenderer.WritePlan(bindingPlan, output);
                return 0;

            case CliAction.AgentBind:
                var plannedBinding =
                    await manager.CreateAgentBindingPlanAsync(
                        request.AgentName!,
                        CreateAgentBindingRequest(request),
                        cancellationToken);
                return await ExecuteConfirmedPlanAsync(
                    request.Confirmed,
                    plannedBinding,
                    (plan, writer) =>
                        AgentReportRenderer.WritePlan(plan, writer),
                    async (plan, token) =>
                    {
                        var binding = await manager.BindAgentAsync(
                            request.AgentName!,
                            plan,
                            token);
                        var operations =
                            await manager.ListOperationsAsync(token);
                        var bindingOperation = operations
                            .Where(operation =>
                                operation.Type
                                    == OperationType.AgentBinding
                                && string.Equals(
                                    operation.Target,
                                    plan.ConfigurationDirectory,
                                    StringComparison.OrdinalIgnoreCase))
                            .OrderByDescending(operation =>
                                operation.UpdatedAtUtc)
                            .FirstOrDefault();
                        AgentReportRenderer.WriteBinding(
                            binding,
                            bindingOperation?.Id,
                            output);
                    },
                    output,
                    error,
                    cancellationToken);

            case CliAction.AgentHealth:
                var healthPlan =
                    await manager.CreateAgentBindingPlanAsync(
                        request.AgentName!,
                        CreateAgentBindingRequest(request),
                        cancellationToken);
                if (!File.Exists(healthPlan.BindingFilePath))
                {
                    throw new InvalidOperationException(
                        "未找到 Agent 绑定文件，请先执行 agent-bind。");
                }

                var health = await manager.CheckAgentHealthAsync(
                    request.AgentName!,
                    CreateHealthBinding(healthPlan),
                    cancellationToken);
                AgentReportRenderer.WriteHealth(
                    request.AgentName!,
                    health,
                    output);
                return 0;

            default:
                throw new InvalidOperationException("未实现的 CLI 操作。");
        }
    }

    private static AgentBindingRequest CreateAgentBindingRequest(
        CliRequest request)
    {
        return new AgentBindingRequest(
            request.ConfigurationDirectory!,
            request.ExecutablePath!,
            request.ManagedEntryPath!,
            request.RuntimeName!,
            request.RuntimeVersion!,
            WorkspacePath: request.WorkspacePath,
            RuntimeCommand: request.RuntimeCommand);
    }

    private static async Task<int> ExecuteConfirmedPlanAsync<TPlan>(
        bool confirmed,
        TPlan plan,
        Action<TPlan, TextWriter> writePreview,
        Func<TPlan, CancellationToken, Task> execute,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        writePreview(plan, output);
        if (!confirmed)
        {
            error.WriteLine(
                "危险操作需要显式 --confirm 参数。");
            return 2;
        }

        await execute(plan, cancellationToken);
        return 0;
    }

    private static AgentBinding CreateHealthBinding(
        AgentBindingPlan plan)
    {
        return new AgentBinding(
            plan.AgentName,
            plan.ConfigurationDirectory,
            plan.Executable,
            plan.ManagedEntryPath,
            plan.RuntimeName,
            plan.RuntimeVersion,
            plan.WorkspacePath,
            plan.RuntimeCommand,
            plan.HealthArguments,
            plan.BindingFilePath,
            new AgentConfigurationRecoveryPoint(
                "health-check-preview",
                plan.AgentName,
                [],
                DateTimeOffset.UnixEpoch));
    }

    private static async Task<OperationRecord?> FindOperationAsync(
        ICliEnvironmentManager manager,
        string? operationId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(operationId))
        {
            return null;
        }

        var operations = await manager.ListOperationsAsync(cancellationToken);
        return operations
            .Where(operation => string.Equals(
                operation.Id,
                operationId,
                StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(operation => operation.UpdatedAtUtc)
            .FirstOrDefault();
    }
}
