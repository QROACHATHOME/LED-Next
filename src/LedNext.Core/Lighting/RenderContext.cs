namespace LedNext.Core.Lighting;

/// <summary>Something that decides what the panel shows at a given moment.</summary>
public interface ILightSource
{
    string Name { get; }
    void Render(RenderContext ctx);
}

/// <summary>Passed to <see cref="ILightSource.Render"/> once per frame.</summary>
public sealed class RenderContext
{
    internal readonly Dictionary<string, Rgb> Colors = new();
    internal readonly Dictionary<(string BoardKey, int Pin), byte> RawPins = new();

    public RenderContext(PanelSnapshot panel, TimeSpan time)
    {
        Panel = panel;
        Time = time;
    }

    public PanelSnapshot Panel { get; }
    public TimeSpan Time { get; }
    /// <summary>Optional one-line description of what is lit right now (shown in the status bar).</summary>
    public string? Status { get; set; }

    public void Set(string buttonId, Rgb color) => Colors[buttonId] = color;

    /// <summary>The colour a button has so far in this frame, or null when nothing set it.</summary>
    public Rgb? ColorOf(string buttonId) => Colors.TryGetValue(buttonId, out var c) ? c : null;

    public void SetAll(Rgb color)
    {
        foreach (var b in Panel.Buttons) Colors[b.Id] = color;
    }

    /// <summary>Drives a pin directly, bypassing wiring, brightness and gamma. Pin is 1-based.</summary>
    public void SetPin(string boardKey, int pin, byte value) => RawPins[(boardKey, pin)] = value;
}

/// <summary>Result of one rendered frame.</summary>
public sealed record FrameSnapshot(
    TimeSpan Time,
    string SourceName,
    string? Status,
    IReadOnlyDictionary<string, Rgb> ButtonColors,
    IReadOnlyDictionary<string, byte[]> BoardPins);
