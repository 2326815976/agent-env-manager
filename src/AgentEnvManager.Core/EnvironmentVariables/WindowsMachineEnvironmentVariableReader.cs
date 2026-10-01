using Microsoft.Win32;

namespace AgentEnvManager.Core.EnvironmentVariables;

public sealed class WindowsMachineEnvironmentVariableReader
    : IMachineEnvironmentVariableReader
{
    private const string MachineEnvironmentKey =
        @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment";

    public Task<IReadOnlyDictionary<string, string?>> ReadAllAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var key = Registry.LocalMachine.OpenSubKey(
            MachineEnvironmentKey);
        var values = new Dictionary<string, string?>(
            StringComparer.OrdinalIgnoreCase);
        if (key is null)
        {
            return Task.FromResult<IReadOnlyDictionary<string, string?>>(
                values);
        }

        foreach (var name in key.GetValueNames().OrderBy(
                     name => name,
                     StringComparer.OrdinalIgnoreCase))
        {
            values[name] = key.GetValue(
                name,
                null,
                RegistryValueOptions.DoNotExpandEnvironmentNames)
                ?.ToString();
        }

        return Task.FromResult<IReadOnlyDictionary<string, string?>>(values);
    }
}
