using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AV.App.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    private string _status = "Ready";
    public string Status
    {
        get => _status;
        set { _status = value; OnPropertyChanged(); }
    }

    private int _scanProgress;
    public int ScanProgress
    {
        get => _scanProgress;
        set { _scanProgress = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
