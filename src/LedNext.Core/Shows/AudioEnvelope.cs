namespace LedNext.Core.Shows;

/// <summary>The PC's sound level for audio-reactive shows. Implemented in the App (Windows' peak meter).</summary>
public interface IAudioLevel
{
    /// <summary>Peak 0..1 of the left and right channel right now (mono: both the same). Any thread.</summary>
    (float Left, float Right) Peak { get; }
}

/// <summary>
/// Smooths the raw peak for the shows: <see cref="Level"/> rises at once and falls over ~0.3 s, <see cref="Beat"/>
/// is true for the frame in which the level jumps well above its recent average, and <see cref="Active"/> says
/// whether anything has played in the last two seconds (silence: the shows look as they do without audio).
/// <see cref="Update"/> is called once per frame by the engine thread; calling it again for the same time does nothing.
/// </summary>
public sealed class AudioEnvelope(IAudioLevel source, double sensitivity)
{
    private const double ReleasePerSecond = 3.0, AverageSeconds = 1.2, SilenceSeconds = 2.0;
    private const float Floor = 0.02f;
    private TimeSpan? _last;
    private TimeSpan? _lastSound, _lastBeat;
    private double _average;

    public double Sensitivity { get; } = sensitivity;
    public double Level { get; private set; }
    public double Left { get; private set; }
    public double Right { get; private set; }
    public bool Beat { get; private set; }
    public bool Active { get; private set; }
    /// <summary>Seconds since the last beat (large when there was none).</summary>
    public double SinceBeat { get; private set; } = 1000;

    public void Update(TimeSpan now)
    {
        if (_last == now) return;
        double dt = _last is { } l ? Math.Clamp((now - l).TotalSeconds, 0, 0.5) : 0;
        _last = now;
        var (left, right) = source.Peak;
        double gain = Sensitivity;
        double nl = Math.Clamp(left * gain, 0, 1), nr = Math.Clamp(right * gain, 0, 1);
        Left = Follow(Left, nl, dt);
        Right = Follow(Right, nr, dt);
        double raw = Math.Max(nl, nr);
        Level = Math.Max(Left, Right);

        if (Math.Max(left, right) > Floor) _lastSound = now;
        Active = _lastSound is { } heard && now - heard < TimeSpan.FromSeconds(SilenceSeconds);

        // Beat: well above the running average, at most ~4 per second.
        _average += (raw - _average) * Math.Clamp(dt / AverageSeconds, 0, 1);
        Beat = Active && raw > _average * 1.35 + 0.08 && (_lastBeat is not { } beat || now - beat > TimeSpan.FromSeconds(0.25));
        if (Beat) _lastBeat = now;
        SinceBeat = _lastBeat is { } b ? (now - b).TotalSeconds : 1000;
    }

    private static double Follow(double current, double target, double dt) =>
        target >= current ? target : Math.Max(target, current - ReleasePerSecond * dt);
}

/// <summary>A fixed level, for tests and the "Try" buttons without sound.</summary>
public sealed class FixedAudioLevel(float left, float right) : IAudioLevel
{
    public float Left { get; set; } = left;
    public float Right { get; set; } = right;
    public (float Left, float Right) Peak => (Left, Right);
}
