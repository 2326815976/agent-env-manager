using System.Reflection;
using System.Runtime.InteropServices;

namespace AgentEnvManager.Core.Agents;

internal static class WindowsShortcutResolver
{
    public static string? Resolve(string shortcutPath)
    {
        object? shell = null;
        object? shortcut = null;
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null)
            {
                return null;
            }

            shell = Activator.CreateInstance(shellType);
            if (shell is null)
            {
                return null;
            }

            shortcut = shellType.InvokeMember(
                "CreateShortcut",
                BindingFlags.InvokeMethod,
                binder: null,
                target: shell,
                args: [shortcutPath]);
            return shortcut?
                .GetType()
                .InvokeMember(
                    "TargetPath",
                    BindingFlags.GetProperty,
                    binder: null,
                    target: shortcut,
                    args: null) as string;
        }
        catch (Exception exception) when (
            exception is COMException
            or TargetInvocationException
            or InvalidOperationException)
        {
            return null;
        }
        finally
        {
            if (shortcut is not null && Marshal.IsComObject(shortcut))
            {
                Marshal.FinalReleaseComObject(shortcut);
            }

            if (shell is not null && Marshal.IsComObject(shell))
            {
                Marshal.FinalReleaseComObject(shell);
            }
        }
    }
}
