using System.IO.Compression;
using AgentEnvManager.Core.Adoption;
using AgentEnvManager.Core.Diagnostics;
using AgentEnvManager.Core.Inspection;
using AgentEnvManager.Core.Operations;
using AgentEnvManager.Core.Storage;
using AgentEnvManager.Core.Tests.TestSupport;

namespace AgentEnvManager.Core.Tests.Diagnostics;

public sealed class DiagnosticPackageTests
{
    [Fact]
    public async Task Preview_and_export_are_sanitized_local_and_audited()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"agent-env-manager-diagnostic-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var userProfile = Environment.GetFolderPath(
                Environment.SpecialFolder.UserProfile);
            var sensitivePath = Path.Combine(
                userProfile,
                ".ssh",
                "id_rsa");
            var manifestStore = new InMemoryManifestStore();
            var journal = new RecordingOperationJournal();
            var recoveryStore = new RecordingRecoveryPointStore();
            var environmentVariableRecoveryStore =
                new RecordingEnvironmentVariableRecoveryPointStore();
            await journal.SaveAsync(new OperationRecord(
                "operation-1",
                OperationType.Switch,
                OperationState.Failed,
                DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch,
                $"切换到 {sensitivePath}；项目 D:\\Private\\Project\\secret.txt",
                "recovery-1",
                "Authorization: Bearer token-value",
                sensitivePath,
                "token=secret-value"));
            await recoveryStore.CreateAsync(
                new AdoptionPreview(
                    new EnvironmentFingerprint("fingerprint-1"),
                    new EnvironmentIdentity("identity-1"),
                    new EnvironmentAsset(
                        EnvironmentAssetKind.ToolRuntime,
                        "Git",
                        "2.56.0",
                        sensitivePath,
                        IsSystemComponent: false,
                        Source: DiscoverySourceInfo.RuntimeProvider),
                    "hash",
                    @"C:\activation",
                    $"恢复 {sensitivePath}",
                    IsAlreadyManaged: true,
                    ExistingIdentity: new EnvironmentIdentity("identity-1")),
                "operation-1",
                existingManifest: null);
            await environmentVariableRecoveryStore.CreateAsync(
                "operation-1",
                new Dictionary<string, string?>
                {
                    ["SECRET_TOKEN"] = "secret-value"
                });
            await manifestStore.SaveAsync(new EnvironmentManifest(
                new EnvironmentIdentity("identity-1"),
                new EnvironmentFingerprint("fingerprint-1"),
                EnvironmentAssetKind.ToolRuntime,
                "Git",
                "2.56.0",
                DiscoverySourceInfo.RuntimeProvider,
                sensitivePath,
                @"C:\activation",
                "hash",
                "recovery-1",
                "operation-1",
                IsSystemComponent: false,
                DateTimeOffset.UnixEpoch,
                "git-activation",
                @"C:\shims\git"));
            var manager = new EnvironmentManager(
                new StubEnvironmentProbe(
                    new EnvironmentProbeResult([], [])),
                manifestStore: manifestStore,
                recoveryPointStore: recoveryStore,
                operationJournal: journal,
                environmentVariableRecoveryPointStore:
                    environmentVariableRecoveryStore,
                managerPaths: ManagerPaths.Resolve(root));

            var preview = await manager.PreviewDiagnosticPackageAsync();

            Assert.Contains(
                preview.Entries,
                entry => entry.Name == "summary.txt");
            Assert.Contains(
                preview.Entries,
                entry => entry.Name == "operations.json");
            Assert.Contains(
                preview.Entries,
                entry => entry.Name == "recovery-points.json");
            Assert.False(preview.TelemetryEnabled);
            Assert.False(preview.AllowsAutomaticUpload);
            var previewText = string.Join(
                "\n",
                preview.Entries.Select(entry => entry.Content));
            Assert.Contains("operation-1", previewText);
            Assert.Contains("recovery-1", previewText);
            Assert.DoesNotContain(
                userProfile,
                previewText,
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(
                ".ssh",
                previewText,
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(
                "id_rsa",
                previewText,
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(
                "token-value",
                previewText,
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(
                "secret-value",
                previewText,
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(
                "Private",
                previewText,
                StringComparison.OrdinalIgnoreCase);

            var destinationPath = Path.Combine(root, "diagnostics.zip");
            var result = await manager.ExportDiagnosticPackageAsync(
                preview,
                destinationPath);

            Assert.Equal(destinationPath, result.DestinationPath);
            Assert.True(File.Exists(destinationPath));
            using (var archive = ZipFile.OpenRead(destinationPath))
            {
                Assert.Contains(
                    archive.Entries,
                    entry => entry.FullName == "operations.json");
                Assert.Contains(
                    archive.Entries,
                    entry => entry.FullName == "summary.txt");
            }

            var exportOperation = Assert.Single(
                journal.History
                    .Where(record => record.Id == result.OperationId)
                    .GroupBy(record => record.Id)
                    .Select(group => group.Last()));
            Assert.Equal(
                OperationType.DiagnosticExport,
                exportOperation.Type);
            Assert.Equal(
                OperationState.Succeeded,
                exportOperation.State);

            var metadataTampered = preview with
            {
                TelemetryEnabled = true
            };
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => manager.ExportDiagnosticPackageAsync(
                    metadataTampered,
                    Path.Combine(root, "metadata-tampered.zip")));

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => manager.ExportDiagnosticPackageAsync(
                    preview,
                    @"\\server\share\diagnostics.zip"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Export_rejects_tampered_preview()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"agent-env-manager-diagnostic-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var manager = new EnvironmentManager(
                new StubEnvironmentProbe(
                    new EnvironmentProbeResult([], [])),
                managerPaths: ManagerPaths.Resolve(root));
            var preview = await manager.PreviewDiagnosticPackageAsync();
            var tampered = preview with
            {
                Entries =
                [
                    new DiagnosticPackageEntry(
                        "tampered.txt",
                        "tampered",
                        "tampered")
                ]
            };

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => manager.ExportDiagnosticPackageAsync(
                    tampered,
                    Path.Combine(root, "tampered.zip")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class StubEnvironmentProbe(EnvironmentProbeResult result)
        : IEnvironmentProbe
    {
        public Task<EnvironmentProbeResult> ProbeAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(result);
        }
    }
}
