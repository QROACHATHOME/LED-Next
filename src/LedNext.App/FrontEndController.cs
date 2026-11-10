using System.IO;
using System.Windows;
using System.Windows.Threading;
using LedNext.App.ViewModels;
using LedNext.Core;
using LedNext.Core.FrontEnd;
using LedNext.Core.Games;
using LedNext.Core.Lighting;
using LedNext.FrontEnd;

namespace LedNext.App;

/// <summary>
/// Lights the panel from LaunchBox / BigBox: listens on the pipe, runs <see cref="FrontEndSession"/>,
/// resolves the game like the Games tab does and hands the lights to the engine. The last front-end
/// event wins over a test pattern or a Games-tab preview, and the other way round.
/// </summary>
public sealed class FrontEndController : IDisposable
{
    private readonly AppSession _session;
    private readonly MainViewModel _main;
    private readonly Dispatcher _dispatcher;
    private readonly FrontEndSession _state = new();
    private readonly FrontEndServer _server = new();

    public FrontEndController(AppSession session, MainViewModel main)
    {
        _session = session;
        _main = main;
        _dispatcher = Application.Current.Dispatcher;
        _server.MessageReceived += m => _dispatcher.BeginInvoke(() => Handle(m));
    }

    /// <summary>Raised on the UI thread when a front end asks for the window ("show").</summary>
    public event Action? ShowRequested;

    /// <summary>False if another LED-Next already listens (it owns the boards).</summary>
    public bool Start()
    {
        try
        {
            _server.Start();
            _main.FrontEndStatus = "Front end: waiting for LaunchBox / BigBox";
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn($"Another LED-Next is already listening for front ends ({ex.Message}).");
            return false;
        }
    }

    private void Handle(FrontEndMessage m)
    {
        if (m.Event != FrontEndEvents.Select) AppLog.Info($"Front end: {m}");
        switch (_state.Handle(m))
        {
            case FrontEndAction.LightGame light:
                LightGame(light.Game, light.Playing);
                break;
            case FrontEndAction.AllOff off:
                _main.SetSource(new AllOff());
                _main.FrontEndStatus = $"Front end: {off.Reason}";
                AppLog.Info($"{off.Reason}: all lights off.");
                break;
            case FrontEndAction.ShowWindow:
                ShowRequested?.Invoke();
                break;
        }
    }

    private void LightGame(FrontEndMessage g, bool playing)
    {
        var db = _session.Games;
        var config = _session.Config;
        var source = g.Source ?? "Front end";
        var found = db?.FindFrontEndGame(g.GameId, g.Platform, g.Title, g.ApplicationPath, config.Games.LaunchBoxPlatformMap);
        if (db is null || found is not { } f)
        {
            AppLog.Warn($"{source}: no LED-Next game for '{g.Title}' ({g.Platform}); lights unchanged.");
            _main.FrontEndStatus = $"{source}: {g.Title} (not in the game library)";
            return;
        }
        var lights = GameLightResolver.Resolve(db, f.Platform, f.Game, config.ControlMap, config.Panel);
        var verb = playing ? "playing" : "browsing";
        _main.SetSource(new GameLights(lights, $"{source} ({verb}): {g.Title}"));
        _main.FrontEndStatus = $"{source} · {verb} {g.Title}";
        // Selections arrive while scrolling; log them briefly, launches in full.
        if (playing) AppLog.Info($"{source}: playing '{g.Title}' → {f.Platform}/{f.Game} ({f.How}); {lights.From}.");
        else AppLog.Info($"{source}: '{g.Title}' → {f.Platform}/{f.Game} ({f.How}).");
    }

    public void Dispose() => _server.Dispose();
}
