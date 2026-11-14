using LedNext.Core.Lighting;

namespace LedNext.Core.Shows;

/// <summary>A show in the settings' lists. <paramref name="NeedsAudio"/>: it only makes sense with sound.</summary>
public sealed record ShowInfo(string Name, string Description, bool NeedsAudio = false)
{
    public override string ToString() => Name;
}

/// <summary>
/// The light shows for idle, front-end start-up and shut-down. Every show is drawn from the frame's time only (plus
/// its own start time and the sound level), so it needs no timer; the engine thread renders it.
/// </summary>
public static class LightShows
{
    public const string Random = "Random";
    public const string None = "None";

    private static readonly (ShowInfo Info, Func<AudioEnvelope?, ILightSource> Make)[] LoopMakers =
    {
        (new("Rainbow wave", "A rainbow sweeping across the panel."), _ => new RainbowWave()),
        (new("Color fade", "The whole panel fades slowly through every colour."), _ => new ColorFade()),
        (new("Breathing", "All buttons glow up and down slowly, changing colour."), _ => new Breathing()),
        (new("Comet", "A coloured comet with a tail runs across the buttons."), _ => new Comet()),
        (new("Twinkle", "Random buttons twinkle on and off in random colours."), _ => new Twinkle()),
        (new("Player colors", "Each player's buttons breathe in that player's colour."), _ => new PlayerColors()),
        (new("Scanner", "A red light sweeps left and right."), _ => new Scanner()),
        (new("Ripple", "Rings of colour spread out from the middle."), _ => new Ripple()),
        (new("Fire", "Warm flickering flames."), _ => new Fire()),
        (new("Rain", "Blue drops fall down the panel."), _ => new Rain()),
        (new("Sound meter", "A level meter from the middle out: left side the left channel, right side the right.", NeedsAudio: true),
            a => new SoundMeter(a)),
        (new("Beat flash", "The panel flashes a new colour on every beat.", NeedsAudio: true), a => new BeatFlash(a)),
    };

    private static readonly (ShowInfo Info, Func<TimeSpan, ILightSource> Make)[] IntroMakers =
    {
        (new("Power up", "Fades in from black to white, then turns into a rainbow."), d => new PowerUp(d)),
        (new("Sweep on", "Buttons switch on from left to right."), d => new SweepOn(d)),
        (new("Flash", "Three white flashes, then a rainbow."), d => new FlashIntro(d)),
    };

    private static readonly (ShowInfo Info, Func<TimeSpan, ILightSource?, ILightSource> Make)[] OutroMakers =
    {
        (new("Fade out", "The lights shown fade to black."), (d, p) => new FadeOut(d, p)),
        (new("Sweep off", "The lights shown switch off from right to left."), (d, p) => new SweepOff(d, p)),
        (new("Flash and fade", "Two white flashes, then white fading to black."), (d, _) => new FlashOutro(d)),
    };

    /// <summary>Idle shows (looping), sound-reactive ones last.</summary>
    public static IReadOnlyList<ShowInfo> Loops { get; } = LoopMakers.Select(m => m.Info).ToArray();
    /// <summary>Start-up shows: the intros, then the looping shows that don't need sound.</summary>
    public static IReadOnlyList<ShowInfo> Intros { get; } =
        IntroMakers.Select(m => m.Info).Concat(Loops.Where(l => !l.NeedsAudio)).ToArray();
    /// <summary>Shut-down shows: the outros, then the looping shows that don't need sound (they fade out at the end).</summary>
    public static IReadOnlyList<ShowInfo> Outros { get; } =
        OutroMakers.Select(m => m.Info).Concat(Loops.Where(l => !l.NeedsAudio)).ToArray();

    public static bool IsLoop(string? name) => FindLoop(name) is not null;

    /// <summary>A looping show, or null for an unknown name.</summary>
    public static ILightSource? Loop(string? name, AudioEnvelope? audio = null) =>
        FindLoop(name) is { } m ? m.Make(audio) : null;

