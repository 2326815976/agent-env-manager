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
}
