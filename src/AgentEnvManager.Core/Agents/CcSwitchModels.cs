namespace AgentEnvManager.Core.Agents;

public sealed record CcSwitchDiscoveryRequest(
    string? ConfigRoot = null,
    string? Executable = null,
    string? WebViewLocalRoot = null,
    string? WebViewRoamingRoot = null);

public sealed record CcSwitchDiscovery(
    bool IsInstalled,
    string? Executable,
    string? Version,
    string ConfigRoot,
    string SettingsFilePath,
    string? CodexConfigDirectory,
    IReadOnlyList<string> CompatibilityJunctions,
    string Message);

public sealed record CcSwitchBindingPreview(
    string SettingsFilePath,
    string FieldName,
    string? CurrentValue,
    string TargetValue,
    string Impact,
    IReadOnlyList<string> PreservedContent,
    IReadOnlyList<string> CompatibilityJunctions,
    string? OperationId,
    string? RecoveryPointId,
    bool IsAlreadyBound);

public sealed record CcSwitchBindingResult(
    Operations.OperationRecord Operation,
    AgentConfigurationRecoveryPoint RecoveryPoint,
    CcSwitchDiscovery Discovery);
