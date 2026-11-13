using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using LedNext.App.ViewModels;
using LedNext.Core;
using LedNext.Core.FrontEnd;
using LedNext.Core.Games;
using LedNext.Core.Lighting;
using LedNext.Core.Outputs;
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
    private static readonly Stopwatch Clock = Stopwatch.StartNew();

    /// <summary>The front-end light source waiting for its first frame on the boards, with its timings.</summary>
    private sealed record Timing(ILightSource Source, string What, long? PipeMs, TimeSpan Received, TimeSpan Handled, TimeSpan Resolved);
    private volatile Timing? _timing;

    private readonly Lamps.LampController _lamps;

    public FrontEndController(AppSession session, MainViewModel main, Lamps.LampController lamps)
    {
        _session = session;
        _main = main;
        _lamps = lamps;
        _dispatcher = Application.Current.Dispatcher;
        _server.MessageReceived += m =>
        {
            var received = Clock.Elapsed;
            long? pipeMs = m.SentAtMs is { } sent ? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - sent : null;
            _dispatcher.BeginInvoke(() => Handle(m, received, pipeMs));
        };
        _session.Engine.SourceShown += OnSourceShown;
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

    private void Handle(FrontEndMessage m, TimeSpan received, long? pipeMs)
    {
        var handled = Clock.Elapsed;
        if (m.Event != FrontEndEvents.Select) AppLog.Info($"Front end: {m}");
        ILightSource? lights = null;
        bool launch = false, closed = false;
        string what = m.Title ?? m.Platform ?? m.Event;
        switch (_state.Handle(m))
        {
            case FrontEndAction.LightGame light:
                lights = LightGame(light.Game, light.Playing);
                launch = light.Playing;
                what = light.Game.Title ?? what;
                break;
            case FrontEndAction.LightPlatform platform:
                lights = LightPlatform(platform.Selection);
                what = $"platform {platform.Selection.Platform}";
                break;
            case FrontEndAction.AllOff off:
                lights = new AllOff();
                closed = true;
                _main.FrontEndStatus = $"Front end: {off.Reason}";
                AppLog.Info($"{off.Reason}: all lights off.");
                break;
            case FrontEndAction.ShowWindow:
                ShowRequested?.Invoke();
                return;
        }

        // The lamp lifecycle decides what the panel shows (a running game keeps it; an exit ends the game).
        var life = _lamps.Lifecycle;
        LampStep step;
        if (closed) step = life.FrontEndClosed(lights!);
        else if (m.Event == FrontEndEvents.Exit) step = life.FrontEndExit(lights);
        else if (lights is null) return;
        else step = launch ? life.FrontEndLaunch(lights) : life.FrontEndSelection(lights);

        // Before showing it: the engine renders a new source at once and may report it before Apply returns.
        if (step.Show is { } shown && !closed) _timing = new Timing(shown, what, pipeMs, received, handled, Clock.Elapsed);
        _lamps.Apply(step);
        _lamps.AfterFrontEnd();
    }

    /// <summary>
    /// Engine thread: the first frame of a front-end source reached the boards. Logs where the time went:
    /// pipe (plugin → LED-Next), wait (for the UI thread), lookup (game + lights), boards (until written).
    /// </summary>
    private void OnSourceShown(ILightSource source, TimeSpan sinceSet, TimeSpan write)
    {
        var t = _timing;
        if (t is null || !ReferenceEquals(t.Source, source)) return;
        _timing = null;
        var wait = (t.Handled - t.Received).TotalMilliseconds;
        var lookup = (t.Resolved - t.Handled).TotalMilliseconds;
        var total = (t.PipeMs ?? 0) + (Clock.Elapsed - t.Received).TotalMilliseconds;
        AppLog.Info($"Timing '{t.What}': pipe {(t.PipeMs is { } p ? $"{p} ms" : "?")}, wait {wait:0} ms, lookup {lookup:0} ms, " +
                    $"boards {sinceSet.TotalMilliseconds:0} ms (write {write.TotalMilliseconds:0} ms); total {total:0} ms.");
    }

    /// <summary>The platform's default lights (not yet shown; the caller sets the source).</summary>
    private ILightSource? LightPlatform(FrontEndMessage p)
    {
        var db = _session.Games;
        var config = _session.Config;
        var source = p.Source ?? "Front end";
        var found = db?.FindFrontEndPlatform(p.Platform, config.Games.LaunchBoxPlatformMap);
        if (db is null || found is not { } f)
        {
            AppLog.Warn($"{source}: no LED-Next platform for '{p.Platform}'; lights unchanged.");
            _main.FrontEndStatus = $"{source}: platform {p.Platform} (not in the game library)";
            return null;
        }
        var lights = GameLightResolver.Resolve(db, f.Platform, null, config.ControlMap, config.Panel);
        var light = new GameLights(lights, $"{source} (platform): {p.Platform}");
        _main.FrontEndStatus = $"{source} · platform {p.Platform}";
        AppLog.Info($"{source}: platform '{p.Platform}' → {f.Platform} ({f.How}); {lights.From}.");
        return light;
    }

    /// <summary>The game's lights (not yet shown; the caller sets the source).</summary>
    private ILightSource? LightGame(FrontEndMessage g, bool playing)
    {
        var db = _session.Games;
        var config = _session.Config;
        var source = g.Source ?? "Front end";
        var found = db?.FindFrontEndGame(g.GameId, g.Platform, g.Title, g.ApplicationPath, config.Games.LaunchBoxPlatformMap);
        if (db is null || found is not { } f)
        {
            AppLog.Warn($"{source}: no LED-Next game for '{g.Title}' ({g.Platform}); lights unchanged.");
            _main.FrontEndStatus = $"{source}: {g.Title} (not in the game library)";
            return null;
        }
        // The game's own MAME install may put its buttons on other keys than the cabinet's map assumes.
        var (map, keys) = _session.ControlMapFor(f.Platform, f.Game, g.GameId);
        var lights = GameLightResolver.Resolve(db, f.Platform, f.Game, map, config.Panel);
        if (keys is not null) lights = lights with { From = $"{lights.From}; {keys}" };
        var verb = playing ? "playing" : "browsing";
        var light = new GameLights(lights, $"{source} ({verb}): {g.Title}");
        _main.FrontEndStatus = $"{source} · {verb} {g.Title}";
        // Selections arrive while scrolling; log them briefly, launches in full.
        if (playing) AppLog.Info($"{source}: playing '{g.Title}' → {f.Platform}/{f.Game} ({f.How}); {lights.From}.");
        else AppLog.Info($"{source}: '{g.Title}' → {f.Platform}/{f.Game} ({f.How})" + (keys is null ? "." : $"; {keys}."));
        return light;
    }

    public void Dispose()
    {
        _session.Engine.SourceShown -= OnSourceShown;
        _server.Dispose();
    }
}
