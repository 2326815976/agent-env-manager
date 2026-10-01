using Microsoft.Win32;

namespace AgentEnvManager.Wpf;

public sealed class WindowsFileSystemPicker : IFileSystemPicker
{
    public string? SelectFolder(
        string title,
        string? initialDirectory = null)
    {
        var dialog = new OpenFolderDialog
        {
            Title = title,
            InitialDirectory = initialDirectory ?? string.Empty
        };
        return dialog.ShowDialog() == true
            ? dialog.FolderName
            : null;
    }

    public string? SelectFile(
        string title,
        string filter,
        string? initialDirectory = null)
    {
        var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = filter,
            InitialDirectory = initialDirectory ?? string.Empty
        };
        return dialog.ShowDialog() == true
            ? dialog.FileName
            : null;
    }
}