    /// <summary>
    /// The idle show: one show, or with <see cref="Random"/> (or an unknown name) a new random one every
    /// <see cref="IdleSettings.ChangeEvery"/>. With <paramref name="audio"/> the sound shows join the random pick
    /// and every other show follows the sound level.
    /// </summary>
    public static IdleShow Idle(IdleSettings settings, IAudioLevel? audio, int? seed = null)
    {
        var envelope = audio is null ? null : new AudioEnvelope(audio, settings.ClampedSensitivity);
        var names = IsLoop(settings.Show)
            ? new[] { FindLoop(settings.Show)!.Value.Info.Name }
            : Loops.Where(l => envelope is not null || !l.NeedsAudio).Select(l => l.Name).ToArray();
        return new IdleShow(names, settings.ChangeEvery, envelope, seed ?? Environment.TickCount);
    }

    /// <summary>The start-up show, or null for <see cref="None"/> / an unknown name.</summary>
    public static ILightSource? Startup(string? name, TimeSpan duration)
    {
        foreach (var m in IntroMakers)
            if (Same(m.Info.Name, name)) return m.Make(duration);
        return Loop(name) is { } loop && !FindLoop(name)!.Value.Info.NeedsAudio ? new Named(loop, $"Start-up: {loop.Name}") : null;
    }

    /// <summary>The shut-down show, or null for <see cref="None"/> / an unknown name. <paramref name="previous"/> is what was shown.</summary>
    public static ILightSource? Shutdown(string? name, TimeSpan duration, ILightSource? previous)
    {
        foreach (var m in OutroMakers)
            if (Same(m.Info.Name, name)) return m.Make(duration, previous);
        return Loop(name) is { } loop && !FindLoop(name)!.Value.Info.NeedsAudio ? new FadingEnd(loop, duration) : null;
    }

    private static (ShowInfo Info, Func<AudioEnvelope?, ILightSource> Make)? FindLoop(string? name)
    {
        foreach (var m in LoopMakers)
            if (Same(m.Info.Name, name)) return m;
        return null;
    }

