namespace AgentEnvManager.Core.Storage;

public sealed record ManagerPaths(
    string StateRoot,
    string DataRoot,
    string ManifestDirectory,
    string RecoveryDirectory,
    string EnvironmentVariableRecoveryDirectory,
    string OperationDirectory,
    string ShimDirectory,
    string AgentBackupDirectory,
    string QuarantineDirectory,
    string DatabasePath,
    string RuntimeDirectory,
    string RuntimeStateDirectory)
{
    public static ManagerPaths Resolve(
        string? stateRoot = null,
        string? dataRoot = null)
    {
        var resolvedRoot = string.IsNullOrWhiteSpace(stateRoot)
            ? Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "AgentEnvManager")
            : Path.GetFullPath(stateRoot);
        var resolvedDataRoot = string.IsNullOrWhiteSpace(dataRoot)
            ? Path.Combine(resolvedRoot, "data")
            : Path.GetFullPath(dataRoot);
        return new ManagerPaths(
            resolvedRoot,
            resolvedDataRoot,
            Path.Combine(resolvedRoot, "manifests"),
            Path.Combine(resolvedRoot, "recovery"),
            Path.Combine(resolvedRoot, "recovery", "environment-variables"),
            Path.Combine(resolvedRoot, "operations"),
            Path.Combine(resolvedRoot, "shims"),
            Path.Combine(resolvedRoot, "agent-backups"),
            Path.Combine(resolvedRoot, "quarantine"),
            Path.Combine(resolvedRoot, "index", "environments.db"),
            Path.Combine(resolvedDataRoot, "runtimes"),
            Path.Combine(resolvedDataRoot, "runtime-state"));
    }
}
