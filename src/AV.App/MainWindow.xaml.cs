using System.Windows;
using System.Windows.Controls;
using AV.App.ViewModels;

namespace AV.App;

public partial class MainWindow : System.Windows.Window
{
    private MainViewModel ViewModel => (MainViewModel)DataContext;

    public MainWindow()
    {
        InitializeComponent();
    }

    private void Dashboard_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.CurrentView = ViewType.Dashboard;
    }

    private void ScanView_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.CurrentView = ViewType.Scan;
    }

    private void QuarantineView_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.CurrentView = ViewType.Quarantine;
        _ = ViewModel.LoadQuarantineAsync();
    }

    private async void ScanFileButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.ScanFileAsync();
    }

    private async void ScanDirectoryButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.ScanDirectoryAsync();
    }

    private async void RefreshQuarantine_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadQuarantineAsync();
    }

    private async void RestoreQuarantine_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.RestoreSelectedAsync();
    }

    private async void DeleteQuarantine_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.DeleteSelectedAsync();
    }

    private void QuarantineGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is DataGrid grid && grid.SelectedItem is QuarantineItemViewModel item)
        {
            ViewModel.SelectedQuarantineId = item.Id;
        }
    }
}
