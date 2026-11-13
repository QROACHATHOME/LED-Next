using System.IO;
using System.Windows;
using LedNext.App.ViewModels;
using LedNext.Core;
using LedNext.Core.Games;
using LedNext.Core.Lighting;
using LedNext.Core.Outputs;

namespace LedNext.App.Lamps;

/// <summary>
/// Live lamps from emulators, and who owns the panel while games run. The emulator adapters and the front end
/// (<see cref="FrontEndController"/>) both feed one <see cref="LampLifecycle"/>, which decides what is shown;
/// this class applies its decisions and maps outputs to controls (LEDBlinky's MameOutputs.ini). All on the UI
/// thread; the engine reads the overlay on its own thread.
/// </summary>
public sealed class LampController : IDisposable
{
    private const string Mame = "MAME";
    private readonly AppSession _session;
    private readonly MainViewModel _main;
    private readonly List<IEmulatorOutputs> _sources = new();
    private MameOutputSession? _game;
    private ControlMap? _map;
    private readonly HashSet<string> _seenOutputs = new(StringComparer.OrdinalIgnoreCase);

    public LampController(AppSession session, MainViewModel main)
    {
        _session = session;
        _main = main;
        Lifecycle = new LampLifecycle(MakeOverlay);
        // Anything else that changes the panel (test patterns, Games-tab previews) is the user's pick.
        _main.SourceSet += source => Lifecycle.SourceShown(source);
    }

    public LampLifecycle Lifecycle { get; }

    /// <summary>Shows what the lifecycle decided. Returns the source it set, or null.</summary>
    public ILightSource? Apply(LampStep step)
    {
        if (step.Show is { } source)
        {
            _main.SetSource(source);
            return source;
        }
        if (step.Redraw) _session.Engine.Refresh();
        return null;
    }

    public void Start()
    {
        var settings = _session.Config.Lamps;
        if (!settings.Enabled) { AppLog.Info("Lamps: off (cabinet.json lamps.enabled)."); return; }
        if (settings.WindowMessages) _sources.Add(new MameWindowOutputs());
        if (settings.Network) _sources.Add(new NetworkOutputs(settings.NetworkPort, Application.Current.Dispatcher));
        foreach (var s in _sources)
        {
            s.Started += OnStarted;
            s.Stopped += OnStopped;
            s.Output += OnOutput;
            try { s.Start(); }
            catch (Exception ex) { AppLog.Error($"Lamps: {s.Via} could not start", ex); }
        }
    }

    private bool GameRunning => Lifecycle.Phase is LampPhase.Playing or LampPhase.PlayingWithLamps;

    private void OnStarted(string rom, string via)
    {
        if (GameRunning && Lifecycle.Via != via)
        {
            Lifecycle.GameStarted(rom, via, _session.Engine.Source, null, _ => true); // another adapter owns the game: logged, ignored
            return;
        }
        var map = LoadMap();
        _game = new MameOutputSession(map, rom, RomChain(rom));
        _seenOutputs.Clear();
        _map = _session.ControlMapFor(Mame, rom).Map;
        AppLog.Info($"Lamps: {rom} started ({via}); outputs mapped by {map.Source} [{_game.Section}].");
        _main.LampStatus = $"Lamps: {rom}";
        Apply(Lifecycle.GameStarted(rom, via, _session.Engine.Source, RomLights(rom, via), ShowsRom));
    }

    private void OnStopped(string via)
    {
        if (_game is null || via != Lifecycle.Via) return;
        LogOutputs();
        Apply(Lifecycle.GameEnded(via));
        EndSession();
    }

    private void OnOutput(string name, int value)
    {
        if (_game is null) return;
        if (!GameRunning) { AfterFrontEnd(); return; } // the front end ended the game (exit, closing)
        // Like LEDBlinky's MAMEOutputTest: each output a game uses is logged once, with what it drives.
        if (_seenOutputs.Add(name))
        {
            var drives = LoadMapCached().ControlsFor(_game.Section, name);
            AppLog.Info($"Lamps: {_game.Rom} reports {name}" + (drives.Count > 0 ? $" → {string.Join(", ", drives)}" : " (not mapped)"));
        }
        var changes = _game.Update(name, value);
        if (changes.Count == 0) return;
        Apply(Lifecycle.Lamps(changes));
        _main.LampStatus = $"Lamps: {_game.Rom} · {Lifecycle.Overlay?.LitLamps ?? 0} on";
    }

    /// <summary>Called after a front-end event: if it ended the game (exit, closing), drop the output session.</summary>
    public void AfterFrontEnd()
    {
        if (_game is null || GameRunning) return;
        LogOutputs();
        EndSession();
    }

    private void LogOutputs()
    {
        if (_game is null) return;
        var outputs = string.Join(", ", _game.Values.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase));
        AppLog.Info($"Lamps: {_game.Rom} outputs it reported: {(outputs.Length == 0 ? "none" : outputs)}.");
    }

    private void EndSession()
    {
        _game = null;
        _map = null;
        _main.LampStatus = null;
    }

    private LampOverlay MakeOverlay(ILightSource baseSource)
    {
        var settings = _session.Config.Lamps;
        var map = _map ?? _session.Config.ControlMap;
        var panel = _session.Config.Panel;
        return new LampOverlay(baseSource, control => map.Lookup(control, panel).Buttons, Rgb.White, settings.StartLampsOnButton1);
    }

    /// <summary>True when the source already shows this game (lit by the front end or the Games tab).</summary>
    private bool ShowsRom(ILightSource source) =>
        _game is not null && source is GameLights g && g.Lights.Game is { } name &&
        RomChain(_game.Rom).Contains(name, StringComparer.OrdinalIgnoreCase);

    /// <summary>The rom's lights from the MAME library, or null when LED-Next doesn't know it (e.g. a TeknoParrot name).</summary>
    private GameLights? RomLights(string rom, string via)
    {
        var db = _session.Games;
        if (db is null || db.GetGame(Mame, rom) is null) return null;
        var lights = GameLightResolver.Resolve(db, Mame, rom, _map ?? _session.Config.ControlMap, _session.Config.Panel);
        return new GameLights(lights, $"{via}: {rom}");
    }

    private List<string> RomChain(string rom)
    {
        var chain = new List<string>();
        var db = _session.Games;
        for (string? name = rom; name is not null && chain.Count < 8 && !chain.Contains(name, StringComparer.OrdinalIgnoreCase);
             name = db?.GetGame(Mame, name)?.Parent)
            chain.Add(name);
        return chain;
    }

    private MameOutputMap? _cachedMap;
    private MameOutputMap LoadMapCached() => _cachedMap ??= LoadMap();

    /// <summary>LEDBlinky's MameOutputs.ini (read again for every game, so edits apply), else the built-in default.</summary>
    private MameOutputMap LoadMap()
    {
        var folder = _session.Config.Games.LedBlinkyFolder;
        MameOutputMap? map = null;
        if (!string.IsNullOrEmpty(folder))
        {
            try { map = MameOutputMap.Load(Path.Combine(folder, "MameOutputs.ini")); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Warn($"Lamps: could not read MameOutputs.ini ({ex.Message}); using LED0 → P1_START, LED1 → P2_START.");
            }
        }
        _cachedMap = map ?? MameOutputMap.BuiltIn();
        return _cachedMap;
    }

    public void Dispose()
    {
        foreach (var s in _sources)
        {
            try { s.Dispose(); }
            catch (Exception ex) { AppLog.Error($"Lamps: closing {s.Via}", ex); }
        }
        _sources.Clear();
    }
}
