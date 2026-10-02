using AgentEnvManager.Core.Agents;

namespace AgentEnvManager.Core.Migrations;

public sealed class WindowsCoordinatedTargetsProvider(
    IExecutableLocator? chatGptLocator = null,
    Func<Environment.SpecialFolder, string>? getFolderPath = null)
    : ICoordinatedTargetsProvider
{
    private const string ShortcutFileName = "ChatGPT.lnk";

    private readonly IExecutableLocator _chatGptLocator =
        chatGptLocator ?? new ChatGptExecutableLocator();
    private readonly Func<Environment.SpecialFolder, string> _getFolderPath =
        getFolderPath ?? Environment.GetFolderPath;

    public Task<CoordinatedShortcutTarget?> ResolveChatGptShortcutAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var executable = _chatGptLocator.FindExecutable("ChatGPT");
        if (string.IsNullOrWhiteSpace(executable)
            || !File.Exists(executable))
        {
            return Task.FromResult<CoordinatedShortcutTarget?>(null);
        }

        var shortcutPath = Path.Combine(
            _getFolderPath(Environment.SpecialFolder.ApplicationData),
            "Microsoft",
            "Windows",
            "Start Menu",
            "Programs",
            ShortcutFileName);
        if (!File.Exists(shortcutPath))
        {
            return Task.FromResult<CoordinatedShortcutTarget?>(null);
        }

        var fullExecutable = Path.GetFullPath(executable);
        return Task.FromResult<CoordinatedShortcutTarget?>(
            new CoordinatedShortcutTarget(
                shortcutPath,
                fullExecutable,
                Path.GetDirectoryName(fullExecutable) ?? string.Empty));
    }

    public Task<CoordinatedStartupTargets?> ResolveStartupTargetsAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var chatGpt = _chatGptLocator.FindExecutable("ChatGPT");
        var ccSwitch = FindCcSwitchExecutable();
        if (string.IsNullOrWhiteSpace(chatGpt)
            || string.IsNullOrWhiteSpace(ccSwitch)
            || !File.Exists(chatGpt)
            || !File.Exists(ccSwitch))
        {
            return Task.FromResult<CoordinatedStartupTargets?>(null);
        }

        return Task.FromResult<CoordinatedStartupTargets?>(
            new CoordinatedStartupTargets(
                Path.GetFullPath(ccSwitch),
                Path.GetFullPath(chatGpt)));
    }

    private string? FindCcSwitchExecutable()
    {
        var configured = Environment.GetEnvironmentVariable(
            "CC_SWITCH_EXECUTABLE");
        if (IsExistingFile(configured))
        {
            return configured;
        }

        var localAppData = _getFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        var programFiles = _getFolderPath(
            Environment.SpecialFolder.ProgramFiles);
        string[] candidates =
        [
            Path.Combine(
                localAppData,
                "Programs",
                "CC Switch",
                "cc-switch.exe"),
            Path.Combine(programFiles, "CC Switch", "cc-switch.exe")
        ];
        foreach (var candidate in candidates)
        {
            if (IsExistingFile(candidate))
            {
                return candidate;
            }
        }

        var shortcut = Path.Combine(
            _getFolderPath(Environment.SpecialFolder.ApplicationData),
            "Microsoft",
            "Windows",
            "Start Menu",
            "Programs",
            "CC Switch",
            "CC Switch.lnk");
        if (!File.Exists(shortcut))
        {
            return null;
        }

        var target = WindowsShortcutResolver.Resolve(shortcut);
        return IsExistingFile(target) ? target : null;
    }

    private static bool IsExistingFile(string? path)
    {
        return !string.IsNullOrWhiteSpace(path)
            && Path.IsPathFullyQualified(path)
            && File.Exists(path);
    }
}