    private static bool Same(string a, string? b) => string.Equals(a, b?.Trim(), StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Plays one show, or picks a new random one every <paramref name="changeEvery"/> with a cross-fade. With a sound
/// level, shows that don't react to sound themselves get brighter with the sound (and look normal in silence).
/// Engine thread only.
/// </summary>
public sealed class IdleShow : ILightSource
{
    public static readonly TimeSpan FadeTime = TimeSpan.FromSeconds(2);
    private readonly IReadOnlyList<string> _names;
    private readonly TimeSpan _changeEvery;
    private readonly AudioEnvelope? _audio;
    private readonly System.Random _random;
    private ILightSource? _current, _previous;
    private string _currentName = "";
    private TimeSpan _since, _fadeFrom;

    public IdleShow(IReadOnlyList<string> names, TimeSpan changeEvery, AudioEnvelope? audio, int seed)
    {
        _names = names.Count > 0 ? names : new[] { LightShows.Loops[0].Name };
        _changeEvery = changeEvery;
        _audio = audio;
        _random = new System.Random(seed);
        Name = _names.Count == 1 ? $"Idle: {_names[0]}" : "Idle: random shows";
    }

    public string Name { get; }
    public IReadOnlyList<string> Names => _names;
    /// <summary>The show playing now (for tests and the status bar).</summary>
    public string Current => _currentName;
    public bool ReactsToAudio => _audio is not null;

    public void Render(RenderContext ctx)
    {
        var t = ctx.Time;
        _audio?.Update(t);
        if (_current is null) Next(t, fade: false);
        else if (_names.Count > 1 && t - _since >= _changeEvery) Next(t, fade: true);

        var now = Draw(_current!, ctx);
        if (_previous is not null)
        {
            double f = (t - _fadeFrom) / FadeTime;
            if (f >= 1) _previous = null;
            else
            {
                var before = Draw(_previous, ctx);
                foreach (var b in ctx.Panel.Buttons)
                {
                    var a = before.TryGetValue(b.Id, out var x) ? x : Rgb.Black;
                    var c = now.TryGetValue(b.Id, out var y) ? y : Rgb.Black;
                    now[b.Id] = ShowMath.Mix(a, c, f);
                }
            }
        }
        bool pulse = _audio is { Active: true } && !_currentReacts;
        foreach (var (id, color) in now) ctx.Set(id, pulse ? color.Scale(0.15 + 0.85 * _audio!.Level) : color);
        ctx.Status = $"Idle: {_currentName}" + (_audio is { Active: true } ? $" · sound {_audio.Level:P0}" : "");
    }

    private bool _currentReacts;

    private void Next(TimeSpan t, bool fade)
    {
        string name = _names[0];
        if (_names.Count > 1)
        {
            do name = _names[_random.Next(_names.Count)];
            while (name == _currentName);
        }
        _previous = fade ? _current : null;
        _fadeFrom = t;
        _since = t;
        _currentName = name;
        _current = LightShows.Loop(name, _audio) ?? new RainbowWave();
        _currentReacts = LightShows.Loops.Any(l => l.NeedsAudio && l.Name == name);
    }

    private static Dictionary<string, Rgb> Draw(ILightSource source, RenderContext ctx)
    {
        var tmp = new RenderContext(ctx.Panel, ctx.Time);
        source.Render(tmp);
        return new Dictionary<string, Rgb>(tmp.Colors);
    }
}

internal static class ShowMath
{
    public static Rgb Mix(Rgb a, Rgb b, double f)
    {
        f = Math.Clamp(f, 0, 1);
        return new((byte)Math.Round(a.R + (b.R - a.R) * f), (byte)Math.Round(a.G + (b.G - a.G) * f), (byte)Math.Round(a.B + (b.B - a.B) * f));
    }

    /// <summary>0..1 from a button id and a number: the same inputs always give the same value.</summary>
    public static double Hash(string id, long n)
    {
        ulong h = 1469598103934665603UL;
        foreach (char c in id) { h ^= c; h *= 1099511628211UL; }
        h ^= (ulong)n * 0x9E3779B97F4A7C15UL;
        h ^= h >> 31; h *= 0xBF58476D1CE4E5B9UL; h ^= h >> 29;
        return (h >> 11) / (double)(1UL << 53);
    }

    /// <summary>Smooth noise 0..1 over time for one button.</summary>
    public static double Noise(string id, double t, long salt = 0)
    {
        long i = (long)Math.Floor(t);
        double f = t - i, s = f * f * (3 - 2 * f);
        return Hash(id, i * 31 + salt) * (1 - s) + Hash(id, (i + 1) * 31 + salt) * s;
    }

    public static double Smooth(double x)
    {
        x = Math.Clamp(x, 0, 1);
        return x * x * (3 - 2 * x);
    }

    /// <summary>Buttons left to right, then top to bottom.</summary>
    public static ButtonInfo[] LeftToRight(PanelSnapshot panel) =>
        panel.Buttons.OrderBy(b => b.NormX).ThenBy(b => b.NormY).ToArray();
}

/// <summary>A show with a start: its first frame is time 0.</summary>
public abstract class TimedShow : ILightSource
{
    private TimeSpan? _start;
    public abstract string Name { get; }

    /// <summary>Seconds since the show's first frame.</summary>
    protected double Elapsed(RenderContext ctx)
    {
        _start ??= ctx.Time;
        return Math.Max(0, (ctx.Time - _start.Value).TotalSeconds);
    }

    public abstract void Render(RenderContext ctx);
}

internal sealed class Named(ILightSource inner, string name) : ILightSource
{
    public string Name { get; } = name;
    public void Render(RenderContext ctx) => inner.Render(ctx);
}

// ---- looping shows ------------------------------------------------------------------------------------

internal sealed class RainbowWave : ILightSource
{
    public string Name => "Rainbow wave";
    public void Render(RenderContext ctx)
    {
        double t = ctx.Time.TotalSeconds;
        foreach (var b in ctx.Panel.Buttons) ctx.Set(b.Id, Rgb.FromHsv(b.NormX * 360 + b.NormY * 60 - t * 60, 1, 1));
    }
}

internal sealed class ColorFade : ILightSource
{
    public string Name => "Color fade";
    public void Render(RenderContext ctx) => ctx.SetAll(Rgb.FromHsv(ctx.Time.TotalSeconds * 15, 1, 1));
}

internal sealed class Breathing : ILightSource
{
    public string Name => "Breathing";
    public void Render(RenderContext ctx)
    {
        double t = ctx.Time.TotalSeconds;
        double v = 0.12 + 0.88 * (0.5 - 0.5 * Math.Cos(2 * Math.PI * t / 5));
        ctx.SetAll(Rgb.FromHsv(t * 6, 1, v));
    }
}

internal sealed class Comet : ILightSource
{
    private const double ButtonsPerSecond = 9, Tail = 4;
    public string Name => "Comet";
    public void Render(RenderContext ctx)
    {
        var order = ShowMath.LeftToRight(ctx.Panel);
        if (order.Length == 0) return;
        double t = ctx.Time.TotalSeconds, head = t * ButtonsPerSecond % order.Length;
        double hue = t * 20;
        for (int i = 0; i < order.Length; i++)
        {
            double behind = ((head - i) % order.Length + order.Length) % order.Length;
            double v = behind <= Tail ? 1 - behind / Tail : 0;
            ctx.Set(order[i].Id, Rgb.FromHsv(hue + i * 4, 1, Math.Max(0.04, v * v)));
        }
    }
}

internal sealed class Twinkle : ILightSource
{
    private const double Period = 2.4;
    public string Name => "Twinkle";
    public void Render(RenderContext ctx)
    {
        double t = ctx.Time.TotalSeconds;
        foreach (var b in ctx.Panel.Buttons)
        {
            double phase = ShowMath.Hash(b.Id, 0) * Period;
            long slot = (long)Math.Floor((t + phase) / Period);
            double f = (t + phase) / Period - slot;
            bool on = ShowMath.Hash(b.Id, slot) < 0.4;
            double v = on ? Math.Pow(Math.Sin(Math.PI * f), 2) : 0;
            var color = Rgb.FromHsv(ShowMath.Hash(b.Id, slot + 7) * 360, 0.7, Math.Max(0.04, v));
            ctx.Set(b.Id, color);
        }
    }
}

internal sealed class PlayerColors : ILightSource
{
    private static readonly double[] Hues = { 0, 0, 220, 120, 55 }; // shared, P1 red, P2 blue, P3 green, P4 yellow
    public string Name => "Player colors";
    public void Render(RenderContext ctx)
    {
        double t = ctx.Time.TotalSeconds;
        foreach (var b in ctx.Panel.Buttons)
        {
            double v = 0.3 + 0.7 * (0.5 - 0.5 * Math.Cos(2 * Math.PI * (t / 3 + b.Player * 0.25)));
            ctx.Set(b.Id, b.Player is >= 1 and <= 4 ? Rgb.FromHsv(Hues[b.Player], 1, v) : Rgb.White.Scale(v));
        }
    }
}

internal sealed class Scanner : ILightSource
{
    public string Name => "Scanner";
    public void Render(RenderContext ctx)
    {
        double x = 0.5 - 0.5 * Math.Cos(2 * Math.PI * ctx.Time.TotalSeconds / 2.6);
        foreach (var b in ctx.Panel.Buttons)
        {
            double v = Math.Max(0, 1 - Math.Abs(b.NormX - x) / 0.12);
            ctx.Set(b.Id, Rgb.FromHsv(0, 1, Math.Max(0.04, v)));
        }
    }
}

internal sealed class Ripple : ILightSource
{
    public string Name => "Ripple";
    public void Render(RenderContext ctx)
    {
        double t = ctx.Time.TotalSeconds;
        foreach (var b in ctx.Panel.Buttons)
        {
            double dx = b.NormX - 0.5, dy = (b.NormY - 0.5) * 0.5, d = Math.Sqrt(dx * dx + dy * dy);
            double v = 0.5 + 0.5 * Math.Cos(2 * Math.PI * (d * 3 - t * 0.7));
            ctx.Set(b.Id, Rgb.FromHsv(200 + d * 160 + t * 10, 1, v * v));
        }
    }
}

internal sealed class Fire : ILightSource
{
    public string Name => "Fire";
    public void Render(RenderContext ctx)
    {
        double t = ctx.Time.TotalSeconds;
        foreach (var b in ctx.Panel.Buttons)
        {
            double n = ShowMath.Noise(b.Id, t * 5), heat = ShowMath.Noise(b.Id, t * 2, 99);
            double v = (0.35 + 0.65 * n) * (0.6 + 0.4 * b.NormY);
            ctx.Set(b.Id, Rgb.FromHsv(4 + 36 * heat, 1, v));
        }
    }
}

internal sealed class Rain : ILightSource
{
    private const int Columns = 8;
    public string Name => "Rain";
    public void Render(RenderContext ctx)
    {
        double t = ctx.Time.TotalSeconds;
        foreach (var b in ctx.Panel.Buttons)
        {
            long col = Math.Min(Columns - 1, (long)(b.NormX * Columns));
            double speed = 0.5 + ShowMath.Hash("rain", col) * 0.6, phase = ShowMath.Hash("rain", col + 100);
            double f = t * speed * 0.5 + phase;
            double head = (f - Math.Floor(f)) * 1.6 - 0.3; // the drop's height, from above the panel to below it
            double behind = head - b.NormY;
            double v = behind >= 0 ? Math.Max(0, 1 - behind / 0.35) : 0;
            ctx.Set(b.Id, Rgb.FromHsv(195 + ShowMath.Hash("rain", col + 200) * 30, 0.8, Math.Max(0.03, v)));
        }
    }
}

/// <summary>Level meter from the middle out. Without sound, a dim green glow.</summary>
internal sealed class SoundMeter(AudioEnvelope? audio) : ILightSource
{
    public string Name => "Sound meter";
    public void Render(RenderContext ctx)
    {
        audio?.Update(ctx.Time);
        foreach (var b in ctx.Panel.Buttons)
        {
            double d = Math.Abs(b.NormX - 0.5) * 2;
            double level = audio is { Active: true } ? (b.NormX < 0.5 ? audio.Left : audio.Right) : 0;
            bool lit = level > 0.01 && d <= level * 1.05;
            ctx.Set(b.Id, lit ? Rgb.FromHsv(120 - 120 * d, 1, 1) : Rgb.FromHsv(120, 1, 0.04));
        }
    }
}

/// <summary>A new colour on every beat, fading until the next. Without sound, a slow dim colour fade.</summary>
internal sealed class BeatFlash(AudioEnvelope? audio) : ILightSource
{
    private double _hue;
    public string Name => "Beat flash";
    public void Render(RenderContext ctx)
    {
        audio?.Update(ctx.Time);
        if (audio is not { Active: true })
        {
            ctx.SetAll(Rgb.FromHsv(ctx.Time.TotalSeconds * 15, 1, 0.3));
            return;
        }
        if (audio.Beat) _hue = (_hue + 67) % 360;
        double v = Math.Max(0.08 + 0.3 * audio.Level, Math.Exp(-audio.SinceBeat * 5));
        ctx.SetAll(Rgb.FromHsv(_hue, 1, v));
    }
}

// ---- start-up shows -------------------------------------------------------------------------------------

/// <summary>Black → white over the first half, then the white turns into a rainbow.</summary>
internal sealed class PowerUp(TimeSpan duration) : TimedShow
{
    public override string Name => "Start-up: Power up";
    public override void Render(RenderContext ctx)
    {
        double e = Elapsed(ctx), p = e / duration.TotalSeconds;
        double v = ShowMath.Smooth(p / 0.5), s = ShowMath.Smooth((p - 0.5) * 2);
        foreach (var b in ctx.Panel.Buttons) ctx.Set(b.Id, Rgb.FromHsv(b.NormX * 360 - e * 60, s, v));
    }
}

/// <summary>Buttons switch on left to right (each flashes white as it comes on), in rainbow colours.</summary>
internal sealed class SweepOn(TimeSpan duration) : TimedShow
{
    public override string Name => "Start-up: Sweep on";
    public override void Render(RenderContext ctx)
    {
        double e = Elapsed(ctx), sweep = duration.TotalSeconds * 0.7;
        double edge = e / sweep * 1.1;
        foreach (var b in ctx.Panel.Buttons)
        {
            double litFor = (edge - b.NormX) * sweep; // seconds since the edge passed this button
            if (litFor < 0) { ctx.Set(b.Id, Rgb.Black); continue; }
            var color = Rgb.FromHsv(b.NormX * 300 - Math.Max(0, e - sweep) * 60, 1, 1);
            ctx.Set(b.Id, litFor < 0.15 ? Rgb.White : color);
        }
    }
}

internal sealed class FlashIntro(TimeSpan duration) : TimedShow
{
    public override string Name => "Start-up: Flash";
    public override void Render(RenderContext ctx)
    {
        double e = Elapsed(ctx);
        if (e < 1.5)
        {
            ctx.SetAll(e % 0.5 < 0.25 ? Rgb.White : Rgb.Black);
            return;
        }
        double v = ShowMath.Smooth((e - 1.5) / Math.Max(0.5, duration.TotalSeconds * 0.3));
        foreach (var b in ctx.Panel.Buttons) ctx.Set(b.Id, Rgb.FromHsv(b.NormX * 360 - e * 60, 1, v));
    }
}

// ---- shut-down shows -----------------------------------------------------------------------------------

/// <summary>What was shown, fading to black.</summary>
internal sealed class FadeOut(TimeSpan duration, ILightSource? previous) : TimedShow
{
    public override string Name => "Shut-down: Fade out";
    public override void Render(RenderContext ctx)
    {
        double p = Elapsed(ctx) / duration.TotalSeconds;
        var colors = ShowParts.Draw(previous, ctx);
        double f = 1 - ShowMath.Smooth(p);
        foreach (var (id, c) in colors) ctx.Set(id, c.Scale(f));
    }
}

/// <summary>What was shown, switching off from right to left.</summary>
internal sealed class SweepOff(TimeSpan duration, ILightSource? previous) : TimedShow
{
    public override string Name => "Shut-down: Sweep off";
    public override void Render(RenderContext ctx)
    {
        double p = Elapsed(ctx) / (duration.TotalSeconds * 0.85);
        var colors = ShowParts.Draw(previous, ctx);
        foreach (var b in ctx.Panel.Buttons)
            ctx.Set(b.Id, b.NormX >= 1.05 - p * 1.1 ? Rgb.Black : colors.TryGetValue(b.Id, out var c) ? c : Rgb.Black);
    }
}

internal sealed class FlashOutro(TimeSpan duration) : TimedShow
{
    public override string Name => "Shut-down: Flash and fade";
    public override void Render(RenderContext ctx)
    {
        double e = Elapsed(ctx);
        if (e < 1.0) { ctx.SetAll(e % 0.5 < 0.25 ? Rgb.White : Rgb.Black); return; }
        double rest = Math.Max(0.5, duration.TotalSeconds - 1.0);
        ctx.SetAll(Rgb.White.Scale(1 - ShowMath.Smooth((e - 1.0) / rest)));
    }
}

/// <summary>A looping show used at shut-down: it fades to black over the last 40% of the time.</summary>
internal sealed class FadingEnd(ILightSource inner, TimeSpan duration) : TimedShow
{
    public override string Name => $"Shut-down: {inner.Name}";
    public override void Render(RenderContext ctx)
    {
        double p = Elapsed(ctx) / duration.TotalSeconds;
        double f = Math.Clamp((1 - p) / 0.4, 0, 1);
        foreach (var (id, c) in ShowParts.Draw(inner, ctx)) ctx.Set(id, c.Scale(f));
    }
}

internal static class ShowParts
{
    private static readonly HashSet<string> Failed = new(); // logged once each, not every frame

    public static Dictionary<string, Rgb> Draw(ILightSource? source, RenderContext ctx)
    {
        if (source is null) return new();
        var tmp = new RenderContext(ctx.Panel, ctx.Time);
        try { source.Render(tmp); }
        catch (Exception ex)
        {
            lock (Failed)
                if (Failed.Add(source.Name)) AppLog.Warn($"Show: '{source.Name}' failed under the show: {ex.Message}");
            return new();
        }
        return new Dictionary<string, Rgb>(tmp.Colors);
    }
}
