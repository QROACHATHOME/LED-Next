using LedNext.Core.Lighting;

namespace LedNext.Core.Speech;

/// <summary>
/// The engine's top layer while labels are spoken: the buttons of the label being said blink (their colour from
/// whatever is shown underneath, else white) and everything else stays as it is. Set from the speech thread or
/// the UI thread, read by the engine thread. Rendered by <see cref="LedEngine.Highlight"/>, so it never replaces
/// the light source (the lamp lifecycle and the user's picks are not affected).
/// </summary>
public sealed class SpeechHighlight : ILightSource
{
    /// <summary>On and off this long each (about 3 blinks a second).</summary>
    public static readonly TimeSpan HalfPeriod = TimeSpan.FromMilliseconds(160);

    private readonly object _lock = new();
    private IReadOnlyList<string> _buttons = Array.Empty<string>();
    private TimeSpan? _since;

    public string Name => "speaking";

    /// <summary>The buttons to blink now; empty stops the blinking.</summary>
    public IReadOnlyList<string> Buttons { get { lock (_lock) return _buttons; } }

    public void Blink(IReadOnlyList<string> buttons)
    {
        lock (_lock) { _buttons = buttons; _since = null; }
    }

    public void Clear() => Blink(Array.Empty<string>());

    public void Render(RenderContext ctx)
    {
        IReadOnlyList<string> buttons;
        TimeSpan since;
        lock (_lock)
        {
            if (_buttons.Count == 0) return;
            _since ??= ctx.Time; // blinking starts lit, on the first frame after Blink
            buttons = _buttons;
            since = _since.Value;
        }
        bool on = ((long)((ctx.Time - since).Ticks / HalfPeriod.Ticks) & 1) == 0;
        foreach (var id in buttons)
        {
            var under = ctx.ColorOf(id);
            ctx.Set(id, on ? (under is { } c && c != Rgb.Black ? c : Rgb.White) : Rgb.Black);
        }
    }
}
