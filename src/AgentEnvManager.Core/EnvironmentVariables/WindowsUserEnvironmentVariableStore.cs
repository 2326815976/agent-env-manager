using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace AgentEnvManager.Core.EnvironmentVariables;

public sealed class WindowsUserEnvironmentVariableStore
    : IUserEnvironmentVariableStore
{
    public Task<string?> GetAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var key = Registry.CurrentUser.OpenSubKey("Environment");
        var value = key?.GetValue(
            name,
            null,
            RegistryValueOptions.DoNotExpandEnvironmentNames);
        return Task.FromResult(value?.ToString());
    }

    public Task<bool> IsExpandableAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var key = Registry.CurrentUser.OpenSubKey("Environment");
        try
        {
            return Task.FromResult(
                key?.GetValueKind(name) == RegistryValueKind.ExpandString);
        }
        catch (IOException)
        {
            return Task.FromResult(false);
        }
    }

    public Task SetAsync(
        string name,
        string? value,
        bool isExpandable = false,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var key = Registry.CurrentUser.CreateSubKey("Environment");
        if (value is null)
        {
            key.DeleteValue(name, throwOnMissingValue: false);
        }
        else
        {
            key.SetValue(
                name,
                value,
                isExpandable
                    ? RegistryValueKind.ExpandString
                    : RegistryValueKind.String);
        }

        return Task.CompletedTask;
    }

    public Task BroadcastAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = SendMessageTimeout(
            new IntPtr(0xffff),
            0x001A,
            UIntPtr.Zero,
            "Environment",
            0x0002,
            5000,
            out _);
        if (result == IntPtr.Zero)
        {
            throw new System.ComponentModel.Win32Exception(
                Marshal.GetLastWin32Error(),
                "广播环境变量变更失败。");
        }

        return Task.CompletedTask;
    }

    [DllImport(
        "user32.dll",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hWnd,
        uint message,
        UIntPtr wParam,
        string lParam,
        uint flags,
        uint timeout,
        out UIntPtr result);
}
