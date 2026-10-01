using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AgentEnvManager.Core.Adoption;
using AgentEnvManager.Core.EnvironmentVariables;
using AgentEnvManager.Core.Operations;
using AgentEnvManager.Core.Storage;

namespace AgentEnvManager.Core.Diagnostics;

public sealed class DiagnosticPackageService(
    IEnvironmentManifestStore manifestStore,
    IOperationJournal operationJournal,
    IEnvironmentRecoveryPointStore recoveryPointStore,
    IEnvironmentVariableRecoveryPointStore environmentVariableRecoveryPointStore,
    ManagerPaths managerPaths,
    TimeProvider? timeProvider = null) : IDiagnosticPackageService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly TimeProvider _timeProvider =
        timeProvider ?? TimeProvider.System;

    public async Task<DiagnosticPackagePreview> PreviewAsync(
        CancellationToken cancellationToken = default)
    {
        var context = new DiagnosticSanitizationContext(
            Environment.GetFolderPath(
                Environment.SpecialFolder.UserProfile),
            managerPaths.StateRoot,
            managerPaths.DataRoot);
        var operations = await operationJournal.ReadAllAsync(
            cancellationToken);
        var recoveryPoints = await recoveryPointStore.ReadAllAsync(
            cancellationToken);
        var environmentVariablePoints =
            await environmentVariableRecoveryPointStore.ReadAllAsync(
                cancellationToken);
        var manifests = await manifestStore.ReadAllAsync(
            cancellationToken);
        var entries = new List<DiagnosticPackageEntry>
        {
            new(
                "summary.txt",
                "诊断包摘要和遥测状态。",
                CreateSummary(operations.Count, recoveryPoints.Count)),
            new(
                "operations.json",
                "脱敏后的操作与恢复日志。",
                JsonSerializer.Serialize(
                    operations.Select(operation =>
                        SanitizeOperation(operation, context)),
                    JsonOptions)),
            new(
                "recovery-points.json",
                "环境恢复点摘要。",
                JsonSerializer.Serialize(
                    recoveryPoints.Select(point =>
                        new
                        {
                            point.Id,
                            point.OperationId,
                            Fingerprint = DiagnosticSanitizer.SanitizeText(
                                point.Fingerprint.Value,
                                context),
                            Description = DiagnosticSanitizer.SanitizeText(
                                point.Description,
                                context),
                            point.CreatedAtUtc
                        }),
                    JsonOptions)),
            new(
                "environment-variable-recovery.json",
                "环境变量恢复点摘要，不包含变量名或值。",
                JsonSerializer.Serialize(
                    environmentVariablePoints.Select(point =>
                        new
                        {
                            point.Id,
                            point.CreatedAtUtc,
                            ValueCount = point.OriginalValues.Count
                        }),
                    JsonOptions)),
            new(
                "manifests.json",
                "已纳管环境摘要，不包含敏感配置内容。",
                JsonSerializer.Serialize(
                    manifests.Select(manifest =>
                        SanitizeManifest(manifest, context)),
                    JsonOptions))
        };
        return new DiagnosticPackagePreview(
            entries,
            "诊断包只导出脱敏后的操作、恢复点和环境摘要；默认不上传任何状态。",
            ComputePreviewHash(
                entries,
                "诊断包只导出脱敏后的操作、恢复点和环境摘要；默认不上传任何状态。",
                DiagnosticPolicy.TelemetryEnabled,
                DiagnosticPolicy.AllowsAutomaticUpload),
            DiagnosticPolicy.TelemetryEnabled,
            DiagnosticPolicy.AllowsAutomaticUpload);
    }

    public async Task<DiagnosticPackageResult> ExportAsync(
        DiagnosticPackagePreview preview,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(
            preview.PreviewHash,
            ComputePreviewHash(
                preview.Entries,
                preview.Impact,
                preview.TelemetryEnabled,
                preview.AllowsAutomaticUpload),
            StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "诊断包预览已被修改，请重新预览。");
        }

        var fullDestinationPath = Path.GetFullPath(destinationPath);
        DiagnosticDestinationPolicy.EnsureLocal(fullDestinationPath);
        var operation = OperationStateMachine.Create(
            OperationType.DiagnosticExport,
            "导出脱敏诊断包",
            _timeProvider,
            target: fullDestinationPath,
            impact: "生成仅限本地的脱敏 zip，不上传任何状态。",
            expectedResult: "诊断包包含操作和恢复信息且不含敏感内容。");
        await operationJournal.SaveAsync(operation, cancellationToken);
        operation = await SaveTransitionAsync(
            OperationStateMachine.MarkValidated(operation, _timeProvider),
            cancellationToken);
        var recoveryPointId = $"diagnostic-export-{operation.Id}";
        operation = await SaveTransitionAsync(
            OperationStateMachine.MarkRecoveryReady(
                operation,
                recoveryPointId,
                _timeProvider),
            cancellationToken);
        var exportCreated = false;
        try
        {
            operation = await SaveTransitionAsync(
                OperationStateMachine.BeginExecution(
                    operation,
                    _timeProvider),
                cancellationToken);
            var destinationDirectory = Path.GetDirectoryName(
                fullDestinationPath);
            if (!string.IsNullOrWhiteSpace(destinationDirectory))
            {
                Directory.CreateDirectory(destinationDirectory);
            }

            await using (var destination = new FileStream(
                fullDestinationPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None))
            {
                exportCreated = true;
                using var archive = new ZipArchive(
                    destination,
                    ZipArchiveMode.Create,
                    leaveOpen: true);
                foreach (var entry in preview.Entries)
                {
                    var zipEntry = archive.CreateEntry(entry.Name);
                    await using var writer = new StreamWriter(
                        zipEntry.Open(),
                        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                    await writer.WriteAsync(
                        entry.Content.AsMemory(),
                        cancellationToken);
                }
            }

            operation = await SaveTransitionAsync(
                OperationStateMachine.BeginVerification(
                    operation,
                    _timeProvider),
                cancellationToken);
            if (!File.Exists(fullDestinationPath)
                || new FileInfo(fullDestinationPath).Length == 0)
            {
                throw new InvalidOperationException(
                    "诊断包导出验证失败。");
            }

            operation = OperationStateMachine.Complete(
                operation,
                _timeProvider);
            await operationJournal.SaveAsync(
                operation,
                cancellationToken);
            return new DiagnosticPackageResult(
                fullDestinationPath,
                preview.Entries.Count,
                _timeProvider.GetUtcNow(),
                operation.Id);
        }
        catch (Exception exception)
        {
            if (exportCreated && File.Exists(fullDestinationPath))
            {
                File.Delete(fullDestinationPath);
            }

            operation = OperationStateMachine.Fail(
                operation,
                exception.Message,
                _timeProvider);
            await operationJournal.SaveAsync(
                operation,
                CancellationToken.None);
            operation = OperationStateMachine.Rollback(
                operation,
                _timeProvider);
            await operationJournal.SaveAsync(
                operation,
                CancellationToken.None);
            throw;
        }
    }

    public async Task<OperationRecord> RollbackAsync(
        string operationId,
        CancellationToken cancellationToken = default)
    {
        var operation = await operationJournal.GetAsync(
            operationId,
            cancellationToken)
            ?? throw new KeyNotFoundException("未找到操作记录。");
        if (operation.Type != OperationType.DiagnosticExport)
        {
            throw new InvalidOperationException(
                "该操作不是诊断包导出操作。");
        }

        if (operation.State is OperationState.Succeeded
            or OperationState.RolledBack)
        {
            operation = OperationStateMachine.Reject(
                operation,
                OperationState.RolledBack,
                "该操作当前状态不能回滚。",
                _timeProvider);
            await operationJournal.SaveAsync(
                operation,
                CancellationToken.None);
            throw new InvalidOperationException(
                "该操作当前状态不能回滚。");
        }

        if (!string.IsNullOrWhiteSpace(operation.Target)
            && File.Exists(operation.Target))
        {
            File.Delete(operation.Target);
        }

        if (operation.State != OperationState.Failed)
        {
            operation = OperationStateMachine.Fail(
                operation,
                "用户请求回滚诊断包导出。",
                _timeProvider);
            await operationJournal.SaveAsync(
                operation,
                cancellationToken);
        }

        operation = OperationStateMachine.Rollback(
            operation,
            _timeProvider);
        await operationJournal.SaveAsync(
            operation,
            cancellationToken);
        return operation;
    }

    private string CreateSummary(
        int operationCount,
        int recoveryPointCount)
    {
        return string.Join(
            Environment.NewLine,
            $"生成时间: {_timeProvider.GetUtcNow():O}",
            $"操作记录数: {operationCount}",
            $"恢复点数量: {recoveryPointCount}",
            $"遥测: {(DiagnosticPolicy.TelemetryEnabled ? "启用" : "关闭")}",
            $"自动上传: {(DiagnosticPolicy.AllowsAutomaticUpload ? "允许" : "禁止")}",
            "敏感配置、凭据和 SSH 私钥: 不包含",
            string.Empty);
    }

    private static object SanitizeOperation(
        OperationRecord operation,
        DiagnosticSanitizationContext context)
    {
        return new
        {
            operation.Id,
            Type = operation.Type.ToString(),
            State = operation.State.ToString(),
            operation.CreatedAtUtc,
            operation.UpdatedAtUtc,
            Summary = DiagnosticSanitizer.SanitizeText(
                operation.Summary,
                context),
            operation.RecoveryPointId,
            FailureReason = DiagnosticSanitizer.SanitizeText(
                operation.FailureReason,
                context),
            Target = DiagnosticSanitizer.SanitizePath(
                operation.Target,
                context),
            Impact = DiagnosticSanitizer.SanitizeText(
                operation.Impact,
                context),
            PreviousTarget = DiagnosticSanitizer.SanitizePath(
                operation.PreviousTarget,
                context),
            ExpectedResult = DiagnosticSanitizer.SanitizeText(
                operation.ExpectedResult,
                context),
            operation.TargetIdentity,
            SourceTarget = DiagnosticSanitizer.SanitizePath(
                operation.SourceTarget,
                context),
            StableActivationPath = DiagnosticSanitizer.SanitizePath(
                operation.StableActivationPath,
                context),
            operation.MigrationStrategy,
            operation.ArtifactSource,
            ArtifactCachePath = DiagnosticSanitizer.SanitizePath(
                operation.ArtifactCachePath,
                context),
            operation.ArtifactSha256,
            MirrorUrl = DiagnosticSanitizer.SanitizeText(
                operation.MirrorUrl,
                context),
            VerificationResult = DiagnosticSanitizer.SanitizeText(
                operation.VerificationResult,
                context)
        };
    }

    private static object SanitizeManifest(
        EnvironmentManifest manifest,
        DiagnosticSanitizationContext context)
    {
        return new
        {
            Identity = manifest.Identity.Value,
            Fingerprint = manifest.Fingerprint.Value,
            Kind = manifest.Kind.ToString(),
            manifest.Name,
            manifest.Version,
            Source = manifest.Source.Kind.ToString(),
            manifest.IsSystemComponent,
            manifest.AdoptedAtUtc,
            Location = DiagnosticSanitizer.SanitizePath(
                manifest.Location,
                context),
            StableActivationPath = DiagnosticSanitizer.SanitizePath(
                manifest.StableActivationPath,
                context),
            ManagedEntryPath = DiagnosticSanitizer.SanitizePath(
                manifest.ManagedEntryPath,
                context),
            manifest.AssetHash
        };
    }

    private async Task<OperationRecord> SaveTransitionAsync(
        OperationRecord operation,
        CancellationToken cancellationToken)
    {
        await operationJournal.SaveAsync(operation, cancellationToken);
        return operation;
    }

    private static string ComputePreviewHash(
        IReadOnlyList<DiagnosticPackageEntry> entries,
        string impact,
        bool telemetryEnabled,
        bool allowsAutomaticUpload)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var entry in entries)
        {
            hash.AppendData(Encoding.UTF8.GetBytes(entry.Name));
            hash.AppendData(Encoding.UTF8.GetBytes(entry.Content));
        }

        hash.AppendData(Encoding.UTF8.GetBytes(impact));
        hash.AppendData(Encoding.UTF8.GetBytes(
            telemetryEnabled.ToString()));
        hash.AppendData(Encoding.UTF8.GetBytes(
            allowsAutomaticUpload.ToString()));
        return Convert.ToHexString(hash.GetHashAndReset())
            .ToLowerInvariant();
    }
}
