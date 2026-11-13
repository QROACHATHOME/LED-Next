using LedNext.Core.Lighting;

namespace LedNext.Core.Outputs;

/// <summary>Who owns the panel (see TASKS/DESIGN_LAMP_STATES.md).</summary>
public enum LampPhase
{
    /// <summary>No game: the front end's selection (or whatever the user picked) is shown.</summary>
    Browsing,
    /// <summary>The front end launched a game and shows its lights; no emulator has reported it yet.</summary>
    Launching,
    /// <summary>An emulator reported a game; its lights are shown, no lamp has changed yet.</summary>
    Playing,
    /// <summary>The game's lights with the emulator's lamps on top (<see cref="LampOverlay"/>).</summary>
    PlayingWithLamps,
}

/// <summary>What to do after an event: show a new source (or keep the current one), and whether to redraw.</summary>
public readonly record struct LampStep(ILightSource? Show, bool Redraw = false)
{
    public static readonly LampStep None = default;
}

/// <summary>
/// One state machine for the panel while games run, fed by the front end (LaunchBox/BigBox) and by every
/// emulator output adapter. It decides what the engine shows; the App applies it (<c>MainViewModel.SetSource</c>).
/// Pure logic, UI thread only, unit-tested. The rules:
/// <list type="bullet">
/// <item>Front-end selections light the panel while browsing; while a game runs they are remembered, not shown.</item>
/// <item>A front-end launch shows that game's lights (<see cref="LampPhase.Launching"/>). When an emulator then reports
/// its game, those lights stay the base under the lamps, even if the emulator's name for the game differs (logged).</item>
/// <item>A game an emulator reports without a front-end launch (started by hand) shows the rom's own lights when
/// LED-Next knows the rom, else keeps what is shown.</item>
/// <item>The first adapter to report a game owns it: another adapter's start, outputs and stop are ignored until it ends.</item>
/// <item>The game ends on the emulator's stop, or on the front end's exit or closing (Flycast sends no stop). Lamps are
/// cleared, never left on. After the emulator's stop the front end's launch lights stay until its exit.</item>
/// <item>A source the user picks in LED-Next (test pattern, Games-tab preview) wins: lamps keep their state but don't
/// take the panel back, and the game's end doesn't replace it. The next front-end event or game start takes over again.</item>
/// </list>
/// </summary>
public sealed class LampLifecycle(Func<ILightSource, LampOverlay> makeOverlay)
{
    private ILightSource? _frontEnd;   // the front end's latest lights (selection, platform or the launched game)
    private ILightSource? _back;       // shown again when a game the front end didn't launch ends
    private ILightSource? _base;       // the game's lights under the lamps
    private LampOverlay? _overlay;

    public LampPhase Phase { get; private set; } = LampPhase.Browsing;
    /// <summary>The front end says a game is playing (between its launch and exit).</summary>
    public bool Launched { get; private set; }
    /// <summary>The emulator's name for the running game, or null.</summary>
    public string? Game { get; private set; }
    /// <summary>The adapter that reported the game ("network port 8000", "Windows messages").</summary>
    public string? Via { get; private set; }
    /// <summary>The user picked something else in LED-Next while this machine owned the panel.</summary>
    public bool UserOverride { get; private set; }
    /// <summary>The last source this machine asked to show.</summary>
    public ILightSource? Shown { get; private set; }
    public LampOverlay? Overlay => _overlay;

    private bool GameRunning => Phase is LampPhase.Playing or LampPhase.PlayingWithLamps;

    // ---- front end ----------------------------------------------------------------------------------------

    /// <summary>A selection or platform-wheel move (or the selection shown again after an exit).</summary>
    public LampStep FrontEndSelection(ILightSource lights)
    {
        _frontEnd = lights;
        if (GameRunning && !Launched)
        {
            _back = lights; // shown when the hand-started game ends
            return LampStep.None;
        }
        if (Launched) return LampStep.None; // the front end's own session ignores these too
        return Take(lights);
    }

    public LampStep FrontEndLaunch(ILightSource lights)
    {
        if (GameRunning) EndGame("the front end launched another game");
        Launched = true;
        _frontEnd = lights;
        To(LampPhase.Launching, "front end launched a game");
        return Take(lights);
    }

    /// <summary>The front end's exit: the game is over whatever the emulator said. <paramref name="back"/> is the
    /// selection to show again, or null to keep the panel as it is.</summary>
    public LampStep FrontEndExit(ILightSource? back)
    {
        if (GameRunning) EndGame("the front end reported the exit");
        Launched = false;
        if (Phase != LampPhase.Browsing) To(LampPhase.Browsing, "front end exit");
        if (back is null) return LampStep.None;
        _frontEnd = back;
        return Take(back);
    }

