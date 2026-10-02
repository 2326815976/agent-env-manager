using System.Reflection;
using System.Runtime.InteropServices;

namespace AgentEnvManager.Core.Migrations;

public sealed record ShortcutState(
    string? TargetPath,
    string? WorkingDirectory)
{
    public static ShortcutState Empty { get; } = new(null, null);
}

public interface IShortcutEditor
{
    Task<ShortcutState> ReadAsync(
        string shortcutPath,
        CancellationToken cancellationToken = default);

    Task WriteAsync(
        string shortcutPath,
        string targetPath,
        string workingDirectory,
        CancellationToken cancellationToken = default);
}

public sealed class WindowsShortcutEditor : IShortcutEditor
{
    public Task<ShortcutState> ReadAsync(
        string shortcutPath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(WithShortcut(
            shortcutPath,
            (shortcut, _) => new ShortcutState(
                GetProperty(shortcut, "TargetPath"),
                GetProperty(shortcut, "WorkingDirectory")),
            ShortcutState.Empty));
    }

    public Task WriteAsync(
        string shortcutPath,
        string targetPath,
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        WithShortcut(
            shortcutPath,
            (shortcut, type) =>
            {
                SetProperty(shortcut, type, "TargetPath", targetPath);
                SetProperty(
                    shortcut,
                    type,
                    "WorkingDirectory",
                    workingDirectory);
                type.InvokeMember(
                    "Save",
                    BindingFlags.InvokeMethod,
                    binder: null,
                    target: shortcut,
                    args: null);
                return true;
            },
            false);
        return Task.CompletedTask;
    }

    private static T WithShortcut<T>(
        string shortcutPath,
        Func<object, Type, T> action,
        T fallback)
    {
        object? shell = null;
        object? shortcut = null;
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null)
            {
                return fallback;
            }

            shell = Activator.CreateInstance(shellType);
            if (shell is null)
            {
                return fallback;
            }

            shortcut = shellType.InvokeMember(
                "CreateShortcut",
                BindingFlags.InvokeMethod,
                binder: null,
                target: shell,
                args: [shortcutPath]);
            return shortcut is null
                ? fallback
                : action(shortcut, shortcut.GetType());
        }
        catch (Exception exception) when (
            exception is COMException
            or TargetInvocationException
            or InvalidOperationException)
        {
            return fallback;
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

    private static string? GetProperty(object shortcut, string name)
    {
        return shortcut.GetType().InvokeMember(
            name,
            BindingFlags.GetProperty,
            binder: null,
            target: shortcut,
            args: null) as string;
    }

    private static void SetProperty(
        object shortcut,
        Type type,
        string name,
        string value)
    {
        type.InvokeMember(
            name,
            BindingFlags.SetProperty,
            binder: null,
            target: shortcut,
            args: [value]);
    }
}
