using AgentEnvManager.Core.Agents;

namespace AgentEnvManager.Core.Tests.Agents;

public sealed class WindowsPathExecutableLocatorTests
{
    [Fact]
    public void FindExecutable_uses_path_and_pathext()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"agent-env-manager-locator-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var executable = Path.Combine(root, "codex.cmd");
            File.WriteAllText(executable, "@echo off\r\n");
            var variables = new Dictionary<string, string?>
            {
                ["PATH"] = root,
                ["PATHEXT"] = ".CMD;.EXE"
            };
            var locator = new WindowsPathExecutableLocator(
                name => variables.GetValueOrDefault(name));

            var found = locator.FindExecutable("codex");

            Assert.Equal(executable, found, ignoreCase: true);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
