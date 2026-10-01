using System.Reflection;
using System.Runtime.InteropServices;

namespace AgentEnvManager.Core.Agents;

public sealed class ChatGptExecutableLocator(
    Func<string, string?>? readEnvironmentVariable = null,
    Func<string, string?>? resolveShortcut = null)
    : IExecutableLocator
{
    private readonly Func<string, string?> _readEnvironmentVariable =
        readEnvironmentVariable ?? Environment.GetEnvironmentVariable;
    private readonly Func<string, string?> _resolveShortcut =
        resolveShortcut ?? ResolveShortcutTarget;

    public string? FindExecutable(string command)
    {
        if (!string.Equals(
                command,
                "ChatGPT",
                StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var configured = _readEnvironmentVariable(
            "CHATGPT_EXECUTABLE");
        if (IsExistingFile(configured))
        {
            return Path.GetFullPath(configured!);
        }

        foreach (var candidate in GetCommonCandidates())
        {
            if (IsExistingFile(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        foreach (var shortcut in GetShortcutCandidates())
        {
            if (!File.Exists(shortcut))
            {
                continue;
            }

            var target = _resolveShortcut(shortcut);
            if (IsExistingFile(target))
            {
                return Path.GetFullPath(target!);
            }
        }

        return null;
    }

    private IReadOnlyList<string> GetCommonCandidates()
    {
        var localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        var programFiles = Environment.GetFolderPath(
            Environment.SpecialFolder.ProgramFiles);
        return
        [
            Path.Combine(
                localAppData,
                "Programs",
                "ChatGPT",
                "ChatGPT.exe"),
            Path.Combine(
                localAppData,
                "OpenAI",
                "ChatGPTLauncher",
                "ChatGPT.exe"),
            Path.Combine(
                localAppData,
                "OpenAI",
                "ChatGPT",
                "ChatGPT.exe"),
            Path.Combine(
                programFiles,
                "ChatGPT",
                "ChatGPT.exe")
        ];
    }

    private static IReadOnlyList<string> GetShortcutCandidates()
    {
        return
        [
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.ApplicationData),
                "Microsoft",
                "Windows",
                "Start Menu",
                "Programs",
                "ChatGPT.lnk"),
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.CommonApplicationData),
                "Microsoft",
                "Windows",
                "Start Menu",
                "Programs",
                "ChatGPT.lnk")
        ];
    }

    private static bool IsExistingFile(string? path)
    {
        return !string.IsNullOrWhiteSpace(path)
            && Path.IsPathFullyQualified(path)
            && File.Exists(path);
    }

    private static string? ResolveShortcutTarget(string shortcutPath)
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
