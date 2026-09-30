using System.Windows;
using AgentEnvManager.Core.Storage;

namespace AgentEnvManager.Wpf;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel(
            new EnvironmentManagerClient(
                EnvironmentManagerFactory.Create(
                    Environment.GetEnvironmentVariable(
                        "AGENT_ENV_MANAGER_HOME"))));
    }
}
