using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using AV.App.ViewModels;

namespace AV.App;

public partial class MainWindow : System.Windows.Window
{
    private MainViewModel ViewModel => (MainViewModel)DataContext;

    public MainWindow()
    {
        InitializeComponent();
        UpdateNav(ViewModel.CurrentView);
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void NavDashboard_Click(object sender, RoutedEventArgs e) => ShowView(ViewType.Dashboard);
    private void NavScan_Click(object sender, RoutedEventArgs e) => ShowView(ViewType.Scan);

    private void NavQuarantine_Click(object sender, RoutedEventArgs e)
    {
        ShowView(ViewType.Quarantine);
        _ = ViewModel.LoadQuarantineAsync();
    }

    private void ShowView(ViewType vt)
    {
        ViewModel.CurrentView = vt;
        UpdateNav(vt);
        var panel = vt switch
        {
            ViewType.Scan => ScanView,
            ViewType.Quarantine => QuarantineView,
            _ => DashboardView
        };
        AnimateView(panel);
    }

    private void UpdateNav(ViewType vt)
    {
        DashDash.Visibility = ScanDash.Visibility = QuarDash.Visibility = Visibility.Collapsed;
        var muted = (Brush)FindResource("TextMuted");
        var accent = (Brush)FindResource("Accent");
        var primary = (Brush)FindResource("TextPrimary");

        NavIconDashboard.Stroke = NavIconScan.Stroke = NavIconQuarantine.Stroke = muted;
        NavDashboard.Foreground = NavScan.Foreground = NavQuarantine.Foreground = muted;

        switch (vt)
        {
            case ViewType.Dashboard:
                DashDash.Visibility = Visibility.Visible;
                NavIconDashboard.Stroke = accent;
                NavDashboard.Foreground = primary;
                break;
            case ViewType.Scan:
                ScanDash.Visibility = Visibility.Visible;
                NavIconScan.Stroke = accent;
                NavScan.Foreground = primary;
                break;
            case ViewType.Quarantine:
                QuarDash.Visibility = Visibility.Visible;
                NavIconQuarantine.Stroke = accent;
                NavQuarantine.Foreground = primary;
                break;
        }
    }

    private void AnimateView(Grid panel)
    {
        panel.Opacity = 0;
        var tt = new TranslateTransform(0, 14);
        panel.RenderTransform = tt;
        var sb = new Storyboard();
        var fade = new DoubleAnimation(1, TimeSpan.FromMilliseconds(240))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(fade, panel);
        Storyboard.SetTargetProperty(fade, new PropertyPath(UIElement.OpacityProperty));
        var slide = new DoubleAnimation(0, TimeSpan.FromMilliseconds(240))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(slide, tt);
        Storyboard.SetTargetProperty(slide, new PropertyPath(TranslateTransform.YProperty));
        sb.Children.Add(fade);
        sb.Children.Add(slide);
        sb.Begin();
    }

    private async void ScanFileButton_Click(object sender, RoutedEventArgs e) => await ViewModel.ScanFileAsync();

    private async void ScanDirectoryButton_Click(object sender, RoutedEventArgs e) => await ViewModel.ScanDirectoryAsync();

    private async void RefreshQuarantine_Click(object sender, RoutedEventArgs e) => await ViewModel.LoadQuarantineAsync();

    private async void RestoreCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string id })
        {
            ViewModel.SelectedQuarantineId = id;
            await ViewModel.RestoreSelectedAsync();
        }
    }

    private async void DeleteCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string id })
        {
            ViewModel.SelectedQuarantineId = id;
            await ViewModel.DeleteSelectedAsync();
        }
    }
}
