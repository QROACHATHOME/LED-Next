using System.Globalization;

namespace LedNext.Core.Import.LedBlinky;

/// <summary>
/// LEDBlinky colour values. Colours are names from <c>Color-RGB.ini</c> ("Red") or "r,g,b"
/// intensities. LEDBlinky intensities run 0-48 (PacLED64 hardware levels); here they are scaled
/// to 0-255 so they go through LED-Next's normal brightness and gamma.
/// </summary>
public sealed class LedBlinkyColors
{
    public const int MaxIntensity = 48;

    // LEDBlinky 8.3's shipped Color-RGB.ini, used when the file is missing.
    private static readonly (string Name, int R, int G, int B)[] BuiltIn =
    {
        ("Black", 5, 5, 5), ("White", 48, 48, 48), ("Red", 48, 0, 0), ("Yellow", 48, 48, 0),
        ("Orange", 48, 24, 0), ("Lime", 0, 48, 0), ("Green", 0, 24, 0), ("Cyan", 0, 48, 48),
        ("Blue", 0, 0, 48), ("Purple", 12, 0, 24), ("Violet", 24, 0, 48), ("Magenta", 48, 0, 48),
        ("Brown", 24, 12, 0),
    };

    private readonly Dictionary<string, Rgb> _named = new(StringComparer.OrdinalIgnoreCase);

    public LedBlinkyColors()
    {
        foreach (var c in BuiltIn) _named[c.Name] = FromIntensity(c.R, c.G, c.B);
    }

    public IReadOnlyDictionary<string, Rgb> Named => _named;

    /// <summary>Built-in colours, overridden/extended by the [Colors] section of Color-RGB.ini if it exists.</summary>
    public static LedBlinkyColors Load(string? colorRgbIniPath)
    {
        var colors = new LedBlinkyColors();
        if (colorRgbIniPath is null || !File.Exists(colorRgbIniPath)) return colors;
        var section = IniFile.Load(colorRgbIniPath).Section("Colors");
        if (section is null) return colors;
        foreach (var (name, value) in section)
            if (TryParseTriple(value, out var rgb)) colors._named[name] = rgb;
        return colors;
    }

    /// <summary>
    /// Parses a colour value. Empty → true with a null colour (not specified). Unknown name or bad
    /// numbers → false.
    /// </summary>
    public bool TryParse(string? spec, out Rgb? color)
    {
        color = null;
        if (string.IsNullOrWhiteSpace(spec)) return true;
        spec = spec.Trim();
        if (_named.TryGetValue(spec, out var named)) { color = named; return true; }
        if (TryParseTriple(spec, out var rgb)) { color = rgb; return true; }
        return false;
    }

    /// <summary>"r,g,b" or "r,g,b,i" with 0-48 intensities (a fourth value is ignored).</summary>
    public static bool TryParseTriple(string spec, out Rgb color)
    {
        color = default;
        var parts = spec.Split(',');
        if (parts.Length is < 3 or > 4) return false;
        var v = new int[3];
        for (int i = 0; i < 3; i++)
            if (!int.TryParse(parts[i].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v[i])) return false;
        color = FromIntensity(v[0], v[1], v[2]);
        return true;
    }

    public static Rgb FromIntensity(int r, int g, int b) => new(Scale(r), Scale(g), Scale(b));

    private static byte Scale(int v) => (byte)Math.Round(Math.Clamp(v, 0, MaxIntensity) * 255.0 / MaxIntensity);
}
