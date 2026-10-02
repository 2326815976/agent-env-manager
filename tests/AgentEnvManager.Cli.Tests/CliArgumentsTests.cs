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
    public void Parse_cc_switch_captures_config_root_and_codex_home()
    {
        var inspect = CliArguments.Parse(
            ["cc-switch", "--config-root", @"D:\Software\CCSwitch"]);
        var bind = CliArguments.Parse(
            [
                "cc-switch-bind",
                "--config-root",
                @"E:\Codex\.cc-switch",
                "--codex-home",
                @"E:\Codex\.codex",
                "--confirm"
            ]);

        Assert.Equal(CliAction.CcSwitchInspect, inspect.Action);
        Assert.Equal(@"D:\Software\CCSwitch", inspect.ConfigRoot);
        Assert.Equal(CliAction.CcSwitchBind, bind.Action);
        Assert.Equal(@"E:\Codex\.cc-switch", bind.ConfigRoot);
        Assert.Equal(@"E:\Codex\.codex", bind.CodexHome);
        Assert.True(bind.Confirmed);
    }

    [Fact]
    public void Parse_cc_switch_bind_requires_both_paths()
    {
        var request = CliArguments.Parse(
            ["cc-switch-bind", "--config-root", @"E:\Codex\.cc-switch"]);

        Assert.Equal(CliAction.Invalid, request.Action);
        Assert.Contains("--codex-home", request.Error!);
    }

    [Fact]
    public void Parse_coordinated_migrate_captures_paths_and_confirmation()
    {
        var request = CliArguments.Parse(
            [
                "coordinated-migrate",
                "--codex-source",
                @"E:\Codex\.codex",
                "--codex-dest",
                @"E:\Moved\.codex",
                "--cc-switch-source",
                @"E:\Codex\.cc-switch",
                "--cc-switch-dest",
                @"E:\Moved\.cc-switch",
                "--confirm"
            ]);

        Assert.Equal(CliAction.CoordinatedMigrationApply, request.Action);
        Assert.Equal(@"E:\Codex\.codex", request.CodexSourcePath);
        Assert.Equal(@"E:\Moved\.codex", request.CodexDestinationPath);
        Assert.Equal(@"E:\Codex\.cc-switch", request.CcSwitchSourcePath);
        Assert.Equal(
            @"E:\Moved\.cc-switch",
            request.CcSwitchDestinationPath);
        Assert.True(request.Confirmed);
    }

    [Fact]
    public void Parse_coordinated_migrate_preview_requires_all_paths()
    {
        var request = CliArguments.Parse(
            [
                "coordinated-migrate-preview",
                "--codex-source",
                @"E:\Codex\.codex"
            ]);

        Assert.Equal(CliAction.Invalid, request.Action);
        Assert.Contains("--codex-dest", request.Error!);
    }

    [Fact]
    public void Parse_env_vars_lists_environment_variables()
    {
        var request = CliArguments.Parse(["env-vars"]);

        Assert.Equal(CliAction.EnvironmentVariablesInspect, request.Action);
        Assert.False(request.ShowSecrets);
    }

    [Fact]
    public void Parse_env_vars_show_secrets_flag()
    {
        var request = CliArguments.Parse(["env-vars", "--show-secrets"]);

        Assert.Equal(CliAction.EnvironmentVariablesInspect, request.Action);
        Assert.True(request.ShowSecrets);
    }

    [Fact]
    public void Parse_env_vars_rejects_unknown_option()
    {
        var request = CliArguments.Parse(["env-vars", "--show-secret"]);

        Assert.Equal(CliAction.Invalid, request.Action);
        Assert.Contains("--show-secret", request.Error!);
    }

    [Fact]
    public void Parse_env_vars_apply_captures_managed_variable_and_confirmation()
    {
        var request = CliArguments.Parse(
            [
                "env-vars-apply",
                "--set",
                "AGENT_ENV_MANAGER_MODE=project",
                "--managed-path",
                @"C:\shims\node",
                "--confirm"
            ]);

        Assert.Equal(CliAction.EnvironmentVariablesApply, request.Action);
        Assert.Equal("AGENT_ENV_MANAGER_MODE", request.VariableName);
        Assert.Equal("project", request.VariableValue);
        Assert.Equal(@"C:\shims\node", request.ManagedPathEntry);
        Assert.True(request.Confirmed);
    }

    [Fact]
    public void Parse_env_vars_apply_rejects_assignment_without_value()
    {
        var request = CliArguments.Parse(
            ["env-vars-apply", "--set", "AGENT_ENV_MANAGER_MODE"]);

        Assert.Equal(CliAction.Invalid, request.Action);
        Assert.Contains("NAME=VALUE", request.Error!);
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

    [Fact]
    public void Parse_runtime_install_captures_install_directory()
    {
        var request = CliArguments.Parse(
            [
                "runtime-install",
                "python",
                "3.13.7",
                "--install-dir",
                @"D:\AgentRuntimes\python-3.13.7",
                "--confirm"
            ]);

        Assert.Equal(CliAction.RuntimeInstall, request.Action);
        Assert.Equal(
            @"D:\AgentRuntimes\python-3.13.7",
            request.InstallRoot);
        Assert.True(request.Confirmed);
    }

    [Fact]
    public void Parse_runtime_install_preview_requires_no_confirmation()
    {
        var request = CliArguments.Parse(
            [
                "runtime-install-preview",
                "uv",
                "0.12.21"
            ]);

        Assert.Equal(CliAction.RuntimeInstallPreview, request.Action);
        Assert.Equal("uv", request.ProviderId);
        Assert.Null(request.InstallRoot);
        Assert.False(request.Confirmed);
    }
}