    public LampStep FrontEndClosed(ILightSource off)
    {
        if (GameRunning) EndGame("the front end closed");
        Launched = false;
        _frontEnd = null;
        if (Phase != LampPhase.Browsing) To(LampPhase.Browsing, "front end closed");
        return Take(off);
    }

    // ---- emulators ----------------------------------------------------------------------------------------

    /// <summary>
    /// An adapter reports a game. <paramref name="current"/> is what the panel shows now; <paramref name="romLights"/>
    /// the rom's own lights (null when LED-Next doesn't know it); <paramref name="showsGame"/> tells whether a source
    /// shows this game (to log a front end / emulator name mismatch).
    /// </summary>
    public LampStep GameStarted(string game, string via, ILightSource current, ILightSource? romLights, Func<ILightSource, bool> showsGame)
    {
        if (GameRunning && Via != via)
        {
            AppLog.Info($"Lamps: {via} reports '{game}' while {Via} owns '{Game}'; ignored.");
            return LampStep.None;
        }
        if (GameRunning) EndGame($"{via} reported a new game");

        Game = game;
        Via = via;
        _overlay = null;
        UserOverride = false;
        if (Launched && _frontEnd is not null)
        {
            _base = _frontEnd;
            if (!showsGame(_frontEnd))
                AppLog.Info($"Lamps: the front end launched '{_frontEnd.Name}', {via} reports '{game}'; keeping the front end's lights.");
            To(LampPhase.Playing, $"{game} started ({via}), lights from the front end");
            return Take(_base);
        }
        _back = current;
        _base = romLights ?? current;
        To(LampPhase.Playing, $"{game} started ({via}) without a front-end launch, " +
                              (romLights is null ? "keeping the current lights" : "the rom's own lights"));
        return Take(_base);
    }

    /// <summary>Lamp changes of the running game (from <see cref="MameOutputSession.Update"/>).</summary>
    public LampStep Lamps(IReadOnlyCollection<(string Control, bool On)> changes)
    {
        if (!GameRunning || changes.Count == 0 || _base is null) return LampStep.None;
        bool created = _overlay is null;
        _overlay ??= makeOverlay(_base);
        foreach (var (control, on) in changes) _overlay.SetLamp(control, on);
        if (created) To(LampPhase.PlayingWithLamps, $"first lamp of {Game}");
        if (UserOverride) return LampStep.None;
        return created ? Take(_overlay) : new LampStep(null, Redraw: true);
    }

    /// <summary>The emulator's stop (MAME "mame_stop" / MAMEOutputStop, Flycast closing its connection).</summary>
    public LampStep GameEnded(string via)
    {
        if (!GameRunning || via != Via) return LampStep.None;
        EndGame($"{via} reported the end");
        if (Launched && _frontEnd is not null)
        {
            To(LampPhase.Launching, "the front end still shows the game until its exit");
            return UserOverride ? LampStep.None : Take(_frontEnd);
        }
        To(LampPhase.Browsing, "game over");
        var back = _back;
        _back = null;
        return UserOverride || back is null ? LampStep.None : Take(back);
    }

    // ---- everything else ----------------------------------------------------------------------------------

    /// <summary>The panel shows a source; if this machine didn't choose it, the user did.</summary>
    public void SourceShown(ILightSource source)
    {
        if (ReferenceEquals(source, Shown) || UserOverride) return;
        if (Phase == LampPhase.Browsing && !Launched) return; // nothing to protect
        UserOverride = true;
        AppLog.Info($"Lamps: '{source.Name}' was picked in LED-Next; it stays until the next front-end event or game.");
    }

    private LampStep Take(ILightSource source)
    {
        if (!UserOverride && ReferenceEquals(source, Shown)) return LampStep.None; // already on the panel
        UserOverride = false;
        Shown = source;
        return new LampStep(source);
    }

    private void EndGame(string why)
    {
        var lit = _overlay?.LitLamps ?? 0;
        AppLog.Info($"Lamps: {Game} ended ({why}); {lit} lamp{(lit == 1 ? "" : "s")} on, all cleared.");
        Game = null;
        Via = null;
        _overlay = null;
        _base = null;
    }

    private void To(LampPhase phase, string why)
    {
        if (phase == Phase) return;
        AppLog.Info($"Lamps: {Phase} → {phase} ({why}).");
        Phase = phase;
    }
}
