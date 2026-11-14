using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using LedNext.App.Speech;
using LedNext.App.ViewModels;
using LedNext.Core;
using LedNext.Core.Lighting;
using LedNext.Core.Outputs;
using LedNext.Core.Shows;
using LedNext.FrontEnd;

namespace LedNext.App.Shows;

public enum ShowKind { None, Idle, Startup, Shutdown, Test }

/// <summary>
/// Light shows that take the panel for a while: the idle show, the front end's start-up and shut-down shows, and
/// the Animations tab's "Try" buttons. While one plays, front-end selections are remembered (<see cref="Defer"/>)
/// and shown when it ends; a launch, a game an emulator reports, or a source the user picks in LED-Next ends it at
/// once. UI thread only.
/// <para>Idle: once a second the time of the last input is read (<c>GetLastInputInfo</c>: keyboard, which includes
/// I-PAC style encoders, and mouse / trackball; plus joysticks and gamepads through raw input while the show runs)
/// and fed to <see cref="IdleMonitor"/>. BigBox's own attract mode moves the selection without anyone playing, so a
/// selection does not count as input.</para>
/// </summary>
public sealed class ShowController : IDisposable
{
    private const string IdleOwner = "idle", GamePlaying = "a game is playing";
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private readonly AppSession _session;
    private readonly MainViewModel _main;
    private readonly Lamps.LampController _lamps;
    private readonly DispatcherTimer _idleTimer, _endTimer;
    private readonly IdleMonitor _monitor = new();
    private readonly LastInputClock _windowsInput = new();
    private ILightSource? _show, _resume;
    private bool _setting, _frontEndOpen, _rawInput, _audio;
    private TimeSpan _activityAt = TimeSpan.Zero, _idleSince;
    private string? _lastBlock = "";

