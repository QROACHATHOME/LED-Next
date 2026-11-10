using System.Diagnostics;
using System.IO;
using System.Text.Json;
using LedNext.FrontEnd;
using Unbroken.LaunchBox.Plugins.Data;

namespace LedNext.LaunchBox;

/// <summary>Settings next to the plugin DLL (LedNext.LaunchBox.json), written by the install script.</summary>
public sealed class PluginSettings
{
    /// <summary>Full path of LED-Next.exe; started minimized when LaunchBox/BigBox start and it isn't running.</summary>
    public string? LedNextExe { get; set; }
    public bool StartLedNext { get; set; } = true;
    /// <summary>Watch LaunchBox's selection (LaunchBox window; BigBox uses the theme element if a theme has it).</summary>
    public bool WatchSelection { get; set; } = true;
    public int WatchIntervalMs { get; set; } = 150;
}

/// <summary>
/// Shared by every plugin class LaunchBox creates (it may create each more than once): one pipe client,
/// one log, one LED-Next start. Everything is guarded, because an exception here would surface in
/// LaunchBox / BigBox.
/// </summary>
public static class PluginHost
{
    private static readonly object Lock = new();
    private static readonly object LogLock = new(); // separate: the client thread logs while Shutdown holds Lock
    private static FrontEndClient? _client;
    private static PluginSettings _settings = new();
    private static string? _logPath;
    private static DateTime _lastStartAttempt = DateTime.MinValue;
    private static SelectionWatcher? _watcher;

    public static string Source { get; } = Process.GetCurrentProcess().ProcessName.Contains("BigBox", StringComparison.OrdinalIgnoreCase)
        ? "BigBox" : "LaunchBox";

    public static string PluginFolder => Path.GetDirectoryName(typeof(PluginHost).Assembly.Location) ?? AppContext.BaseDirectory;

    public static PluginSettings Settings { get { EnsureStarted(); return _settings; } }

    /// <summary>Starts the client (and LED-Next if needed). Safe to call any number of times.</summary>
    public static void EnsureStarted()
    {
        lock (Lock)
        {
            if (_client is not null) return;
            try
            {
                _logPath = Path.Combine(PluginFolder, "LedNext.LaunchBox.log");
                TrimLog();
                _settings = LoadSettings();
                Log($"{Source} plugin loaded (LED-Next front-end protocol, pipe '{FrontEndProtocol.PipeName}'). " +
                    $"LED-Next: {_settings.LedNextExe ?? "(not set)"}; start it: {_settings.StartLedNext}.");
                var client = new FrontEndClient();
                client.Log += Log;
                client.ConnectFailed += StartLedNextIfNeeded;
                _client = client;
            }
            catch (Exception ex)
            {
                Log($"Plugin start failed: {ex}");
            }
        }
    }

    public static void Send(string ev, IGame? game = null)
    {
        try
        {
            EnsureStarted();
            _client?.Send(game is null
                ? new FrontEndMessage(ev, Source)
                : new FrontEndMessage(ev, Source, game.Id, game.Title, game.Platform, game.ApplicationPath));
            if (ev != FrontEndEvents.Select) Log($"Sent {ev}{(game is null ? "" : $": {game.Title} ({game.Platform})")}");
        }
        catch (Exception ex)
        {
            Log($"Sending {ev} failed: {ex.Message}");
        }
    }

    private static readonly object SelectionLock = new();
    private static string? _lastSelectedId;
    private static readonly HashSet<string> SelectionSources = new();

    /// <summary>
    /// Sends "select" unless this game was the last one sent. The watcher, the SelectionChanged event and a
    /// BigBox theme element can all report the same change; the first one wins. The first selection from
    /// each source is logged, to show which hooks LaunchBox / BigBox actually call.
    /// </summary>
    public static void SendSelection(IGame game, string via)
    {
        try
        {
            lock (SelectionLock)
            {
                if (SelectionSources.Add(via)) Log($"First selection via {via}: {game.Title} ({game.Platform})");
                if (game.Id == _lastSelectedId) return;
                _lastSelectedId = game.Id;
            }
            Send(FrontEndEvents.Select, game);
        }
        catch (Exception ex)
        {
            Log($"Selection via {via} failed: {ex.Message}");
        }
    }

    /// <summary>Reads LaunchBox's selected game and sends it (for the SelectionChanged system event).</summary>
    public static void SendSelectedGame(string via)
    {
        try
        {
            var games = Unbroken.LaunchBox.Plugins.PluginHelper.StateManager?.GetAllSelectedGames();
            if (games is { Length: > 0 } && games[0]?.Id is not null) SendSelection(games[0], via);
        }
        catch (Exception ex)
        {
            Log($"Reading the selected game ({via}) failed: {ex.Message}");
        }
    }

    public static void StartSelectionWatcher()
    {
        try
        {
            EnsureStarted();
            if (!_settings.WatchSelection || _watcher is not null) return;
            _watcher = SelectionWatcher.TryStart(TimeSpan.FromMilliseconds(Math.Max(50, _settings.WatchIntervalMs)));
        }
        catch (Exception ex)
        {
            Log($"Selection watcher not started: {ex.Message}");
        }
    }

    public static void Shutdown()
    {
        try
        {
            Send(FrontEndEvents.Closing);
            _watcher?.Stop();
            _watcher = null;
            lock (Lock)
            {
                _client?.Dispose(TimeSpan.FromMilliseconds(500));
                _client = null;
            }
            Log($"{Source} closing.");
        }
        catch (Exception ex)
        {
            Log($"Shutdown: {ex.Message}");
        }
    }

    private static void StartLedNextIfNeeded()
    {
        try
        {
            var exe = _settings.LedNextExe;
            if (!_settings.StartLedNext || string.IsNullOrWhiteSpace(exe)) return;
            if (DateTime.UtcNow - _lastStartAttempt < TimeSpan.FromSeconds(30)) return;
            _lastStartAttempt = DateTime.UtcNow;
            if (Process.GetProcessesByName("LED-Next").Length > 0) return; // starting up; it'll listen soon
            if (!File.Exists(exe)) { Log($"LED-Next not found at {exe}; not started."); return; }
            Process.Start(new ProcessStartInfo(exe, "--minimized")
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(exe)!,
            });
            Log($"Started LED-Next minimized: {exe}");
        }
        catch (Exception ex)
        {
            Log($"Could not start LED-Next: {ex.Message}");
        }
    }

    private static PluginSettings LoadSettings()
    {
        var path = Path.Combine(PluginFolder, "LedNext.LaunchBox.json");
        if (!File.Exists(path)) { Log($"No {Path.GetFileName(path)}; using defaults."); return new PluginSettings(); }
        try
        {
            return JsonSerializer.Deserialize<PluginSettings>(File.ReadAllText(path),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip }) ?? new();
        }
        catch (Exception ex)
        {
            Log($"Could not read {path}: {ex.Message}; using defaults.");
            return new PluginSettings();
        }
    }

    public static void Log(string message)
    {
        try
        {
            if (_logPath is null) return;
            lock (LogLock) File.AppendAllText(_logPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{Source}] {message}{Environment.NewLine}");
        }
        catch { /* never let logging break LaunchBox */ }
    }

    private static void TrimLog()
    {
        try
        {
            if (_logPath is not null && File.Exists(_logPath) && new FileInfo(_logPath).Length > 1_000_000)
                File.Delete(_logPath);
        }
        catch { /* keep going */ }
    }
}
