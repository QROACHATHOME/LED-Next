using System.Text.RegularExpressions;
using System.Xml.Linq;
using LedNext.Core.Games;

namespace LedNext.Core.Import.Mame;

/// <summary>A game's control map for the MAME install it runs in, and what changed against the cabinet's map.</summary>
/// <param name="Note">"keys of Mame02XX Race: P1_BUTTON1 → P1_B4, …", or null when nothing changed.</param>
public sealed record MameKeyLayout(ControlMap Map, string? Note);

/// <summary>
/// Works out which panel buttons a game's controls really are in the MAME install that runs it.
/// </summary>
/// <remarks>
/// The cabinet's <see cref="ControlMap"/> comes from one MAME install (LEDBlinky's <c>MameFolder</c>). Other
/// installs can assign other keys: a racing build may have P1_BUTTON1 on the key of panel button 4. This reads
/// the install's own key assignments the way MAME layers them — the <c>ctrlr</c> file named in <c>mame.ini</c>,
/// then <c>cfg/default.cfg</c>, then the game's own <c>cfg/&lt;rom&gt;.cfg</c> — and follows each key to the
/// panel button that sends it (LEDBlinky's input map). Only player controls (buttons, start, coin) that the
/// install sets explicitly and that land on other buttons than the cabinet's map says are changed; a control
/// whose keys no panel button sends is not lit. Everything else is left as it was, so an install that matches
/// the cabinet's (or one that only sets keys LED-Next can't follow) lights exactly as before.
/// Files are read only, and re-read when they change.
/// </remarks>
public sealed partial class MameKeyLayouts
{
    private readonly IReadOnlyDictionary<string, HashSet<string>> _buttonCodes;
    private readonly object _lock = new();
    private readonly Dictionary<string, (DateTime Stamp, Dictionary<string, Dictionary<string, List<string>>> Systems)> _files =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (string? Ctrlr, string CfgDir)> _folders = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="buttonCodes">Panel button id → input codes it sends (from LEDBlinky's input map and the wiring).</param>
    public MameKeyLayouts(IReadOnlyDictionary<string, HashSet<string>> buttonCodes) => _buttonCodes = buttonCodes;

    public int ButtonCount => _buttonCodes.Count;

    /// <summary>The MAME folder of an emulator program, if it looks like MAME (it has <c>cfg\default.cfg</c> or a <c>mame.ini</c>).</summary>
    public static string? MameFolderOf(string? emulatorPath)
    {
        if (string.IsNullOrWhiteSpace(emulatorPath)) return null;
        try
        {
            var dir = Path.GetDirectoryName(emulatorPath);
            if (dir is null) return null;
            return File.Exists(Path.Combine(dir, "cfg", "default.cfg")) || File.Exists(Path.Combine(dir, "mame.ini")) ? dir : null;
        }
        catch (ArgumentException) { return null; }
    }

    /// <summary>
    /// <paramref name="baseMap"/> with the controls this install (and the game's own cfg) puts on other buttons replaced.
    /// Returns the base map itself when nothing changes.
    /// </summary>
    public MameKeyLayout For(ControlMap baseMap, PanelLayout panel, string mameFolder, string? rom)
    {
        var assigned = Assignments(mameFolder, rom, out var sources);
        if (assigned.Count == 0 || _buttonCodes.Count == 0) return new MameKeyLayout(baseMap, null);

        var map = new ControlMap { Mappings = baseMap.Mappings.ToList() };
        var changes = new List<string>();
        foreach (var (type, (codes, source)) in assigned.OrderBy(a => a.Key, StringComparer.OrdinalIgnoreCase))
        {
            // Like the import: a port set to no key keeps the cabinet's mapping. Only player buttons, start and coin.
            if (codes.Count == 0) continue;
            var (name, isDigital) = ControlNames.Classify(type);
            if (name is null || !isDigital || !PlayerButton().IsMatch(name)) continue;

            var buttons = panel.Buttons.Where(b => _buttonCodes.TryGetValue(b.Id, out var bc) && codes.Any(bc.Contains))
                                       .Select(b => b.Id).ToList();
            var (current, _) = baseMap.Lookup(name, panel);
            if (buttons.Count == 0 && current.Count == 0) continue;
            if (buttons.Count > 0 && buttons.Order(StringComparer.Ordinal).SequenceEqual(current.Order(StringComparer.Ordinal))) continue;
            // START / COIN keys are usually not in LEDBlinky's input map: no match there says nothing.
            if (buttons.Count == 0 && !AnyButtonHasCodesFor(current)) continue;

            var keys = codes.Count == 0 ? "no key" : string.Join(" or ", codes);
            map.Mappings.RemoveAll(m => string.Equals(m.Control, name, StringComparison.OrdinalIgnoreCase));
            map.Mappings.Add(new ControlMapping
            {
                Control = name,
                Buttons = buttons,
                NotOnPanel = buttons.Count == 0,
                Source = buttons.Count > 0 ? $"{keys} in {source}" : $"{keys} in {source}: no panel button sends it",
            });
            changes.Add($"{name} → {(buttons.Count > 0 ? string.Join("+", buttons) : "not on the panel")}");
        }
        if (changes.Count == 0) return new MameKeyLayout(baseMap, null);
        return new MameKeyLayout(map, $"keys of {string.Join(" + ", sources)}: {string.Join(", ", changes)}");
    }

    private bool AnyButtonHasCodesFor(IReadOnlyList<string> buttons) => buttons.Any(_buttonCodes.ContainsKey);

    /// <summary>MAME port type → (input codes, where it was set), in MAME's order: ctrlr, default.cfg, the game's cfg.</summary>
    private Dictionary<string, (List<string> Codes, string Source)> Assignments(string folder, string? rom, out List<string> sources)
    {
        var result = new Dictionary<string, (List<string>, string)>(StringComparer.OrdinalIgnoreCase);
        var used = new List<string>();
        sources = used;
        var name = Path.GetFileName(folder.TrimEnd('\\', '/'));
        var (ctrlr, cfgDir) = Options(folder);

        void Layer(string path, string system, string label)
        {
            var systems = ReadFile(path);
            if (systems is null || !systems.TryGetValue(system, out var ports) || ports.Count == 0) return;
            foreach (var (type, codes) in ports) result[type] = (codes, label);
            if (!used.Contains(label)) used.Add(label);
        }

        if (ctrlr is not null)
        {
            var ctrlrFile = Path.Combine(folder, "ctrlr", ctrlr + ".cfg");
            Layer(ctrlrFile, "default", $"{name} ({ctrlr}.cfg)");
            if (!string.IsNullOrEmpty(rom)) Layer(ctrlrFile, rom, $"{name} ({ctrlr}.cfg)");
        }
        Layer(Path.Combine(cfgDir, "default.cfg"), "default", name);
        if (!string.IsNullOrEmpty(rom)) Layer(Path.Combine(cfgDir, rom + ".cfg"), rom, $"{rom}.cfg");
        return result;
    }

    /// <summary>The <c>ctrlr</c> option and the cfg folder from the install's mame.ini (folder or ini\ subfolder).</summary>
    private (string? Ctrlr, string CfgDir) Options(string folder)
    {
        lock (_lock)
            if (_folders.TryGetValue(folder, out var cached)) return cached;
        string? ctrlr = null;
        string cfgDir = Path.Combine(folder, "cfg");
        foreach (var ini in new[] { Path.Combine(folder, "mame.ini"), Path.Combine(folder, "ini", "mame.ini") })
        {
            if (!File.Exists(ini)) continue;
            try
            {
                foreach (var line in File.ReadLines(ini))
                {
                    var m = IniOption().Match(line);
                    if (!m.Success) continue;
                    var value = m.Groups[2].Value.Trim().Trim('"');
                    if (m.Groups[1].Value.Equals("ctrlr", StringComparison.OrdinalIgnoreCase))
                        ctrlr = value.Length > 0 ? value : null;
                    else if (value.Length > 0 && !value.Contains(';'))
                        cfgDir = Path.IsPathRooted(value) ? value : Path.Combine(folder, value);
                }
            }
            catch (IOException) { }
            break;
        }
        lock (_lock) _folders[folder] = (ctrlr, cfgDir);
        return (ctrlr, cfgDir);
    }

    /// <summary>A MAME cfg file: system name → port type → input codes. Null when missing or unreadable. Cached by file time.</summary>
    private Dictionary<string, Dictionary<string, List<string>>>? ReadFile(string path)
    {
        DateTime stamp;
        try
        {
            if (!File.Exists(path)) return null;
            stamp = File.GetLastWriteTimeUtc(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return null; }
        lock (_lock)
            if (_files.TryGetValue(path, out var cached) && cached.Stamp == stamp) return cached.Systems;

        var systems = new Dictionary<string, Dictionary<string, List<string>>>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var system in XDocument.Load(path).Descendants("system"))
            {
                var sysName = (string?)system.Attribute("name");
                if (sysName is null) continue;
                if (!systems.TryGetValue(sysName, out var ports)) systems[sysName] = ports = new(StringComparer.OrdinalIgnoreCase);
                foreach (var port in system.Elements("input").Elements("port"))
                {
                    var type = (string?)port.Attribute("type");
                    var seq = port.Elements("newseq").FirstOrDefault(e => (string?)e.Attribute("type") == "standard");
                    if (type is null || seq is null) continue;
                    ports[type] = Codes(seq.Value);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            AppLog.Warn($"Could not read MAME's {path}: {ex.Message}");
            return null;
        }
        lock (_lock) _files[path] = (stamp, systems);
        return systems;
    }

    /// <summary>
    /// The codes that press a control in a MAME input sequence: "KEYCODE_A OR JOYCODE_1_BUTTON1" → both;
    /// a code after NOT is a condition, not a key, and "NONE" / "DEFAULT" give nothing.
    /// </summary>
    public static List<string> Codes(string sequence)
    {
        var codes = new List<string>();
        bool not = false;
        foreach (var token in sequence.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var t = token.ToUpperInvariant();
            if (t == "NOT") { not = true; continue; }
            if (CodeToken().IsMatch(t) && !not && !codes.Contains(t)) codes.Add(t);
            not = false;
        }
        return codes;
    }

    [GeneratedRegex(@"^(KEYCODE|JOYCODE|MOUSECODE|GUNCODE)_[A-Z0-9_]+$")]
    private static partial Regex CodeToken();

    [GeneratedRegex(@"^P\d_(BUTTON\d+|START|COIN)$")]
    private static partial Regex PlayerButton();

    [GeneratedRegex(@"^\s*(ctrlr|cfg_directory)\s+(.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex IniOption();
}
