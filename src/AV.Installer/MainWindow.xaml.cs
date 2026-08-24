using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AV.Installer.Services;
using Microsoft.Win32;

namespace AV.Installer;

public partial class MainWindow : Window
{
    private string _installPath = @"C:\Program Files\XentraAV";
    private int _currentStep = 1;
    private UpdateService? _updateService;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _updateService = new UpdateService();
        try
        {
            var updateInfo = await _updateService.CheckForUpdateAsync();
            if (updateInfo != null)
            {
                VersionText.Text = $"v{updateInfo.CurrentVersion} → v{updateInfo.LatestVersion}";
                UpdateStatusText.Text = $"New version available: v{updateInfo.LatestVersion}\n\nRelease notes:\n{updateInfo.ReleaseNotes}";
                DownloadUpdateButton.Visibility = Visibility.Visible;
                UpdateOverlay.Visibility = Visibility.Visible;
            }
        }
        catch
        {
            // No update available or network error — silently continue
        }
    }

    private void GoToStep(int step)
    {
        _currentStep = step;
        Step1Panel.Visibility = step == 1 ? Visibility.Visible : Visibility.Collapsed;
        Step2Panel.Visibility = step == 2 ? Visibility.Visible : Visibility.Collapsed;
        Step3Panel.Visibility = step == 3 ? Visibility.Visible : Visibility.Collapsed;
        Step4Panel.Visibility = step == 4 ? Visibility.Visible : Visibility.Collapsed;

        Step1Border.Background = step == 1 ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1A00F0FF")) : Brushes.Transparent;
        Step2Border.Background = step == 2 ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1A00F0FF")) : Brushes.Transparent;
        Step3Border.Background = step == 3 ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1A00F0FF")) : Brushes.Transparent;
        Step4Border.Background = step == 4 ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1A00F0FF")) : Brushes.Transparent;

        Step2Dot.Fill = step >= 2 ? FindBrush("NeonCyan") : FindBrush("DarkBorder");
        Step3Dot.Fill = step >= 3 ? FindBrush("NeonCyan") : FindBrush("DarkBorder");
        Step4Dot.Fill = step >= 4 ? FindBrush("NeonCyan") : FindBrush("DarkBorder");
        Step2Text.Foreground = step >= 2 ? FindBrush("NeonCyan") : FindBrush("DarkBorder");
        Step3Text.Foreground = step >= 3 ? FindBrush("NeonCyan") : FindBrush("DarkBorder");
        Step4Text.Foreground = step >= 4 ? FindBrush("NeonCyan") : FindBrush("DarkBorder");
    }

    private SolidColorBrush FindBrush(string key) =>
        (SolidColorBrush)FindResource(key);

    private void BeginButton_Click(object sender, RoutedEventArgs e) => GoToStep(2);

    private void ContinueButton_Click(object sender, RoutedEventArgs e) => GoToStep(3);

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Select Xentra AV Installation Directory",
            InitialDirectory = _installPath,
            Multiselect = false
        };
        if (dialog.ShowDialog() == true)
        {
            _installPath = dialog.FolderName;
            InstallPathBox.Text = _installPath;
        }
    }

    private async void InstallButton_AutoInstall()
    {
        BeginButton.IsEnabled = false;
        var logger = new InstallLogger(LogText, LogScroller);
        var service = new InstallerService(logger);

        var progressReporter = new Progress<int>(value =>
        {
            Dispatcher.Invoke(() =>
            {
                InstallProgress.Value = value;
                PercentText.Text = $"{value}%";
            });
        });

        try
        {
            await service.InstallAsync(
                _installPath,
                PinToTaskbarCheckBox.IsChecked == true,
                DesktopShortcutCheckBox.IsChecked == true);

            InstallProgress.Value = 100;
            PercentText.Text = "100%";
            CompletePathText.Text = $"Installed to: {_installPath}";
            GoToStep(4);
        }
        catch (Exception ex)
        {
            logger.Log($"[ERROR] {ex.Message}");
            MessageBox.Show($"Installation failed: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            BeginButton.IsEnabled = true;
        }
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        // Auto-start install after step 3 is shown
    }

    private void Step3Panel_IsVisibleChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
    {
        if (Step3Panel.Visibility == Visibility.Visible)
        {
            InstallButton_AutoInstall();
        }
    }

    private void LaunchButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var exePath = Path.Combine(_installPath, "AV.App.exe");
            if (File.Exists(exePath))
                Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true });
        }
        catch { }
        Application.Current.Shutdown();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();

    private async void UpdateButton_Click(object sender, RoutedEventArgs e)
    {
        UpdateOverlay.Visibility = Visibility.Visible;
        UpdateStatusText.Text = "Connecting to GitHub...";
        DownloadUpdateButton.Visibility = Visibility.Collapsed;

        try
        {
            _updateService ??= new UpdateService();
            var updateInfo = await _updateService.CheckForUpdateAsync();
            if (updateInfo != null)
            {
                UpdateStatusText.Text = $"New version: v{updateInfo.LatestVersion}\n\n{updateInfo.ReleaseNotes}";
                DownloadUpdateButton.Visibility = Visibility.Visible;
            }
            else
            {
                UpdateStatusText.Text = "You are running the latest version.";
            }
        }
        catch (Exception ex)
        {
            UpdateStatusText.Text = $"Update check failed: {ex.Message}";
        }
    }

    private async void DownloadUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        DownloadUpdateButton.IsEnabled = false;
        UpdateProgress.Visibility = Visibility.Visible;
        UpdateStatusText.Text = "Downloading update...";

        try
        {
            var progress = new Progress<double>(value =>
            {
                Dispatcher.Invoke(() => UpdateProgress.Value = value);
            });

            var tempPath = await _updateService!.DownloadUpdateAsync(progress);
            UpdateStatusText.Text = $"Downloaded to: {tempPath}\nInstalling...";

            _installPath = tempPath;
            InstallPathBox.Text = tempPath;
            UpdateOverlay.Visibility = Visibility.Collapsed;
            GoToStep(2);
        }
        catch (Exception ex)
        {
            UpdateStatusText.Text = $"Download failed: {ex.Message}";
            DownloadUpdateButton.IsEnabled = true;
        }
    }

    private void SkipUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        UpdateOverlay.Visibility = Visibility.Collapsed;
    }
}

internal class InstallLogger : IInstallerLogger
{
    private readonly TextBlock _log;
    private readonly ScrollViewer _scroller;

    public InstallLogger(TextBlock log, ScrollViewer scroller)
    {
        _log = log;
        _scroller = scroller;
    }

    public void Log(string message)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            _log.Text += message + "\n";
            _scroller.ScrollToEnd();
        });
    }
}
