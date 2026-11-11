using System.IO;
using System.Windows;
using LedNext.App.ViewModels;
using LedNext.Core;
using LedNext.Core.Games;
using LedNext.Core.Lighting;
using LedNext.Core.Outputs;

namespace LedNext.App.Lamps;

/// <summary>
/// Live lamps from emulators: while a game runs, its outputs (MAME's LED0 / LAMPn…) light the panel controls
/// LEDBlinky's MameOutputs.ini maps them to, on top of the game's lights (<see cref="LampOverlay"/>). All on the
/// UI thread; the engine reads the overlay on its own thread.
/// </summary>
public sealed class LampController : IDisposable
{
    private const string Mame = "MAME";
    private readonly AppSession _session;
    private readonly MainViewModel _main;
    private readonly List<IEmulatorOutputs> _sources = new();
    private MameOutputSession? _game;
    private string? _via;
    private LampOverlay? _overlay;
    /// <summary>What the panel showed before the lamps (put back when the game ends, if nothing replaced it).</summary>
    private ILightSource? _before;
    private ControlMap? _map;
    private readonly HashSet<string> _seenOutputs = new(StringComparer.OrdinalIgnoreCase);

    public LampController(AppSession session, MainViewModel main)
    {
        _session = session;
        _main = main;
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

    private void OnStarted(string rom, string via)
    {
        var map = LoadMap();
        _game = new MameOutputSession(map, rom, RomChain(rom));
        _via = via;
        _overlay = null;
        _before = null;
        _seenOutputs.Clear();
        _map = _session.ControlMapFor(Mame, rom).Map;
        AppLog.Info($"Lamps: {rom} started ({via}); outputs mapped by {map.Source} [{_game.Section}].");
        _main.LampStatus = $"Lamps: {rom}";
    }

    private void OnStopped(string via)
    {
        if (_game is null || via != _via) return;
        var outputs = string.Join(", ", _game.Values.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase));
        AppLog.Info($"Lamps: {_game.Rom} ended. Outputs it reported: {(outputs.Length == 0 ? "none" : outputs)}.");
        if (_overlay is not null && ReferenceEquals(_session.Engine.Source, _overlay)) _main.SetSource(_before ?? _overlay.Base);
        _game = null;
        _overlay = null;
        _before = null;
        _via = null;
        _main.LampStatus = null;
    }

    private void OnOutput(string name, int value)
    {
        if (_game is null) return;
        // Like LEDBlinky's MAMEOutputTest: each output a game uses is logged once, with what it drives.
        if (_seenOutputs.Add(name))
        {
            var drives = LoadMapCached().ControlsFor(_game.Section, name);
            AppLog.Info($"Lamps: {_game.Rom} reports {name}" + (drives.Count > 0 ? $" → {string.Join(", ", drives)}" : " (not mapped)"));
        }
        var changes = _game.Update(name, value);
        if (changes.Count == 0) return;
        var overlay = EnsureOverlay();
        foreach (var (control, on) in changes) overlay.SetLamp(control, on);
        _main.LampStatus = $"Lamps: {_game.Rom} · {overlay.LitLamps} on";
        _session.Engine.Refresh();
    }

    /// <summary>
    /// The overlay on what the panel shows now: the running game's lights from the front end, else the rom's own
    /// lights from the library. Wraps the current source again if something replaced it while the game runs.
    /// </summary>
    private LampOverlay EnsureOverlay()
    {
        var current = _session.Engine.Source;
        if (_overlay is not null && ReferenceEquals(current, _overlay)) return _overlay;

        var baseSource = current is LampOverlay lo ? lo.Base : current;
        _before = baseSource;
        if (!ShowsRom(baseSource) && RomLights() is { } romLights) baseSource = romLights;

        var settings = _session.Config.Lamps;
        var map = _map ?? _session.Config.ControlMap;
        var panel = _session.Config.Panel;
        var overlay = new LampOverlay(baseSource, control => map.Lookup(control, panel).Buttons, Rgb.White, settings.StartLampsOnButton1);
        if (_overlay is not null) overlay.CopyLampsFrom(_overlay);
        _overlay = overlay;
        _main.SetSource(overlay);
        return overlay;
    }

    /// <summary>True when the source already shows this game (lit by the front end or the Games tab).</summary>
    private bool ShowsRom(ILightSource source) =>
        _game is not null && source is GameLights g && g.Lights.Game is { } name &&
        RomChain(_game.Rom).Contains(name, StringComparer.OrdinalIgnoreCase);

    /// <summary>The rom's lights from the MAME library, or null when LED-Next doesn't know it (e.g. a TeknoParrot name).</summary>
    private GameLights? RomLights()
    {
        var db = _session.Games;
        if (db is null || _game is null || db.GetGame(Mame, _game.Rom) is null) return null;
        var lights = GameLightResolver.Resolve(db, Mame, _game.Rom, _map ?? _session.Config.ControlMap, _session.Config.Panel);
        return new GameLights(lights, $"{_via}: {_game.Rom}");
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
