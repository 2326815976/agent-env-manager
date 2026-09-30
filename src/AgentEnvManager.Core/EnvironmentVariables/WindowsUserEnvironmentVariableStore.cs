using System.Runtime.InteropServices;

namespace AgentEnvManager.Core.EnvironmentVariables;

public sealed class WindowsUserEnvironmentVariableStore
    : IUserEnvironmentVariableStore
{
    public Task<string?> GetAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Environment.GetEnvironmentVariable(
            name,
            EnvironmentVariableTarget.User));
    }

    public Task SetAsync(
        string name,
        string? value,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Environment.SetEnvironmentVariable(
            name,
            value,
            EnvironmentVariableTarget.User);
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
