using AgentEnvManager.Core.EnvironmentVariables;
using AgentEnvManager.Core.Inspection;
using AgentEnvManager.Core.Scanning;
using AgentEnvManager.Core.Storage;

namespace AgentEnvManager.Wpf;

internal static class EnvironmentManagerFactory
{
    public static EnvironmentManager Create()
    {
        var paths = ManagerPaths.Resolve(
            Environment.GetEnvironmentVariable("AGENT_ENV_MANAGER_HOME"));
        return new EnvironmentManager(
            new WindowsEnvironmentProbe(
                new WindowsEnvironmentSnapshotSource()),
            manifestStore: new FileEnvironmentManifestStore(
                paths.ManifestDirectory),
            index: new SqliteEnvironmentIndex(paths.DatabasePath),
            recoveryPointStore: new FileEnvironmentRecoveryPointStore(
                paths.RecoveryDirectory),
            environmentVariableRecoveryPointStore:
                new FileEnvironmentVariableRecoveryPointStore(
                    paths.EnvironmentVariableRecoveryDirectory),
            operationJournal: new FileOperationJournal(
                paths.OperationDirectory),
            managerPaths: paths);
    }
}
