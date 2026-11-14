namespace LedNext.Core.Shows;

/// <summary>
/// Idle (attract) animation: after the controls haven't been touched for a while, the panel plays a light show
/// until a key, button or the mouse is used again. Added in config version 11; off by default.
/// </summary>
public sealed class IdleSettings
{
    public bool Enabled { get; set; }
    /// <summary>Minutes without input before the show starts.</summary>
    public double Minutes { get; set; } = 5;
    /// <summary>A show name from <see cref="LightShows.Loops"/>, or <see cref="LightShows.Random"/>.</summary>
    public string Show { get; set; } = LightShows.Random;
    /// <summary>With <see cref="LightShows.Random"/>: seconds before the next show (they cross-fade).</summary>
    public double ChangeEverySeconds { get; set; } = 60;
    /// <summary>
    /// Only while LaunchBox / BigBox runs (on). Off: also when no front end is open (LED-Next started by hand, or
    /// after BigBox closed).
    /// </summary>
    public bool OnlyWithFrontEnd { get; set; } = true;

    /// <summary>The idle show follows the music or game sound playing on the PC (Windows' default speakers).</summary>
    public bool ReactToAudio { get; set; }
    /// <summary>Gain on the sound level: 1 = as Windows reports it, more for quiet sound.</summary>
    public double AudioSensitivity { get; set; } = 1.5;

    public TimeSpan After => TimeSpan.FromMinutes(Math.Clamp(Minutes, 0.25, 240));
    public TimeSpan ChangeEvery => TimeSpan.FromSeconds(Math.Clamp(ChangeEverySeconds, 5, 3600));
    public double ClampedSensitivity => Math.Clamp(AudioSensitivity, 0.25, 8);
}

/// <summary>
/// A short light show when the front end starts and when it closes. Added in config version 11; none by default
/// (the panel behaves as before: the selection at start, all off at close).
/// </summary>
public sealed class FrontEndShowSettings
{
    /// <summary>A show name from <see cref="LightShows.Intros"/>, or <see cref="LightShows.None"/>.</summary>
    public string Startup { get; set; } = LightShows.None;
    public double StartupSeconds { get; set; } = 5;
    /// <summary>A show name from <see cref="LightShows.Outros"/>, or <see cref="LightShows.None"/>.</summary>
    public string Shutdown { get; set; } = LightShows.None;
    public double ShutdownSeconds { get; set; } = 3;
    /// <summary>Also when LaunchBox (not only BigBox) starts or closes.</summary>
    public bool AlsoLaunchBox { get; set; }

    public TimeSpan StartupDuration => TimeSpan.FromSeconds(Math.Clamp(StartupSeconds, 1, 60));
    public TimeSpan ShutdownDuration => TimeSpan.FromSeconds(Math.Clamp(ShutdownSeconds, 1, 30));

    /// <summary>True when the shows apply to this front end ("BigBox", "LaunchBox").</summary>
    public bool AppliesTo(string? source) =>
        string.Equals(source, "BigBox", StringComparison.OrdinalIgnoreCase) ||
        (AlsoLaunchBox && string.Equals(source, "LaunchBox", StringComparison.OrdinalIgnoreCase));
}
