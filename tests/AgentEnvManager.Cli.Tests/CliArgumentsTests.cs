using AgentEnvManager.Cli;

namespace AgentEnvManager.Cli.Tests;

public sealed class CliArgumentsTests
{
    [Fact]
    public void Parse_runtimes_lists_runtime_providers()
    {
        var request = CliArguments.Parse(["runtimes"]);

        Assert.Equal(CliAction.RuntimeList, request.Action);
    }

    [Fact]
    public void Parse_runtime_install_captures_mirror_and_confirmation()
    {
        var request = CliArguments.Parse(
            [
                "runtime-install",
                "python",
                "3.13.7",
                "--mirror",
                "https://mirror.test/python",
                "--confirm"
            ]);

        Assert.Equal(CliAction.RuntimeInstall, request.Action);
        Assert.Equal("python", request.ProviderId);
        Assert.Equal("3.13.7", request.Version);
        Assert.Equal("https://mirror.test/python", request.MirrorUrl);
        Assert.True(request.Confirmed);
    }
}
