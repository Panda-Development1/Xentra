using System.IO;
using System.Windows;
using AV.Engine.Interfaces;
using AV.Engine.Services;

namespace AV.App;

public partial class MainWindow : System.Windows.Window
{
    private IScanner? _scanner;
    private IQuarantine? _quarantine;
    private ILogger? _logger;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += async (s, e) => await InitAsync();
    }

    private async Task InitAsync()
    {
        var sigStore = new SignatureStore();
        await sigStore.LoadSignaturesAsync();
        _logger = new Logger(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "XentraAV", "Logs"));
        _scanner = new Scanner(sigStore, _logger);
        _quarantine = new Quarantine(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "XentraAV", "Quarantine"), _logger);
    }

    private async void ScanFileButton_Click(object sender, RoutedEventArgs e)
    {
        if (_scanner is null) return;
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select file to scan"
        };

        if (dialog.ShowDialog() == true)
        {
            StatusText.Text = "Scanning...";
            var result = await _scanner.ScanFileAsync(dialog.FileName);
            ResultsList.Items.Clear();
            ResultsList.Items.Add($"{result.FilePath}: {result.Status}");
            if (result.Detection != null)
                ResultsList.Items.Add($"  Threat: {result.Detection.ThreatName}");
            StatusText.Text = "Scan complete.";
        }
    }

    private async void ScanDirectoryButton_Click(object sender, RoutedEventArgs e)
    {
        if (_scanner is null) return;
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Select directory to scan"
        };
        if (dialog.ShowDialog() == true)
        {
            StatusText.Text = "Scanning directory...";
            var results = await _scanner.ScanDirectoryAsync(dialog.FolderName);
            ResultsList.Items.Clear();
            foreach (var r in results)
            {
                ResultsList.Items.Add($"{r.FilePath}: {r.Status}");
            }
            StatusText.Text = $"Scan complete. {results.Count} files scanned.";
        }
    }

    private async void QuarantineListButton_Click(object sender, RoutedEventArgs e)
    {
        if (_quarantine is null) return;
        var entries = await _quarantine.ListAsync();
        ResultsList.Items.Clear();
        foreach (var entry in entries)
        {
            ResultsList.Items.Add($"{entry.Id}: {entry.OriginalFileName} ({entry.ThreatName})");
        }
        StatusText.Text = $"{entries.Count} quarantined items.";
    }
}
