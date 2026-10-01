using AgentEnvManager.Core.Adoption;
using AgentEnvManager.Core.Activation;
using AgentEnvManager.Core.EnvironmentVariables;
using AgentEnvManager.Core.Inspection;
using AgentEnvManager.Core.Operations;
using AgentEnvManager.Core.Diagnostics;
using AgentEnvManager.Core.Runtimes;
using AgentEnvManager.Wpf;

namespace AgentEnvManager.Wpf.Tests;

public sealed class MainViewModelTests
{
    [Fact]
    public async Task ScanAsync_populates_rows_with_domain_labels()
    {
        var report = new InspectionReport(
            DateTimeOffset.UnixEpoch,
            [
                new ObservedEnvironment(
                    new EnvironmentAsset(
                        EnvironmentAssetKind.ToolRuntime,
                        "Node.js",
                        "24.1.0",
                        @"D:\Runtimes\node",
                        IsSystemComponent: false,
                        Source: DiscoverySourceInfo.PathCommand),
                    new EnvironmentFingerprint("node-24"),
                    ManagementState.Observed,
                    HealthState.Unknown)
            ],
            [],
            []);
        var viewModel = new MainViewModel(
            new StubManagerClient(report));

        await viewModel.ScanAsync();

        var row = Assert.Single(viewModel.Environments);
        Assert.Equal("工具运行时", row.TypeLabel);
        Assert.Equal("仅观测", row.ManagementStateLabel);
        Assert.Equal("24.1.0", row.Version);
        Assert.Equal(@"D:\Runtimes\node", row.Location);
        Assert.Equal("未知", row.HealthStateLabel);
    }

    [Fact]
    public async Task ScanAsync_shows_path_and_command_conflicts()
    {
        var report = new InspectionReport(
            DateTimeOffset.UnixEpoch,
            [],
            [
                new PathConflict(
                    @"C:\Tools",
                    2,
                    [PathScope.User, PathScope.Machine])
            ],
            [
                new CommandPathConflict(
                    "node",
                    [
                        new CommandPathCandidate(
                            @"C:\Tools\node.exe",
                            0,
                            Effective: true,
                            [PathScope.User])
                    ])
            ]);
        var viewModel = new MainViewModel(new StubManagerClient(report));

        await viewModel.ScanAsync();

        Assert.Equal("C:\\Tools（2 次）", Assert.Single(
            viewModel.PathConflicts).Label);
        Assert.Equal("node", Assert.Single(
            viewModel.CommandPathConflicts).Name);
        Assert.Contains(
            "顺序 1",
            Assert.Single(viewModel.CommandPathConflicts).Summary);
        Assert.Equal(DateTimeOffset.UnixEpoch, viewModel.ReportGeneratedAt);
    }

    [Theory]
    [InlineData(EnvironmentAssetKind.Shell, "Shell")]
    [InlineData(EnvironmentAssetKind.AgentConfiguration, "Agent 配置环境")]
    [InlineData(EnvironmentAssetKind.ToolRuntime, "工具运行时")]
    public void FormatKind_uses_domain_terms(
        EnvironmentAssetKind kind,
        string expected)
    {
        Assert.Equal(expected, EnvironmentLabelFormatter.FormatKind(kind));
    }

    [Fact]
    public async Task AdoptAsync_uses_reviewed_preview_and_rescans()
    {
        var observed = new ObservedEnvironment(
            new EnvironmentAsset(
                EnvironmentAssetKind.ToolRuntime,
                "Node.js",
                "24.1.0",
                @"D:\Runtimes\node",
                IsSystemComponent: false,
                Source: DiscoverySourceInfo.PathCommand),
            new EnvironmentFingerprint("node-24"),
            ManagementState.Observed,
            HealthState.Unknown);
        var managedReport = new InspectionReport(
            DateTimeOffset.UnixEpoch,
            [
                observed with
                {
                    ManagementState = ManagementState.Managed,
                    Identity = new EnvironmentIdentity("node-24")
                }
            ],
            [],
            []);
        var client = new AdoptingManagerClient(observed, managedReport);
        var viewModel = new MainViewModel(client);
        await viewModel.ScanAsync();
        viewModel.SelectedEnvironment = Assert.Single(viewModel.Environments);

        await viewModel.PreviewAdoptionAsync();

        Assert.Equal(@"D:\Runtimes\node", viewModel.PendingTarget);
        Assert.Contains("恢复点", viewModel.PendingRecoveryPoint);

        await viewModel.AdoptAsync();

        Assert.Equal(
            "已纳管",
            Assert.Single(viewModel.Environments).ManagementStateLabel);
        Assert.NotNull(client.LastPreview);
        Assert.Equal("node-24", client.LastPreview.Fingerprint.Value);
    }

