using System.Text.RegularExpressions;

namespace LedNext.Core.Games;

/// <summary>
/// What the games list shows. A saved, named filter is a smart list; it is evaluated each time it is
/// opened, so it always reflects the current library.
/// </summary>
public sealed record GameFilter
{
    /// <summary>Platform key, or null for all platforms.</summary>
    public string? Platform { get; init; }
    /// <summary>Part of the ROM name or title.</summary>
    public string? Text { get; init; }
    /// <summary>Only games with their own, a parent's or customized lights.</summary>
    public bool LitOnly { get; init; } = true;
    public bool CustomizedOnly { get; init; }
    /// <summary>Only parent ROMs (clones follow their parent anyway).</summary>
    public bool HideClones { get; init; }
    public int? MinPlayers { get; init; }
    public int? MaxPlayers { get; init; }
    public int? MinButtons { get; init; }
    public int? MaxButtons { get; init; }
    /// <summary>One of <see cref="ControlTags.All"/>, or null for any.</summary>
    public string? Control { get; init; }
    /// <summary>Only games added to LaunchBox in the last N days (newest first). Null = any.</summary>
    public int? AddedWithinDays { get; init; }
    /// <summary>Only games that are in the LaunchBox library.</summary>
    public bool InLaunchBoxOnly { get; init; }
    /// <summary>Only games with no lights of their own (just the platform default) and not customized.</summary>
    public bool NeedsSetupOnly { get; init; }

    /// <summary>Short text for the UI, e.g. "6 buttons · no clones".</summary>
    public string Describe()
    {
        var parts = new List<string>();
        if (AddedWithinDays is { } days) parts.Add(days == 1 ? "added to LaunchBox today" : $"added to LaunchBox in {days} days");
        else if (InLaunchBoxOnly) parts.Add("in LaunchBox");
        if (NeedsSetupOnly) parts.Add("needs setup");
        if (Platform is not null) parts.Add(Platform.Replace('_', ' '));
        if (!string.IsNullOrWhiteSpace(Text)) parts.Add($"\"{Text}\"");
        if (MinButtons is not null || MaxButtons is not null) parts.Add(Range(MinButtons, MaxButtons, "button"));
        if (MinPlayers is not null || MaxPlayers is not null) parts.Add(Range(MinPlayers, MaxPlayers, "player"));
        if (Control is not null) parts.Add(Control);
        if (HideClones) parts.Add("no clones");
        if (CustomizedOnly) parts.Add("customized");
        else if (LitOnly && !NeedsSetupOnly) parts.Add("with lights");
        return parts.Count == 0 ? "all games" : string.Join(" · ", parts);
    }

    private static string Range(int? min, int? max, string unit) => (min, max) switch
    {
        ({ } a, { } b) when a == b => $"{a} {unit}{(a == 1 ? "" : "s")}",
        ({ } a, { } b) => $"{a}–{b} {unit}s",
        ({ } a, null) => $"{a}+ {unit}s",
        (null, { } b) => $"up to {b} {unit}s",
        _ => "",
    };
}

/// <summary>Control types a game uses, derived from its lit controls and its control description.</summary>
public static class ControlTags
{
    public static readonly string[] All = { "joystick", "trackball", "spinner", "lightgun", "paddle", "pedal" };

    public static IEnumerable<string> FromLights(LightProfile? profile)
    {
        if (profile is null) yield break;
        foreach (var c in profile.Controls)
        {
            var n = c.Control;
            if (n.EndsWith("_JOYSTICK", StringComparison.OrdinalIgnoreCase) || n.EndsWith("_STICK", StringComparison.OrdinalIgnoreCase)) yield return "joystick";
            else if (n.EndsWith("_TRACKBALL", StringComparison.OrdinalIgnoreCase)) yield return "trackball";
            else if (n.EndsWith("_DIAL", StringComparison.OrdinalIgnoreCase)) yield return "spinner";
            else if (n.EndsWith("_LIGHTGUN", StringComparison.OrdinalIgnoreCase)) yield return "lightgun";
            else if (n.EndsWith("_PADDLE", StringComparison.OrdinalIgnoreCase)) yield return "paddle";
            else if (n.Contains("_PEDAL", StringComparison.OrdinalIgnoreCase)) yield return "pedal";
        }
    }

    /// <summary>From descriptions like "8-way Joystick", "Trackball", "360 Steering Wheel", "Analog Gun".</summary>
    public static IEnumerable<string> FromInfo(string? info)
    {
        if (string.IsNullOrWhiteSpace(info)) yield break;
        var s = info.ToLowerInvariant();
        if (Regex.IsMatch(s, "joystick|triggerstick|flightstick|joy\\d")) yield return "joystick";
        if (s.Contains("trackball")) yield return "trackball";
        if (Regex.IsMatch(s, "spinner|dial|steering|turntable")) yield return "spinner";
        if (s.Contains("gun")) yield return "lightgun";
        if (s.Contains("paddle")) yield return "paddle";
        if (s.Contains("pedal")) yield return "pedal";
    }

    /// <summary>Highest button number lit for any player (P2_BUTTON6 → 6).</summary>
    public static int ButtonCount(LightProfile? profile)
    {
        if (profile is null) return 0;
        int max = 0;
        foreach (var c in profile.Controls)
        {
            var m = Regex.Match(c.Control, @"^P\d_BUTTON(\d+)$", RegexOptions.IgnoreCase);
            if (m.Success) max = Math.Max(max, int.Parse(m.Groups[1].Value));
        }
        return max;
    }

    public static string Join(IEnumerable<string> tags) =>
        string.Join(" ", tags.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(t => Array.IndexOf(All, t)));
}
