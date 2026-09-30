using AgentEnvManager.Core.Inspection;

namespace AgentEnvManager.Core.Scanning;

internal static class RegistryAppPathCollector
{
    public static IReadOnlyList<ExecutableCandidate> Collect(
        IWindowsEnvironmentAccessor accessor)
    {
        var candidates = new List<ExecutableCandidate>();

        foreach (var registration in accessor.ReadAppPathRegistrations())
        {
            var command = CommandCatalog.Commands.FirstOrDefault(item =>
                string.Equals(
                    item.AppPathFileName,
                    registration.ExecutableName,
                    StringComparison.OrdinalIgnoreCase));
            if (command is null || !accessor.FileExists(registration.Path))
            {
                continue;
            }

            candidates.Add(PathCommandCollector.CreateCandidate(
                accessor,
                command,
                registration.Path,
                DiscoverySourceInfo.AppPathsRegistry));
        }

        return candidates;
    }
}
