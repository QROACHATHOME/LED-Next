using System.Diagnostics;
using LedNext.Core.Games;
using LedNext.Core.Lighting;

namespace LedNext.Core.Outputs;

/// <summary>
/// The game's lights with an emulator's lamps on top. A lamp-driven control shows its colour from the game's
/// lights (or <paramref name="onColor"/> when the game leaves it unlit) while the lamp is on, and is dark while
/// it is off. With <paramref name="startLampsOnButton1"/>, a player's Start lamp also blinks that player's button 1
/// while it is blinking (it changed in the last <see cref="BlinkWindow"/>), for a panel whose Start buttons have
/// no LEDs; once the lamp settles, button 1 shows the game's lights again.
/// Lamps are set from the UI thread and read by the engine thread.
/// </summary>
public sealed class LampOverlay(ILightSource baseSource, Func<string, IReadOnlyList<string>> buttonsFor, Rgb onColor,
    bool startLampsOnButton1) : ILightSource
{
    public static readonly TimeSpan BlinkWindow = TimeSpan.FromSeconds(1.5);

    private readonly object _lock = new();
    private readonly Dictionary<string, (bool On, TimeSpan ChangedAt)> _lamps = new(StringComparer.OrdinalIgnoreCase);
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    public ILightSource Base { get; } = baseSource;
    public string Name => $"{Base.Name} + lamps";
    /// <summary>For tests: the overlay's clock instead of real time.</summary>
    public Func<TimeSpan>? Clock { get; init; }

    private TimeSpan Now => Clock?.Invoke() ?? _clock.Elapsed;

    public void SetLamp(string control, bool on)
    {
        lock (_lock)
        {
            if (_lamps.TryGetValue(control, out var old) && old.On == on) return;
            _lamps[control] = (on, Now);
        }
    }

    /// <summary>Copies the lamps of an overlay this one replaces (the game's lights changed while it runs).</summary>
    public void CopyLampsFrom(LampOverlay other)
    {
        lock (other._lock)
        lock (_lock)
            foreach (var (c, s) in other._lamps) _lamps[c] = s;
    }

    public int LitLamps { get { lock (_lock) return _lamps.Count(l => l.Value.On); } }

    public void Render(RenderContext ctx)
    {
        Base.Render(ctx);
        KeyValuePair<string, (bool On, TimeSpan ChangedAt)>[] lamps;
        lock (_lock) lamps = _lamps.ToArray();
        if (lamps.Length == 0) return;
        var now = Now;
        var gameColors = new Dictionary<string, Rgb>(ctx.Colors);

        void Show(string control, bool on)
        {
            foreach (var id in buttonsFor(control))
                ctx.Set(id, on ? (gameColors.TryGetValue(id, out var c) && c != Rgb.Black ? c : onColor) : Rgb.Black);
        }

        foreach (var (control, (on, changedAt)) in lamps)
        {
            Show(control, on);
            if (startLampsOnButton1 && now - changedAt < BlinkWindow && IsStart(control, out var player))
                Show($"P{player}_BUTTON1", on);
        }
    }

    private static bool IsStart(string control, out int player)
    {
        player = ControlNames.PlayerOf(control);
        return player > 0 && control.EndsWith("_START", StringComparison.OrdinalIgnoreCase);
    }
}
