using System;
using System.Windows;

namespace AV.App;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            var ex = args.ExceptionObject as Exception;
            MessageBox.Show($"Fatal error: {ex?.Message}\n\n{ex?.StackTrace}",
                "Xentra AV", MessageBoxButton.OK, MessageBoxImage.Error);
        };

        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show($"Error: {args.Exception.Message}\n\n{args.Exception.StackTrace}",
                "Xentra AV", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
    }
}
