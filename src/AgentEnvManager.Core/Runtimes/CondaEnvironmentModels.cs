using AgentEnvManager.Core.Inspection;

namespace AgentEnvManager.Core.Runtimes;

public sealed record ObservedCondaEnvironment(
    string Name,
    string Prefix,
    bool IsActive,
    bool IsBase,
    ManagementState ManagementState = ManagementState.Observed);

public sealed record CondaRebuildPlan(
    string SourcePrefix,
    string DefinitionPath,
    string? TargetPrefix,
    string CommandLine,
    string Impact);
