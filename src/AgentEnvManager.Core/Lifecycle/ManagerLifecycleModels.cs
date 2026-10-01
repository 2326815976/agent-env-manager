namespace AgentEnvManager.Core.Lifecycle;

public sealed record ManagerLifecyclePreview(
    string Action,
    IReadOnlyList<string> RemovedPaths,
    IReadOnlyList<string> PreservedPaths,
    string Impact,
    bool RequiresConfirmation,
    bool IsConfirmed);

public interface IManagerLifecycleService
{
    ManagerLifecyclePreview PreviewUpgrade(string targetVersion);

    ManagerLifecyclePreview PreviewUninstall(
        bool clearManagerData,
        bool confirmed);
}

public sealed class ManagerLifecycleService : IManagerLifecycleService
{
    private readonly string _appRoot;
    private readonly string _stateRoot;
    private readonly string _dataRoot;

    public ManagerLifecycleService(
        string appRoot,
        string stateRoot,
        string dataRoot)
    {
        _appRoot = Path.GetFullPath(appRoot);
        _stateRoot = Path.GetFullPath(stateRoot);
        _dataRoot = Path.GetFullPath(dataRoot);
    }

    public ManagerLifecyclePreview PreviewUpgrade(string targetVersion)
    {
        if (string.IsNullOrWhiteSpace(targetVersion))
        {
            throw new ArgumentException(
                "目标版本不能为空。",
                nameof(targetVersion));
        }

        return new ManagerLifecyclePreview(
            $"升级到 {targetVersion}",
            [_appRoot],
            [_stateRoot, _dataRoot],
            "升级只替换管理器程序文件，不修改工具运行时、Agent 绑定、恢复点或缓存。",
            RequiresConfirmation: false,
            IsConfirmed: true);
    }

    public ManagerLifecyclePreview PreviewUninstall(
        bool clearManagerData,
        bool confirmed)
    {
        if (!clearManagerData)
        {
            return new ManagerLifecyclePreview(
                "默认卸载",
                [_appRoot],
                [_stateRoot, _dataRoot],
                "默认卸载只移除管理器程序文件，保留状态、工具运行时和恢复点。",
                RequiresConfirmation: false,
                IsConfirmed: true);
        }

        if (!confirmed)
        {
            throw new InvalidOperationException(
                "清除管理器数据需要单独确认。");
        }

        return new ManagerLifecyclePreview(
            "卸载并清除管理器数据",
            [_appRoot, _stateRoot, _dataRoot],
            [],
            "已单独确认：移除管理器程序文件、状态、运行时和恢复点。",
            RequiresConfirmation: true,
            IsConfirmed: true);
    }
}
