using System.Windows;
using System.Windows.Threading;
using LedNext.FrontEnd;
using Unbroken.LaunchBox.Plugins;

namespace LedNext.LaunchBox;

/// <summary>
/// Polls LaunchBox's selected game (PluginHelper.StateManager) on the UI thread and sends a "select"
/// when it changes. A few dictionary lookups every 150 ms; stops itself if the API isn't available
/// (e.g. in BigBox, which reports selection through <see cref="LedNextThemeElement"/> instead).
/// </summary>
public sealed class SelectionWatcher
{
    private readonly DispatcherTimer _timer;
    private string? _lastId;
    private int _failures;

    private SelectionWatcher(Dispatcher dispatcher, TimeSpan interval)
    {
        _timer = new DispatcherTimer(interval, DispatcherPriority.Background, (_, _) => Poll(), dispatcher);
    }

    public static SelectionWatcher? TryStart(TimeSpan interval)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null) { PluginHost.Log("No WPF dispatcher; selection is not watched."); return null; }
        SelectionWatcher? w = null;
        dispatcher.Invoke(() => { w = new SelectionWatcher(dispatcher, interval); w._timer.Start(); });
        PluginHost.Log($"Watching the selected game every {interval.TotalMilliseconds:0} ms.");
        return w;
    }

    public void Stop()
    {
        try { _timer.Dispatcher.BeginInvoke(() => _timer.Stop()); } catch { /* shutting down */ }
    }

    private void Poll()
    {
        try
        {
            var selection = PluginHost.ReadSelection();
            if (selection.Key is null || selection.Key == _lastId) return;
            _lastId = selection.Key;
            PluginHost.SendSelection(selection, "watcher");
        }
        catch (Exception ex)
        {
            if (++_failures < 3) return;
            _timer.Stop();
            PluginHost.Log($"Selection watcher stopped after repeated errors ({ex.GetType().Name}: {ex.Message}).");
        }
    }
}

