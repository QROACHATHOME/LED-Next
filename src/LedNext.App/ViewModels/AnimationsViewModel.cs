using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LedNext.App.Shows;
using LedNext.Core.Shows;

namespace LedNext.App.ViewModels;

/// <summary>Animations tab: the idle show, its sound reaction, and the front end's start-up / shut-down shows.
/// Edits cabinet.json's <c>idle</c> and <c>shows</c>.</summary>
public sealed partial class AnimationsViewModel : ObservableObject
{
    private const string MeterUser = "Animations tab";
    private readonly AppSession _session;
    private readonly ShowController _shows;
    private readonly DispatcherTimer _meterTimer;

    public AnimationsViewModel(AppSession session, ShowController shows)
    {
        _session = session;
        _shows = shows;
        IdleShows = new[] { new ShowInfo(LightShows.Random, "A new random show every so often (sound shows only with sound on).") }
            .Concat(LightShows.Loops).ToArray();
        StartupShows = new[] { new ShowInfo(LightShows.None, "No start-up show: the selection is shown at once.") }
            .Concat(LightShows.Intros).ToArray();
        ShutdownShows = new[] { new ShowInfo(LightShows.None, "No shut-down show: all lights go off at once.") }
            .Concat(LightShows.Outros).ToArray();
        _meterTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(80), DispatcherPriority.Background, (_, _) => ReadMeter(),
            System.Windows.Application.Current.Dispatcher);
        _meterTimer.Stop();
        _shows.Changed += () => OnPropertyChanged(nameof(Playing));
    }

    private IdleSettings I => _session.Config.Idle;
    private FrontEndShowSettings F => _session.Config.Shows;

    public IReadOnlyList<ShowInfo> IdleShows { get; }
    public IReadOnlyList<ShowInfo> StartupShows { get; }
    public IReadOnlyList<ShowInfo> ShutdownShows { get; }

    [ObservableProperty] private string? _status;

    public string Playing => _shows.Active switch
    {
        ShowKind.None => "No show playing.",
        ShowKind.Idle => "The idle show is playing.",
        ShowKind.Startup => "The start-up show is playing.",
        ShowKind.Shutdown => "The shut-down show is playing.",
        _ => "A show you tried is playing.",
    };

    private void Changed(string? property = null, params string[] also)
    {
        _session.MarkDirty();
        OnPropertyChanged(property);
        foreach (var p in also) OnPropertyChanged(p);
    }

    private static ShowInfo Find(IReadOnlyList<ShowInfo> list, string? name) =>
        list.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)) ?? list[0];

    // ---- idle

    public bool IdleEnabled { get => I.Enabled; set { I.Enabled = value; Changed(); } }
    public double IdleMinutes
    {
        get => I.Minutes;
        set { I.Minutes = Math.Clamp(Math.Round(value * 4) / 4, 0.25, 240); Changed(); }
    }
    public ShowInfo IdleShow
    {
        get => Find(IdleShows, I.Show);
        set { I.Show = value?.Name ?? LightShows.Random; Changed(null, nameof(IsRandom), nameof(IdleShowNote)); }
    }
    public bool IsRandom => !LightShows.IsLoop(I.Show);
    public string IdleShowNote => IdleShow.Description +
        (IdleShow.NeedsAudio && !I.ReactToAudio ? " Turn on \"Follow the sound\" below for this one." : "");
    public double ChangeEverySeconds
    {
        get => I.ChangeEverySeconds;
        set { I.ChangeEverySeconds = Math.Clamp(Math.Round(value / 5) * 5, 5, 3600); Changed(); }
    }
    public bool OnlyWithFrontEnd { get => I.OnlyWithFrontEnd; set { I.OnlyWithFrontEnd = value; Changed(); } }
    /// <summary>Animation speed, 0.25–2 (1 = normal).</summary>
    public double Speed
    {
        get => I.Speed;
        set { I.Speed = Math.Clamp(Math.Round(value / 0.05) * 0.05, 0.25, 2); Changed(); }
    }

    // ---- sound

    public bool ReactToAudio { get => I.ReactToAudio; set { I.ReactToAudio = value; Changed(null, nameof(IdleShowNote)); } }
    public double AudioSensitivity
    {
        get => I.AudioSensitivity;
        set { I.AudioSensitivity = Math.Clamp(Math.Round(value, 2), 0.25, 8); Changed(); }
    }

    [ObservableProperty] private double _soundLevel;
    [ObservableProperty] private string? _soundNote;

    /// <summary>Shows the speakers' level live (with the sensitivity applied), to set the sensitivity.</summary>
    public bool WatchSound
    {
        get => _meterTimer.IsEnabled;
        set
        {
            if (value == _meterTimer.IsEnabled) return;
            if (value) { _shows.Audio.Acquire(MeterUser); _meterTimer.Start(); }
            else { _meterTimer.Stop(); _shows.Audio.Release(MeterUser); SoundLevel = 0; SoundNote = null; }
            OnPropertyChanged();
        }
    }

    private void ReadMeter()
    {
        var (l, r) = _shows.Audio.Peak;
        double level = Math.Clamp(Math.Max(l, r) * I.ClampedSensitivity, 0, 1);
        SoundLevel = Math.Round(level * 100);
        SoundNote = Math.Max(l, r) < 0.02 ? "Silence: play some music or a game's sound." :
            level >= 0.99 ? "Often at the top: lower the sensitivity." : $"Level {SoundLevel:0}%";
    }

    // ---- start-up and shut-down

    public ShowInfo StartupShow
    {
        get => Find(StartupShows, F.Startup);
        set { F.Startup = value?.Name ?? LightShows.None; Changed(); }
    }
    public double StartupSeconds
    {
        get => F.StartupSeconds;
        set { F.StartupSeconds = Math.Clamp(Math.Round(value * 2) / 2, 1, 60); Changed(); }
    }
    public ShowInfo ShutdownShow
    {
        get => Find(ShutdownShows, F.Shutdown);
        set { F.Shutdown = value?.Name ?? LightShows.None; Changed(); }
    }
    public double ShutdownSeconds
    {
        get => F.ShutdownSeconds;
        set { F.ShutdownSeconds = Math.Clamp(Math.Round(value * 2) / 2, 1, 30); Changed(); }
    }
    public bool AlsoLaunchBox { get => F.AlsoLaunchBox; set { F.AlsoLaunchBox = value; Changed(); } }

    [RelayCommand] private void TryIdle() => Status = _shows.TryIdle();
    [RelayCommand] private void TryStartup() => Status = _shows.TryStartup();
    [RelayCommand] private void TryShutdown() => Status = _shows.TryShutdown();

    [RelayCommand]
    private void StopShow()
    {
        _shows.Stop("stopped in the Animations tab");
        Status = "Stopped.";
    }
}
