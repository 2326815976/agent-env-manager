using AgentEnvManager.Core.Storage;

namespace AgentEnvManager.Core.Tests.Storage;

public sealed class EnvironmentManagerFactoryTests
{
    [Fact]
    public void Create_registers_chatgpt_as_the_only_agent_adapter()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"aem-factory-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var manager = EnvironmentManagerFactory.Create(
                Path.Combine(root, "state"),
                Path.Combine(root, "data"));

            // Codex 是 ChatGPT 的内置组件，不作为独立 Agent 目标。
            Assert.Equal(["ChatGPT"], manager.DescribeAgentAdapters());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
