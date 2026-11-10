using System.Windows;
using System.Windows.Threading;
using LedNext.App.ViewModels;
using LedNext.Core;
using LedNext.FrontEnd;

namespace LedNext.App;

public partial class App : Application
{
    private const string SingleInstanceName = @"Local\LedNext.App.SingleInstance";
    private AppSession? _session;
    private FrontEndController? _frontEnd;
    private Mutex? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;
        bool minimized = e.Args.Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase));

        // Only one LED-Next may drive the boards. A second start (the user, or the LaunchBox plugin
        // starting it while it already runs) asks the first one to show itself and exits, before
        // touching any board.
        _singleInstance = new Mutex(initiallyOwned: true, SingleInstanceName, out bool first);
        if (!first)
        {
            _singleInstance.Dispose();
            _singleInstance = null;
            if (!minimized)
            {
                using var client = new FrontEndClient(connectTimeoutMs: 1000);
                client.Send(new FrontEndMessage(FrontEndEvents.Show, "LED-Next"));
                client.Dispose(TimeSpan.FromSeconds(2));
            }
            Shutdown();
            return;
        }

        _session = new AppSession();
        var vm = new MainViewModel(_session);
        var window = new MainWindow(System.IO.Path.Combine(_session.ConfigDirectory, "window.json")) { DataContext = vm };
        _restoreState = window.WindowState;
        // Minimizing hides the window to the tray icon; the lights keep running, the on-screen preview pauses.
        window.StateChanged += (_, _) =>
        {
            vm.PreviewVisible = window.WindowState != WindowState.Minimized;
            if (window.WindowState == WindowState.Minimized) window.Hide();
            else _restoreState = window.WindowState;
        };
        _tray = new TrayIcon(open: () => BringToFront(window), exit: () => window.Close());
        if (minimized)
        {
            window.WindowState = WindowState.Minimized;
            vm.PreviewVisible = false;
            AppLog.Info("Started minimized to the tray (by a front-end plugin).");
        }
        window.Show();
        if (minimized) window.Hide();

        _frontEnd = new FrontEndController(_session, vm);
        _frontEnd.ShowRequested += () => BringToFront(window);
        _frontEnd.Start();
    }

    private TrayIcon? _tray;
    private WindowState _restoreState = WindowState.Normal;

    private void BringToFront(Window window)
    {
        window.Show();
        if (window.WindowState == WindowState.Minimized) window.WindowState = _restoreState;
        window.Activate();
        // Activate alone may only flash the taskbar button when another app has the focus.
        window.Topmost = true;
        window.Topmost = false;
        window.Focus();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _frontEnd?.Dispose();
        _session?.Dispose();
        if (_singleInstance is not null)
        {
            try { _singleInstance.ReleaseMutex(); } catch (ApplicationException) { /* not owned */ }
            _singleInstance.Dispose();
        }
        base.OnExit(e);
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLog.Error("Unhandled UI exception", e.Exception);
        MessageBox.Show(e.Exception.Message, "LED-Next error", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
