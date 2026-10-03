using System.Diagnostics;
using AgentEnvManager.Core.Inspection;
using Microsoft.Win32;

namespace AgentEnvManager.Core.Scanning;

public sealed record WindowsAppPathRegistration(
    string ExecutableName,
    string Path);

public interface IWindowsEnvironmentAccessor
{
    string? GetEnvironmentVariable(string name);

    IReadOnlyList<string> ReadPath(PathScope scope);

    string GetFolderPath(Environment.SpecialFolder folder);

    string ExpandEnvironmentVariables(string value);

    bool FileExists(string path);

    bool DirectoryExists(string path);

    IReadOnlyList<string> EnumerateDirectories(
        string root,
        string searchPattern);

    IReadOnlyList<WindowsAppPathRegistration> ReadAppPathRegistrations();

    string? ReadFileVersion(string path);

    string? ResolveLinkTarget(string path);
}

public sealed class WindowsEnvironmentAccessor : IWindowsEnvironmentAccessor
{
    public string? ResolveLinkTarget(string path)
    {
        try
        {
            return Directory.ResolveLinkTarget(
                path,
                returnFinalTarget: true)?.FullName;
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException)
        {
            return null;
        }
    }

    public string? GetEnvironmentVariable(string name)
    {
        return Environment.GetEnvironmentVariable(name);
    }

    public IReadOnlyList<string> ReadPath(PathScope scope)
    {
        var target = scope switch
        {
            PathScope.Process => EnvironmentVariableTarget.Process,
            PathScope.User => EnvironmentVariableTarget.User,
            PathScope.Machine => EnvironmentVariableTarget.Machine,
            _ => throw new ArgumentOutOfRangeException(nameof(scope))
        };
        var value = Environment.GetEnvironmentVariable("Path", target);

        return string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(
                Path.PathSeparator,
                StringSplitOptions.RemoveEmptyEntries
                | StringSplitOptions.TrimEntries);
    }

    public string GetFolderPath(Environment.SpecialFolder folder)
    {
        return Environment.GetFolderPath(folder);
    }

    public string ExpandEnvironmentVariables(string value)
    {
        return Environment.ExpandEnvironmentVariables(value);
    }

    public bool FileExists(string path)
    {
        return File.Exists(path);
    }

    public bool DirectoryExists(string path)
    {
        return Directory.Exists(path);
    }

    public IReadOnlyList<string> EnumerateDirectories(
        string root,
        string searchPattern)
    {
        return Directory.EnumerateDirectories(
                root,
                searchPattern,
                SearchOption.TopDirectoryOnly)
            .ToArray();
    }

    public IReadOnlyList<WindowsAppPathRegistration> ReadAppPathRegistrations()
    {
        var registrations = new List<WindowsAppPathRegistration>();
        ReadAppPathRegistrations(
            Registry.CurrentUser,
            registrations);
        ReadAppPathRegistrations(
            Registry.LocalMachine,
            registrations);
        return registrations;
    }

    public string? ReadFileVersion(string path)
    {
        try
        {
            var versionInfo = FileVersionInfo.GetVersionInfo(path);
            var version = versionInfo.ProductVersion ?? versionInfo.FileVersion;
            return string.IsNullOrWhiteSpace(version)
                ? null
                : version
                    .Split('+', 2)[0]
                    .Split(' ', 2)[0];
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }

    private static void ReadAppPathRegistrations(
        RegistryKey root,
        ICollection<WindowsAppPathRegistration> registrations)
    {
        using var appPaths = root.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\App Paths");
        if (appPaths is null)
        {
            return;
        }

        foreach (var executableName in appPaths.GetSubKeyNames())
        {
            using var appPath = appPaths.OpenSubKey(executableName);
            if (appPath?.GetValue(null) is not string rawPath)
            {
                continue;
            }

            registrations.Add(new WindowsAppPathRegistration(
                executableName,
                Environment.ExpandEnvironmentVariables(
                    rawPath.Trim().Trim('"'))));
        }
    }
}
