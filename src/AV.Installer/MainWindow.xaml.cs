using System.Diagnostics;
using System.IO;
using System.Windows;
using AV.Installer.Services;

namespace AV.Installer;

public partial class MainWindow : System.Windows.Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Select installation folder"
        };
        if (dialog.ShowDialog() == true)
        {
            InstallPathBox.Text = dialog.FolderName;
        }
    }

    private async void InstallButton_Click(object sender, RoutedEventArgs e)
    {
        InstallButton.IsEnabled = false;
        var logger = new InstallerLogger(LogText, LogScroller);
        var installer = new InstallerService(logger);

        try
        {
            string installPath = InstallPathBox.Text;
            bool pinToTaskbar = PinToTaskbarCheckBox.IsChecked == true;
            bool createDesktopShortcut = DesktopShortcutCheckBox.IsChecked == true;

            await installer.InstallAsync(installPath, pinToTaskbar, createDesktopShortcut);

            MessageBox.Show("Installation complete!", "Xentra AV", MessageBoxButton.OK, MessageBoxImage.Information);

            string appExe = Path.Combine(installPath, "AV.App.exe");
            if (File.Exists(appExe))
            {
                Process.Start(new ProcessStartInfo(appExe) { UseShellExecute = true });
            }

            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Installation failed: {ex.Message}", "Xentra AV", MessageBoxButton.OK, MessageBoxImage.Error);
            InstallButton.IsEnabled = true;
        }
    }
}

public interface IInstallerLogger
{
    void Log(string message);
}

public class InstallerLogger : IInstallerLogger
{
    private readonly System.Windows.Controls.TextBlock _logText;
    private readonly System.Windows.Controls.ScrollViewer _scroller;

    public InstallerLogger(System.Windows.Controls.TextBlock logText, System.Windows.Controls.ScrollViewer scroller)
    {
        _logText = logText;
        _scroller = scroller;
    }

    public void Log(string message)
    {
        _logText.Text += message + Environment.NewLine;
        _scroller.ScrollToEnd();
    }
}
