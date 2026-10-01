using AgentEnvManager.Core.Storage;

namespace AgentEnvManager.Core.Tests.Storage;

public sealed class ManagerPathsTests
{
    [Fact]
    public void Resolve_separates_state_root_and_runtime_data_root()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"agent-env-manager-path-tests-{Guid.NewGuid():N}");
        var stateRoot = Path.Combine(root, "state");
        var dataRoot = Path.Combine(root, "data");

        var paths = ManagerPaths.Resolve(stateRoot, dataRoot);

        Assert.Equal(Path.GetFullPath(stateRoot), paths.StateRoot);
        Assert.Equal(Path.GetFullPath(dataRoot), paths.DataRoot);
        Assert.Equal(
            Path.Combine(Path.GetFullPath(dataRoot), "runtimes"),
            paths.RuntimeDirectory);
        Assert.StartsWith(
            Path.GetFullPath(stateRoot),
            paths.ManifestDirectory,
            StringComparison.OrdinalIgnoreCase);
    }
}
