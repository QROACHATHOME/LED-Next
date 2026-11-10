using System.Windows;
using System.Windows.Threading;
using LedNext.App.ViewModels;
using LedNext.Core;

namespace LedNext.App;

public partial class App : Application
{
    private AppSession? _session;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;

        _session = new AppSession();
        var window = new MainWindow(System.IO.Path.Combine(_session.ConfigDirectory, "window.json")) { DataContext = new MainViewModel(_session) };
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _session?.Dispose();
        base.OnExit(e);
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLog.Error("Unhandled UI exception", e.Exception);
        MessageBox.Show(e.Exception.Message, "LED-Next error", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
