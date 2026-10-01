using AgentEnvManager.Core.Inspection;
using AgentEnvManager.Core.Runtimes;
using AgentEnvManager.Core.Storage;
using AgentEnvManager.Core.Tests.TestSupport;

namespace AgentEnvManager.Core.Tests.Runtimes;

public sealed class CondaEnvironmentTests
{
    [Fact]
    public async Task InspectCondaEnvironmentsAsync_returns_observed_environments()
    {
        var runner = new RecordingRuntimeCommandRunner(invocation =>
        {
            Assert.Equal("conda", invocation.Executable);
            Assert.Equal(
                ["env", "list", "--json"],
                invocation.Arguments);
            return new RuntimeCommandResult(
                0,
                """
                {
                  "active_prefix": null,
                  "conda_prefix": "D:\\Anaconda3",
                  "envs": [
                    "D:\\Anaconda3",
                    "D:\\Anaconda3\\envs\\myenv"
                  ]
                }
                """,
                string.Empty);
        });
        var manager = CreateManager(runner);

        var environments = await manager.InspectCondaEnvironmentsAsync();

        Assert.Collection(
            environments,
            environment =>
            {
                Assert.Equal("base", environment.Name);
                Assert.Equal(@"D:\Anaconda3", environment.Prefix);
                Assert.True(environment.IsBase);
                Assert.False(environment.IsActive);
                Assert.Equal(
                    ManagementState.Observed,
                    environment.ManagementState);
            },
            environment =>
            {
                Assert.Equal("myenv", environment.Name);
                Assert.Equal(
                    @"D:\Anaconda3\envs\myenv",
                    environment.Prefix);
                Assert.False(environment.IsBase);
                Assert.Equal(
                    ManagementState.Observed,
                    environment.ManagementState);
            });
    }

    [Fact]
    public async Task ExportCondaEnvironmentAsync_exports_definition_and_rebuild_plan()
    {
        var root = CreateTempRoot();
        try
        {
            var definitionPath = Path.Combine(
                root,
                "conda",
                "myenv.yml");
            var targetPrefix = Path.Combine(root, "target", "myenv");
            var runner = new RecordingRuntimeCommandRunner(invocation =>
            {
                Assert.Equal("conda", invocation.Executable);
                Assert.Equal("env", invocation.Arguments[0]);
                Assert.Equal("export", invocation.Arguments[1]);
                Assert.Contains(
                    @"D:\Anaconda3\envs\myenv",
                    invocation.Arguments);
                Assert.Contains(definitionPath, invocation.Arguments);
                Assert.Contains("--no-builds", invocation.Arguments);
                Directory.CreateDirectory(
                    Path.GetDirectoryName(definitionPath)!);
                File.WriteAllText(
                    definitionPath,
                    "name: myenv\nchannels: []\ndependencies: []\n");
                return new RuntimeCommandResult(0, string.Empty, string.Empty);
            });
            var manager = CreateManager(runner);

            var plan = await manager.ExportCondaEnvironmentAsync(
                @"D:\Anaconda3\envs\myenv",
                definitionPath,
                targetPrefix);

            Assert.Equal(
                @"D:\Anaconda3\envs\myenv",
                plan.SourcePrefix);
            Assert.Equal(definitionPath, plan.DefinitionPath);
            Assert.Equal(targetPrefix, plan.TargetPrefix);
            Assert.Contains(
                "conda env create",
                plan.CommandLine,
                StringComparison.Ordinal);
            Assert.Contains(
                definitionPath,
                plan.CommandLine,
                StringComparison.Ordinal);
            Assert.Contains(
                targetPrefix,
                plan.CommandLine,
                StringComparison.Ordinal);
            Assert.Contains(
                "不移动",
                plan.Impact,
                StringComparison.Ordinal);
            Assert.True(File.Exists(definitionPath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static EnvironmentManager CreateManager(
        RecordingRuntimeCommandRunner runner)
    {
        return new EnvironmentManager(
            new StubEnvironmentProbe(
                new EnvironmentProbeResult([], [])),
            managerPaths: ManagerPaths.Resolve(
                Path.Combine(
                    Path.GetTempPath(),
                    $"agent-env-manager-conda-tests-{Guid.NewGuid():N}")),
            runtimeCommandRunner: runner);
    }

    private static string CreateTempRoot()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"agent-env-manager-conda-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
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
