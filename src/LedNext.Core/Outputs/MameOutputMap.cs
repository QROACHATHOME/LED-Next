using LedNext.Core.Games;
using LedNext.Core.Import;

namespace LedNext.Core.Outputs;

/// <summary>
/// Which panel controls MAME's outputs (lamps, LEDs) drive, from LEDBlinky's <c>MameOutputs.ini</c> (read only):
/// a <c>[rom]</c> section per game, else <c>[default]</c>; <c>LED0=Start1</c>, repeated keys for more controls.
/// Only control names are used; LEDBlinky's hardware ports, Pixelcade and web requests are skipped.
/// Without the file: LED0 → P1_START, LED1 → P2_START (LEDBlinky's default).
/// </summary>
public sealed class MameOutputMap
{
    private const string DefaultSection = "default";
    private readonly Dictionary<string, Dictionary<string, List<string>>> _sections = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Entries that were not control names (hardware ports, Pixelcade, web requests).</summary>
    public int Skipped { get; private set; }
    public string Source { get; private set; } = "built-in default";

    public static MameOutputMap BuiltIn()
    {
        var map = new MameOutputMap();
        map.Add(DefaultSection, "LED0", "P1_START");
        map.Add(DefaultSection, "LED1", "P2_START");
        return map;
    }

    /// <summary>Reads the file; null when it doesn't exist.</summary>
    public static MameOutputMap? Load(string path)
    {
        if (!File.Exists(path)) return null;
        return Parse(IniFile.Load(path), path);
    }

    public static MameOutputMap Parse(IniFile ini, string source = "MameOutputs.ini")
    {
        var map = new MameOutputMap { Source = source };
        foreach (var (section, entries) in ini.Sections)
            foreach (var (output, value) in entries)
            {
                var v = value.Trim();
                // Hardware ports ("PL,1,28,48"), Pixelcade and web requests all contain commas.
                if (v.Length == 0 || v.Contains(',') || ControlNames.Normalize(v) is not { } control) { map.Skipped++; continue; }
                map.Add(section, output, control);
            }
        if (!map._sections.ContainsKey(DefaultSection))
            foreach (var (output, controls) in BuiltIn()._sections[DefaultSection])
                foreach (var c in controls) map.Add(DefaultSection, output, c);
        return map;
    }

    private void Add(string section, string output, string control)
    {
        if (!_sections.TryGetValue(section.Trim(), out var outputs))
            _sections[section.Trim()] = outputs = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        if (!outputs.TryGetValue(output.Trim(), out var list)) outputs[output.Trim()] = list = new List<string>();
        if (!list.Contains(control, StringComparer.OrdinalIgnoreCase)) list.Add(control);
    }

    /// <summary>
    /// The section a game uses: the first of <paramref name="romChain"/> (the rom, then its parents) that has
    /// one, else [default]. Returns its name.
    /// </summary>
    public string SectionFor(IEnumerable<string> romChain)
    {
        foreach (var rom in romChain)
            if (!string.Equals(rom, DefaultSection, StringComparison.OrdinalIgnoreCase) && _sections.ContainsKey(rom))
                return _sections.Keys.First(k => string.Equals(k, rom, StringComparison.OrdinalIgnoreCase));
        return DefaultSection;
    }

    /// <summary>The controls an output drives in a section (empty when it drives none).</summary>
    public IReadOnlyList<string> ControlsFor(string section, string output) =>
        _sections.TryGetValue(section, out var outputs) && outputs.TryGetValue(output, out var list) ? list : Array.Empty<string>();
}
