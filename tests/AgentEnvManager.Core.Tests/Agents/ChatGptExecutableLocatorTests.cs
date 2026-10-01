using AgentEnvManager.Core.Agents;

namespace AgentEnvManager.Core.Tests.Agents;

public sealed class ChatGptExecutableLocatorTests
{
    [Fact]
    public void FindExecutable_prefers_environment_override()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"agent-env-manager-chatgpt-locator-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var executable = Path.Combine(root, "ChatGPT.exe");
            File.WriteAllText(executable, string.Empty);
            var variables = new Dictionary<string, string?>
            {
                ["CHATGPT_EXECUTABLE"] = executable
            };
            var locator = new ChatGptExecutableLocator(
                name => variables.GetValueOrDefault(name),
                _ => null);

            var found = locator.FindExecutable("ChatGPT");

            Assert.Equal(executable, found);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
