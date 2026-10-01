using AgentEnvManager.Core.Lifecycle;

namespace AgentEnvManager.Core.Tests.Lifecycle;

public sealed class ManagerLifecycleServiceTests
{
    [Fact]
    public void PreviewUpgrade_only_replaces_application_files()
    {
        var service = CreateService();

        var preview = service.PreviewUpgrade("0.2.0");

        Assert.Equal(
            [@"C:\install\AgentEnvManager\app"],
            preview.RemovedPaths);
        Assert.Contains(@"C:\state", preview.PreservedPaths);
        Assert.Contains(@"D:\data", preview.PreservedPaths);
        Assert.Contains(
            "不修改工具运行时",
            preview.Impact,
            StringComparison.Ordinal);
    }

    [Fact]
    public void PreviewUninstall_default_preserves_state_and_runtime()
    {
        var preview = CreateService().PreviewUninstall(
            clearManagerData: false,
            confirmed: false);

        Assert.Equal(
            [@"C:\install\AgentEnvManager\app"],
            preview.RemovedPaths);
        Assert.Contains(@"C:\state", preview.PreservedPaths);
        Assert.Contains(@"D:\data", preview.PreservedPaths);
        Assert.Contains(
            "保留状态、工具运行时和恢复点",
            preview.Impact,
            StringComparison.Ordinal);
    }

    [Fact]
    public void PreviewUninstall_clear_data_requires_confirmation()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => CreateService().PreviewUninstall(
                clearManagerData: true,
                confirmed: false));

        Assert.Contains(
            "单独确认",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void PreviewUninstall_clear_data_removes_all_manager_assets()
    {
        var preview = CreateService().PreviewUninstall(
            clearManagerData: true,
            confirmed: true);

        Assert.Contains(@"C:\install\AgentEnvManager\app", preview.RemovedPaths);
        Assert.Contains(@"C:\state", preview.RemovedPaths);
        Assert.Contains(@"D:\data", preview.RemovedPaths);
        Assert.Empty(preview.PreservedPaths);
    }

    private static ManagerLifecycleService CreateService()
    {
        return new ManagerLifecycleService(
            @"C:\install\AgentEnvManager\app",
            @"C:\state",
            @"D:\data");
    }
}
