using System.Windows;

namespace AgentEnvManager.Wpf;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel(
            new EnvironmentManagerClient(
                EnvironmentManagerFactory.Create()));
    }
}
