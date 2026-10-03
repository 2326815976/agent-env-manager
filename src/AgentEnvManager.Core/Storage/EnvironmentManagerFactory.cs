using AgentEnvManager.Core.EnvironmentVariables;
using AgentEnvManager.Core.Agents;
using AgentEnvManager.Core.Deletion;
using AgentEnvManager.Core.Inspection;
using AgentEnvManager.Core.Migrations;
using AgentEnvManager.Core.Scanning;

namespace AgentEnvManager.Core.Storage;

public static class EnvironmentManagerFactory
{
    public static EnvironmentManager CreateDefault()
    {
        return Create(
            Environment.GetEnvironmentVariable(
                "AGENT_ENV_MANAGER_HOME"),
            Environment.GetEnvironmentVariable(
                "AGENT_ENV_MANAGER_DATA_ROOT"));
    }

    public static EnvironmentManager Create(
        string? stateRoot = null,
        string? dataRoot = null)
    {
        var paths = ManagerPaths.Resolve(stateRoot, dataRoot);
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
            managerPaths: paths,
            quarantineStore: new FileEnvironmentQuarantineStore(
                paths.QuarantineDirectory),
            runtimeStateCatalog: new WindowsRuntimeStateCatalog(
                paths.RuntimeStateDirectory),
            migrationOccupancyProbe: new WindowsMigrationOccupancyProbe(),
            environmentPathMover: new FileSystemEnvironmentPathMover(),
            agentAdapters:
            [
                // Codex 是 ChatGPT 的内置工具调用组件（第一版不作为独立 Agent
                // 目标），因此默认只注册 ChatGPT；CodexAgentAdapter 仍保留，
                // 需要时可显式注册。
                new ChatGptAgentAdapter(
                    new SystemAgentProcessRunner(),
                    new FileAgentConfigurationBackupStore(
                        paths.AgentBackupDirectory))
            ]);
    }
}
