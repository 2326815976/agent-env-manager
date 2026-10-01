namespace AgentEnvManager.Wpf;

public interface IFileSystemPicker
{
    string? SelectFolder(
        string title,
        string? initialDirectory = null);

    string? SelectFile(
        string title,
        string filter,
        string? initialDirectory = null);
}
