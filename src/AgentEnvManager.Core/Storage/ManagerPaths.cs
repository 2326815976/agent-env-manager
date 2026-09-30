namespace AgentEnvManager.Core.Storage;

public sealed record ManagerPaths(
    string StateRoot,
    string ManifestDirectory,
    string RecoveryDirectory,
    string OperationDirectory,
    string DatabasePath)
{
    public static ManagerPaths Resolve(string? stateRoot = null)
    {
        var resolvedRoot = string.IsNullOrWhiteSpace(stateRoot)
            ? Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "AgentEnvManager")
            : Path.GetFullPath(stateRoot);
        return new ManagerPaths(
            resolvedRoot,
            Path.Combine(resolvedRoot, "manifests"),
            Path.Combine(resolvedRoot, "recovery"),
            Path.Combine(resolvedRoot, "operations"),
            Path.Combine(resolvedRoot, "index", "environments.db"));
    }
}
