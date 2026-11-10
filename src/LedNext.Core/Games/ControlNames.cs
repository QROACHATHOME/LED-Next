using System.Text.RegularExpressions;

namespace LedNext.Core.Games;

/// <summary>
/// Logical control names, LEDBlinky / MAME style. Several source names collapse into one lit
/// control: the four joystick directions are one joystick, START1 is P1_START, and so on.
/// </summary>
public static partial class ControlNames
{
    /// <summary>
    /// The normalised name, or null for descriptor entries that are not lit controls
    /// (LEDBlinky's CONTROL_JOY8WAY, CONTROL_TRACKBALL… on player 0).
    /// </summary>
    public static string? Normalize(string raw) => Classify(raw).Name;

    /// <summary>
    /// Normalised name plus whether it is a "digital" control: one physical input with its own
    /// key code (a button, start, coin, UI key). Joysticks and analog controls are collapsed from
    /// several inputs and are matched to panel buttons by name only, never by key code.
    /// </summary>
    public static (string? Name, bool IsDigital) Classify(string raw)
    {
        var s = raw.Trim().ToUpperInvariant();
        if (s.Length == 0 || s.StartsWith("CONTROL_", StringComparison.Ordinal)) return (null, false);

        var m = StartCoin().Match(s);
        if (m.Success) return ($"P{m.Groups[2].Value}_{m.Groups[1].Value}", true);

        m = Player().Match(s);
        if (m.Success)
        {
            var p = m.Groups[1].Value;
            var rest = m.Groups[2].Value;
            if (rest.StartsWith("JOYSTICK", StringComparison.Ordinal)) return ($"P{p}_JOYSTICK", false);
            if (rest.StartsWith("TRACKBALL", StringComparison.Ordinal)) return ($"P{p}_TRACKBALL", false);
            if (rest.StartsWith("DIAL", StringComparison.Ordinal)) return ($"P{p}_DIAL", false);
            if (rest.StartsWith("AD_STICK", StringComparison.Ordinal) || rest == "STICK") return ($"P{p}_STICK", false);
            if (rest.StartsWith("LIGHTGUN", StringComparison.Ordinal)) return ($"P{p}_LIGHTGUN", false);
            if (rest.StartsWith("PADDLE", StringComparison.Ordinal)) return ($"P{p}_PADDLE", false);
            if (rest.StartsWith("POSITIONAL", StringComparison.Ordinal)) return ($"P{p}_POSITIONAL", false);
            var pedal = Pedal().Match(rest);
            if (pedal.Success) return ($"P{p}_PEDAL{pedal.Groups[1].Value}", false);
        }
        return (s, true);
    }

    /// <summary>Player number in a normalised name (P2_BUTTON1 → 2), or 0.</summary>
    public static int PlayerOf(string control)
    {
        var m = Player().Match(control);
        return m.Success ? int.Parse(m.Groups[1].Value) : 0;
    }

    [GeneratedRegex(@"^(START|COIN)(\d)$")]
    private static partial Regex StartCoin();

    [GeneratedRegex(@"^P(\d)_(.+)$")]
    private static partial Regex Player();

    [GeneratedRegex(@"^PEDAL(\d)")]
    private static partial Regex Pedal();
}
