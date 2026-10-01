using AgentEnvManager.Core.Adoption;
using AgentEnvManager.Core.EnvironmentVariables;
using AgentEnvManager.Core.Inspection;
using AgentEnvManager.Core.Scanning;
using AgentEnvManager.Core.Storage;

namespace AgentEnvManager.Cli;

public static class CliApplication
{
    public static async Task<int> RunAsync(
        string[] args,
        CancellationToken cancellationToken = default)
    {
        var request = CliArguments.Parse(args);

        if (request.Action == CliAction.Help)
        {
            CliMessages.WriteUsage(Console.Out);
            return 0;
        }

        if (request.Action == CliAction.Unknown)
        {
            Console.Error.WriteLine($"未知命令: {request.UnknownCommand}");
            CliMessages.WriteUsage(Console.Error);
            return 2;
        }

        var manager = CreateEnvironmentManager();

        switch (request.Action)
        {
            case CliAction.Inspect:
                var report = await manager.InspectAsync(cancellationToken);
                InspectionReportRenderer.Write(report, Console.Out);
                return 0;

            case CliAction.PreviewAdoption:
                var preview = await manager.PreviewAdoptionAsync(
                    new EnvironmentFingerprint(request.Fingerprint!),
                    cancellationToken);
                AdoptionReportRenderer.Write(preview, Console.Out);
                return 0;

            case CliAction.Adopt:
                var fingerprint = new EnvironmentFingerprint(
                    request.Fingerprint!);
                var adoptionPreview = await manager.PreviewAdoptionAsync(
                    fingerprint,
                    cancellationToken);
                AdoptionReportRenderer.Write(adoptionPreview, Console.Out);
                var managed = await manager.AdoptAsync(
                    adoptionPreview,
                    cancellationToken);
                AdoptionReportRenderer.Write(managed, Console.Out);
                return 0;

            case CliAction.RebuildIndex:
                var count = await manager.RebuildEnvironmentIndexAsync(
                    cancellationToken);
                CliMessages.WriteIndexRebuild(count, Console.Out);
                return 0;

            case CliAction.Rollback:
                var rolledBack = await manager.RollbackOperationAsync(
                    request.OperationId!,
                    cancellationToken);
                CliMessages.WriteRollback(rolledBack, Console.Out);
                return 0;

            case CliAction.DiagnosticsPreview:
                var diagnosticPreview =
                    await manager.PreviewDiagnosticPackageAsync(
                        cancellationToken);
                CliMessages.WriteDiagnosticPreview(
                    diagnosticPreview,
                    Console.Out);
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
                    Console.Out);
                return 0;

            default:
                throw new InvalidOperationException("未知 CLI 操作。");
        }
    }

    private static EnvironmentManager CreateEnvironmentManager()
    {
        return EnvironmentManagerFactory.CreateDefault();
    }
}
