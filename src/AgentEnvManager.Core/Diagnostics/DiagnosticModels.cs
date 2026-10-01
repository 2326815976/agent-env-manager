using AgentEnvManager.Core.Operations;

namespace AgentEnvManager.Core.Diagnostics;

public static class DiagnosticPolicy
{
    public const bool TelemetryEnabled = false;

    public const bool AllowsAutomaticUpload = false;
}

public sealed record DiagnosticPackageEntry(
    string Name,
    string Description,
    string Content);

public sealed record DiagnosticPackagePreview(
    IReadOnlyList<DiagnosticPackageEntry> Entries,
    string Impact,
    string PreviewHash,
    bool TelemetryEnabled,
    bool AllowsAutomaticUpload);

public sealed record DiagnosticPackageResult(
    string DestinationPath,
    int EntryCount,
    DateTimeOffset CreatedAtUtc,
    string OperationId);

public interface IDiagnosticPackageService
{
    Task<DiagnosticPackagePreview> PreviewAsync(
        CancellationToken cancellationToken = default);

    Task<DiagnosticPackageResult> ExportAsync(
        DiagnosticPackagePreview preview,
        string destinationPath,
        CancellationToken cancellationToken = default);

    Task<OperationRecord> RollbackAsync(
        string operationId,
        CancellationToken cancellationToken = default);
}