    public ShowController(AppSession session, MainViewModel main, Lamps.LampController lamps, Dispatcher dispatcher)
    {
        _session = session;
        _main = main;
        _lamps = lamps;
        Audio = new AudioMeter();
        _idleTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => IdleTick(), dispatcher);
        _endTimer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher) { };
        _endTimer.Tick += (_, _) => { _endTimer.Stop(); Finish("its time is up"); };
        _main.SourceSet += OnSourceSet;
        RawInputWatcher.Shared.Input += OnRawInput;
        _idleTimer.Start();
    }

    public AudioMeter Audio { get; }
    public ShowKind Active { get; private set; }
    /// <summary>For the status bar and the Animations tab.</summary>
    public event Action? Changed;

    private IdleSettings Idle => _session.Config.Idle;
    private FrontEndShowSettings FrontEndShows => _session.Config.Shows;

    // ---- front end ------------------------------------------------------------------------------------------

    /// <summary>Every front-end message, before it is handled: tracks whether a front end is open.</summary>
    public void FrontEndEvent(FrontEndMessage m)
    {
        if (m.Event == FrontEndEvents.Show) return;
        _frontEndOpen = m.Event != FrontEndEvents.Closing;
    }

    /// <summary>The front end finished starting: plays the start-up show if one is picked for it.</summary>
    public bool FrontEndStarted(string? source)
    {
        if (!FrontEndShows.AppliesTo(source)) return false;
        var show = LightShows.Startup(FrontEndShows.Startup, FrontEndShows.StartupDuration);
        if (show is null) return false;
        Begin(ShowKind.Startup, show, FrontEndShows.StartupDuration, _session.Engine.Source, $"{source} started");
        return true;
    }

    /// <summary>
    /// The front end closed. With a shut-down show picked for it, plays it and then shows <paramref name="off"/>;
    /// returns false (the caller shows <paramref name="off"/> itself) when there is none.
    /// </summary>
    public bool FrontEndClosing(string? source, ILightSource off)
    {
        if (!FrontEndShows.AppliesTo(source)) return false;
        var previous = Active == ShowKind.None ? _session.Engine.Source : _resume;
        var show = LightShows.Shutdown(FrontEndShows.Shutdown, FrontEndShows.ShutdownDuration, previous);
        if (show is null) return false;
        Begin(ShowKind.Shutdown, show, FrontEndShows.ShutdownDuration, off, $"{source} closed");
        return true;
    }

    /// <summary>
    /// While a show plays, a front-end selection is remembered instead of shown: true when the caller must not show it.
    /// </summary>
    public bool Defer(ILightSource lights)
    {
        if (Active == ShowKind.None) return false;
        _resume = lights;
        return true;
    }

    // ---- try buttons ----------------------------------------------------------------------------------------

    public string TryIdle()
    {
        var resume = ResumeTarget();
        if (Active != ShowKind.None) Cleanup(); // before the new show takes the sound meter
        var show = MakeIdleShow();
        Begin(ShowKind.Test, show, TimeSpan.FromSeconds(20), resume, "tried in the Animations tab");
        return $"Playing {show.Name} for 20 s" + (show.ReactsToAudio ? " (following the sound)." : ".");
    }

    public string TryStartup()
    {
        var show = LightShows.Startup(FrontEndShows.Startup, FrontEndShows.StartupDuration);
        if (show is null) return "Pick a start-up show first.";
        Begin(ShowKind.Test, show, FrontEndShows.StartupDuration, ResumeTarget(), "tried in the Animations tab");
        return $"Playing {show.Name} for {FrontEndShows.StartupDuration.TotalSeconds:0.#} s.";
    }

    public string TryShutdown()
    {
        var resume = ResumeTarget();
        var show = LightShows.Shutdown(FrontEndShows.Shutdown, FrontEndShows.ShutdownDuration, resume);
        if (show is null) return "Pick a shut-down show first.";
        Begin(ShowKind.Test, show, FrontEndShows.ShutdownDuration, resume, "tried in the Animations tab");
        return $"Playing {show.Name} for {FrontEndShows.ShutdownDuration.TotalSeconds:0.#} s, then back to what was shown.";
    }

    /// <summary>Stops any show and shows what it replaced (or what the front end selected meanwhile).</summary>
    public void Stop(string why) => Finish(why);

    private ILightSource ResumeTarget() => Active == ShowKind.None ? _session.Engine.Source : _resume ?? new AllOff();

    // ---- running a show -------------------------------------------------------------------------------------

    private void Begin(ShowKind kind, ILightSource show, TimeSpan? duration, ILightSource? resume, string why)
    {
        if (Active != ShowKind.None) Cleanup();
        Active = kind;
        _show = show;
        _resume = resume;
        AppLog.Info($"Show: {show.Name} ({why}" + (duration is { } d ? $", {d.TotalSeconds:0.#} s)." : ", until the controls are used)."));
        Set(show);
        _endTimer.Stop();
        if (duration is { } t) { _endTimer.Interval = t; _endTimer.Start(); }
        Changed?.Invoke();
    }

    /// <summary>Ends the show and puts back what it replaced.</summary>
    private void Finish(string why)
    {
        if (Active == ShowKind.None) return;
        var resume = _resume ?? new AllOff();
        var name = _show?.Name;
        Cleanup();
        AppLog.Info($"Show: {name} ended ({why}); showing {resume.Name}.");
        Set(resume);
        Changed?.Invoke();
    }

    /// <summary>Ends the show without putting anything back: something else takes the panel (a launch, a game).</summary>
    public void Cancel(string why)
    {
        if (Active == ShowKind.None) return;
        AppLog.Info($"Show: {_show?.Name} stopped ({why}).");
        Cleanup();
        Changed?.Invoke();
    }

    private void Cleanup()
    {
        _endTimer.Stop();
        if (Active == ShowKind.Idle || _monitor.IsIdle) _monitor.Reset();
        if (_rawInput) { RawInputWatcher.Shared.End(IdleOwner); _rawInput = false; }
        if (_audio) { Audio.Release(IdleOwner); _audio = false; }
        Active = ShowKind.None;
        _show = null;
        _resume = null;
    }

    private void Set(ILightSource source)
    {
        _setting = true;
        try { _main.SetSource(source); }
        finally { _setting = false; }
    }

    /// <summary>The user picked something in LED-Next (a test pattern, a Games-tab preview) while a show played.</summary>
    private void OnSourceSet(ILightSource source)
    {
        if (_setting || Active == ShowKind.None || ReferenceEquals(source, _show)) return;
        Cancel($"'{source.Name}' was picked");
    }

    // ---- idle -----------------------------------------------------------------------------------------------

    private IdleShow MakeIdleShow()
    {
        IAudioLevel? audio = null;
        if (Idle.ReactToAudio)
        {
            Audio.Acquire(IdleOwner);
            _audio = true;
            audio = Audio;
        }
        return LightShows.Idle(Idle, audio);
    }

    private void OnRawInput(string what)
    {
        if (Active == ShowKind.Idle) _activityAt = Clock.Elapsed;
    }

    /// <summary>Why the idle show can't start now, or null.</summary>
    private string? IdleBlockedBy()
    {
        if (!Idle.Enabled) return "off";
        var life = _lamps.Lifecycle;
        if (life.Launched || life.Phase != LampPhase.Browsing) return GamePlaying;
        if (Active is ShowKind.Startup or ShowKind.Shutdown or ShowKind.Test) return "another show is playing";
        if (Idle.OnlyWithFrontEnd && !_frontEndOpen) return "no front end is open";
        if (_lamps.Speech.Speaking) return "speaking";
        return null;
    }

    private void IdleTick()
    {
        try
        {
            var now = Clock.Elapsed;
            var lastInput = _activityAt;
            if (LastInputTick() is { } tick && _windowsInput.LastInput(now, tick, unchecked((uint)Environment.TickCount)) is var windows && windows > lastInput) lastInput = windows;
            var blocked = IdleBlockedBy();
            // A game just ended: wait the full time again (a game played only with a gamepad leaves no Windows input).
            if (_lastBlock == GamePlaying && blocked != GamePlaying)
            {
                _activityAt = now;
                lastInput = now;
            }
            if (blocked != _lastBlock)
            {
                if (Idle.Enabled && blocked is not null) AppLog.Info($"Idle: waiting ({blocked}).");
                _lastBlock = blocked;
            }
            switch (_monitor.Tick(now, lastInput, Idle.After, blocked))
            {
                case IdleChange.Start:
                    StartIdle(now - lastInput);
                    break;
                case IdleChange.Stop:
                    if (Active == ShowKind.Idle) Finish(blocked is null ? $"input after {Minutes(now - _idleSince)}" : blocked);
                    break;
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("Idle: check failed", ex);
        }
    }

    private void StartIdle(TimeSpan quietFor)
    {
        var resume = ResumeTarget();
        if (Active != ShowKind.None) Cleanup();
        var show = MakeIdleShow();
        _idleSince = Clock.Elapsed;
        Begin(ShowKind.Idle, show, null, resume, $"no input for {Minutes(quietFor)}" + (show.ReactsToAudio ? ", following the sound" : ""));
        RawInputWatcher.Shared.Begin(IdleOwner); // joysticks and gamepads also end it
        _rawInput = true;
    }

    private static string Minutes(TimeSpan t) => t.TotalMinutes >= 1 ? $"{t.TotalMinutes:0.#} min" : $"{t.TotalSeconds:0} s";

    /// <summary>Tick count of Windows' last keyboard or mouse input in this session, or null.</summary>
    private static uint? LastInputTick()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        return GetLastInputInfo(ref info) ? info.dwTime : null;
    }

    public void Dispose()
    {
        _idleTimer.Stop();
        _endTimer.Stop();
        _main.SourceSet -= OnSourceSet;
        RawInputWatcher.Shared.Input -= OnRawInput;
        if (_rawInput) RawInputWatcher.Shared.End(IdleOwner);
        Audio.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO info);
}