    [Fact]
    public async Task RuntimeAndDiagnosticsCenters_are_wired_to_client()
    {
        var client = new RuntimeAndDiagnosticsClient();
        var viewModel = new MainViewModel(client);

        await viewModel.RuntimeCenter.LoadProvidersAsync();
        await viewModel.DiagnosticsPackage.PreviewAsync();

        Assert.Equal(
            "Python",
            Assert.Single(viewModel.RuntimeCenter.Providers).Name);
        Assert.Equal(
            "summary.txt",
            Assert.Single(viewModel.DiagnosticsPackage.Entries).Name);
    }

    private sealed class StubManagerClient(InspectionReport report)
        : StubEnvironmentManagerClient
    {
        public override Task<InspectionReport> InspectAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(report);
        }

        public override Task<AdoptionPreview> PreviewAdoptionAsync(
            EnvironmentFingerprint fingerprint,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public override Task<ManagedEnvironment> AdoptAsync(
            AdoptionPreview preview,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public override Task<VersionSwitchPreview> PreviewVersionSwitchAsync(
            EnvironmentFingerprint fingerprint,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public override Task<OperationRecord> SwitchVersionAsync(
            VersionSwitchPreview preview,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public override Task<IReadOnlyList<OperationRecord>> ListOperationsAsync(
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public override Task<IReadOnlyList<AdoptionRecoveryPoint>>
            ListEnvironmentRecoveryPointsAsync(
                CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public override Task<IReadOnlyList<EnvironmentVariableRecoveryPoint>>
            ListEnvironmentVariableRecoveryPointsAsync(
                CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public override Task<OperationRecord> RollbackOperationAsync(
            string operationId,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public override Task<OperationRollbackPlan> PreviewRollbackAsync(
            string operationId,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class AdoptingManagerClient(
        ObservedEnvironment observed,
        InspectionReport managedReport)
        : StubEnvironmentManagerClient
    {
        private int _inspectCount;

        public AdoptionPreview? LastPreview { get; private set; }

        public override Task<InspectionReport> InspectAsync(
            CancellationToken cancellationToken = default)
        {
            _inspectCount++;
            return Task.FromResult(_inspectCount == 1
                ? new InspectionReport(
                    DateTimeOffset.UnixEpoch,
                    [observed],
                    [],
                    [])
                : managedReport);
        }

        public override Task<AdoptionPreview> PreviewAdoptionAsync(
            EnvironmentFingerprint fingerprint,
            CancellationToken cancellationToken = default)
        {
            LastPreview = new AdoptionPreview(
                fingerprint,
                new EnvironmentIdentity("node-24"),
                observed.Asset,
                "asset-hash",
                @"C:\Activations\node\current",
                "纳管不会移动原始文件。",
                IsAlreadyManaged: false,
                ExistingIdentity: null,
                OperationId: "operation-1",
                RecoveryPointId: "recovery-1");
            return Task.FromResult(LastPreview);
        }

        public override Task<ManagedEnvironment> AdoptAsync(
            AdoptionPreview preview,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ManagedEnvironment(
                preview.ProposedIdentity,
                new EnvironmentManifest(
                    preview.ProposedIdentity,
                    preview.Fingerprint,
                    preview.Asset.Kind,
                    preview.Asset.Name,
                    preview.Asset.Version,
                    preview.Asset.Source,
                    preview.Asset.Location,
                    preview.StableActivationPath,
                    preview.AssetHash,
                    "recovery",
                    "operation",
                    preview.Asset.IsSystemComponent,
                    DateTimeOffset.UnixEpoch,
                    "activation-identity",
                    @"C:\shims\node")));
        }

        public override Task<VersionSwitchPreview> PreviewVersionSwitchAsync(
            EnvironmentFingerprint fingerprint,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public override Task<OperationRecord> SwitchVersionAsync(
            VersionSwitchPreview preview,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public override Task<IReadOnlyList<OperationRecord>> ListOperationsAsync(
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public override Task<IReadOnlyList<AdoptionRecoveryPoint>>
            ListEnvironmentRecoveryPointsAsync(
                CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public override Task<IReadOnlyList<EnvironmentVariableRecoveryPoint>>
            ListEnvironmentVariableRecoveryPointsAsync(
                CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public override Task<OperationRecord> RollbackOperationAsync(
            string operationId,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public override Task<OperationRollbackPlan> PreviewRollbackAsync(
            string operationId,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class RuntimeAndDiagnosticsClient
        : StubEnvironmentManagerClient
    {
        public override IReadOnlyList<RuntimeProviderDescriptor>
            DescribeRuntimeProviders()
        {
            return [new PythonRuntimeProvider().Descriptor];
        }

        public override Task<DiagnosticPackagePreview>
            PreviewDiagnosticPackageAsync(
                CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new DiagnosticPackagePreview(
                [
                    new DiagnosticPackageEntry(
                        "summary.txt",
                        "诊断摘要。",
                        "脱敏诊断摘要。")
                ],
                "只导出脱敏内容。",
                "preview-hash",
                TelemetryEnabled: false,
                AllowsAutomaticUpload: false));
        }
    }
}
