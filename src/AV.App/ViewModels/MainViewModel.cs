using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Threading;
using AV.App.Services;

namespace AV.App.ViewModels;

public class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IpcClient _ipc;
    private readonly DispatcherTimer _reconnectTimer;
    private CancellationTokenSource? _scanCts;

    public MainViewModel()
    {
        _ipc = new IpcClient();
        _reconnectTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _reconnectTimer.Tick += async (s, e) => await TryConnectAsync();
        _reconnectTimer.Start();
        _ = TryConnectAsync();
    }

    private ViewType _currentView = ViewType.Dashboard;
    public ViewType CurrentView
    {
        get => _currentView;
        set { _currentView = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsDashboardVisible)); OnPropertyChanged(nameof(IsScanVisible)); OnPropertyChanged(nameof(isQuarantineVisible)); }
    }

    public bool IsDashboardVisible => CurrentView == ViewType.Dashboard;
    public bool IsScanVisible => CurrentView == ViewType.Scan;
    public bool isQuarantineVisible => CurrentView == ViewType.Quarantine;

    private string _statusText = "Disconnected";
    public string StatusText
    {
        get => _statusText;
        set { _statusText = value; OnPropertyChanged(); }
    }

    private string _shieldStatus = "Inactive";
    public string ShieldStatus
    {
        get => _shieldStatus;
        set { _shieldStatus = value; OnPropertyChanged(); OnPropertyChanged(nameof(ShieldStatusColor)); }
    }

    public string ShieldStatusColor => ShieldStatus == "Active" ? "#39FF14" : "#FF4444";

    private int _quarantineCount;
    public int QuarantineCount
    {
        get => _quarantineCount;
        set { _quarantineCount = value; OnPropertyChanged(); }
    }

    private int _scanProgress;
    public int ScanProgress
    {
        get => _scanProgress;
        set { _scanProgress = value; OnPropertyChanged(); }
    }

    private string _scanStatusText = "";
    public string ScanStatusText
    {
        get => _scanStatusText;
        set { _scanStatusText = value; OnPropertyChanged(); }
    }

    private int _scannedFiles;
    public int ScannedFiles
    {
        get => _scannedFiles;
        set { _scannedFiles = value; OnPropertyChanged(); }
    }

    private int _threatsFound;
    public int ThreatsFound
    {
        get => _threatsFound;
        set { _threatsFound = value; OnPropertyChanged(); }
    }

    private string _currentScanFile = "";
    public string CurrentScanFile
    {
        get => _currentScanFile;
        set { _currentScanFile = value; OnPropertyChanged(); }
    }

    private bool _isScanning;
    public bool IsScanning
    {
        get => _isScanning;
        set { _isScanning = value; OnPropertyChanged(); }
    }

    private bool _scanComplete;
    public bool ScanComplete
    {
        get => _scanComplete;
        set { _scanComplete = value; OnPropertyChanged(); }
    }

    private string _scanCompleteSummary = "";
    public string ScanCompleteSummary
    {
        get => _scanCompleteSummary;
        set { _scanCompleteSummary = value; OnPropertyChanged(); }
    }

    public ObservableCollection<string> ScanResults { get; } = new();
    public ObservableCollection<QuarantineItemViewModel> QuarantineItems { get; } = new();

    private string _selectedQuarantineId = "";
    public string SelectedQuarantineId
    {
        get => _selectedQuarantineId;
        set { _selectedQuarantineId = value; OnPropertyChanged(); }
    }

    public async Task ScanFileAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Select file to scan" };
        if (dialog.ShowDialog() == true)
            await StartScanAsync(dialog.FileName);
    }

    public async Task ScanDirectoryAsync()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Select directory to scan" };
        if (dialog.ShowDialog() == true)
            await StartScanAsync(dialog.FolderName);
    }

    private async Task StartScanAsync(string path)
    {
        if (!IsConnected || IsScanning) return;

        CurrentView = ViewType.Scan;
        IsScanning = true;
        ScanComplete = false;
        ScanProgress = 0;
        ScannedFiles = 0;
        ThreatsFound = 0;
        CurrentScanFile = "";
        ScanStatusText = "Starting scan...";
        ScanResults.Clear();

        _scanCts = new CancellationTokenSource();

        await _ipc.SendScanCommandAsync(path, line =>
        {
            Application.Current.Dispatcher.Invoke(() => HandleScanLine(line));
        }, _scanCts.Token);
    }

    private void HandleScanLine(string line)
    {
        if (line.StartsWith("SCAN_PROGRESS:"))
        {
            var parts = line[14..].Split('|', 2);
            if (int.TryParse(parts[0], out int count))
            {
                ScannedFiles = count;
                CurrentScanFile = parts.Length > 1 ? Path.GetFileName(parts[1]) : "";
                ScanStatusText = $"Scanned {ScannedFiles} files...";
            }
        }
        else if (line.StartsWith("SCAN_RESULT:"))
        {
            var parts = line[12..].Split('|', 2);
            string status = parts[0];
            string filePath = parts.Length > 1 ? parts[1] : "";
            string fileName = Path.GetFileName(filePath);

            ScannedFiles++;
            ScanResults.Add($"{status}: {fileName}");

            if (status == "Threat")
                ThreatsFound++;
        }
        else if (line.StartsWith("SCAN_DETECTION:"))
        {
            var parts = line[15..].Split('|', 2);
            string threat = parts[0];
            string filePath = parts.Length > 1 ? parts[1] : "";
            ScanResults.Add($"  THREAT: {threat} in {Path.GetFileName(filePath)}");
        }
        else if (line.StartsWith("SCAN_COMPLETE:"))
        {
            var parts = line[14..].Split('|');
            if (parts.Length >= 3)
            {
                int.TryParse(parts[0], out int total);
                int.TryParse(parts[1], out int threats);
                int.TryParse(parts[2], out int errors);

                ScannedFiles = total;
                ThreatsFound = threats;
                ScanProgress = 100;
                IsScanning = false;
                ScanComplete = true;
                ScanCompleteSummary = $"Scan complete: {total} files, {threats} threats, {errors} errors";

                _ = Task.Delay(3000).ContinueWith(_ =>
                {
                    Application.Current.Dispatcher.Invoke(() => CurrentView = ViewType.Dashboard);
                });
            }
        }
        else if (line.StartsWith("ERR:"))
        {
            ScanStatusText = line;
            IsScanning = false;
        }
    }

    public async Task LoadQuarantineAsync()
    {
        if (!IsConnected) return;

        var response = await _ipc.SendCommandAsync("GET_QUARANTINE_LIST");
        if (response == null) return;

        QuarantineItems.Clear();

        if (response == "QUARANTINE_LIST:EMPTY") return;

        var lines = response.Split('\n');
        foreach (var line in lines)
        {
            if (!line.StartsWith("QUARANTINE_ENTRY:")) continue;
            var parts = line[17..].Split('|');
            if (parts.Length >= 5)
            {
                QuarantineItems.Add(new QuarantineItemViewModel
                {
                    Id = parts[0],
                    FileName = parts[1],
                    ThreatName = parts[2],
                    QuarantinedAt = parts[3],
                    OriginalPath = parts[4]
                });
            }
        }

        QuarantineCount = QuarantineItems.Count;
    }

    public async Task RestoreSelectedAsync()
    {
        if (!IsConnected || string.IsNullOrEmpty(SelectedQuarantineId)) return;
        await _ipc.SendCommandAsync($"RESTORE_FILE {SelectedQuarantineId}");
        await LoadQuarantineAsync();
    }

    public async Task DeleteSelectedAsync()
    {
        if (!IsConnected || string.IsNullOrEmpty(SelectedQuarantineId)) return;
        await _ipc.SendCommandAsync($"DELETE_FILE {SelectedQuarantineId}");
        await LoadQuarantineAsync();
    }

    private async Task TryConnectAsync()
    {
        if (IsConnected) return;

        var connected = await _ipc.ConnectAsync();
        if (connected)
        {
            StatusText = "Connected";
            ShieldStatus = "Active";
            _reconnectTimer.Stop();

            var response = await _ipc.SendCommandAsync("GET_STATUS");
            if (response != null && response.StartsWith("STATUS:"))
            {
                var parts = response[7..].Split('|');
                StatusText = parts[0];
            }

            await LoadQuarantineAsync();
        }
        else
        {
            StatusText = "Disconnected - retrying...";
            ShieldStatus = "Inactive";
        }
    }

    public bool IsConnected => _ipc.IsConnected;

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public void Dispose()
    {
        _scanCts?.Cancel();
        _scanCts?.Dispose();
        _ipc?.Dispose();
    }
}

public enum ViewType { Dashboard, Scan, Quarantine }

public class QuarantineItemViewModel
{
    public string Id { get; set; } = "";
    public string FileName { get; set; } = "";
    public string ThreatName { get; set; } = "";
    public string QuarantinedAt { get; set; } = "";
    public string OriginalPath { get; set; } = "";
}
