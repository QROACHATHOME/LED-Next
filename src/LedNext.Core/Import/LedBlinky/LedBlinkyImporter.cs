using System.Xml;
using System.Xml.Linq;
using LedNext.Core.Games;

namespace LedNext.Core.Import.LedBlinky;

public sealed class LedBlinkyImportResult
{
    public InMemoryGameLibrary Library { get; } = new();
    public ControlMap ControlMap { get; set; } = new();
    /// <summary>Problems in the data (unknown colours, unreadable files).</summary>
    public List<string> Warnings { get; } = new();
    /// <summary>Findings worth showing (mapping surprises, pins LEDBlinky uses that LED-Next doesn't).</summary>
    public List<string> Notes { get; } = new();
    public List<string> FilesRead { get; } = new();

    public int PlatformCount => Library.Platforms.Count;
    public int GameCount => Library.Games.Count;
    public int LitGameCount => Library.Games.Values.Count(g => g.Lighting != GameLighting.PlatformDefault);

    public string Summary =>
        $"{GameCount:N0} games on {PlatformCount} platforms, {LitGameCount:N0} with their own or a parent's lights; " +
        $"{ControlMap.Mappings.Count} controls mapped by input code.";
}

/// <summary>
/// Reads a LEDBlinky install folder (read only) into a game library and a control map.
/// </summary>
/// <remarks>
/// Files used, all optional except that at least one game source must exist:
/// <list type="bullet">
/// <item><c>LEDBlinkyControls.xml</c>: platforms (emulators), their defaults, per-game controls and colours,
///   MAME's default input codes.</item>
/// <item><c>Colors.ini</c>: per-ROM MAME colours (used when LEDBlinkyControls.xml has no entry for the ROM).</item>
/// <item><c>Controls.ini</c>: MAME game names and what each control does.</item>
/// <item><c>LEDBlinkyMinimizedMame.xml</c>: every MAME machine with its parent ROM, so clones inherit.</item>
/// <item><c>Color-RGB.ini</c>: colour names. <c>settings.ini</c>: MAME folder and default options.</item>
/// <item><c>LEDBlinkyInputMap.xml</c> (+ MAME's <c>cfg/default.cfg</c>): which pin answers to which key,
///   for the control map.</item>
/// </list>
/// </remarks>
public static class LedBlinkyImporter
{
    public const string Mame = "MAME";
    private const string ControlsXml = "LEDBlinkyControls.xml";
    private const string ColorsIni = "Colors.ini";
    private const string ControlsIni = "Controls.ini";
    private const string MinimizedMame = "LEDBlinkyMinimizedMame.xml";
    private const string InputMapXml = "LEDBlinkyInputMap.xml";

    /// <param name="mameFolder">
    /// MAME folder whose <c>cfg/default.cfg</c> holds the user's key assignments. Null (the default) uses
    /// <c>MameFolder</c> from LEDBlinky's settings.ini; tests pass a fixed folder so the result does not
    /// depend on which machine they run on.
    /// </param>
    public static LedBlinkyImportResult Import(string folder, PanelLayout panel, WiringMap wiring, IProgress<string>? progress = null,
        string? mameFolder = null)
    {
        if (!Directory.Exists(folder)) throw new DirectoryNotFoundException($"LEDBlinky folder not found: {folder}");
        string P(string file) => Path.Combine(folder, file);
        if (!File.Exists(P(ControlsXml)) && !File.Exists(P(ColorsIni)))
            throw new FileNotFoundException($"Neither {ControlsXml} nor {ColorsIni} is in {folder}. Is this the LEDBlinky folder?");

        var ctx = new Context(new LedBlinkyImportResult());
        var r = ctx.Result;

        IniFile? settings = null;
        if (File.Exists(P("settings.ini")))
        {
            settings = IniFile.Load(P("settings.ini"));
            r.FilesRead.Add("settings.ini");
        }
        bool mameDefaultForOthers = settings?.Get("MAMEConfig", "UseMameDefaultForOtherEmulators") != "0";
        mameFolder ??= settings?.Get("MAMEConfig", "MameFolder");

        ctx.Colors = LedBlinkyColors.Load(P("Color-RGB.ini"));
        if (File.Exists(P("Color-RGB.ini"))) r.FilesRead.Add("Color-RGB.ini");

        var digitalCodes = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        if (File.Exists(P(ControlsXml)))
        {
            progress?.Report($"Reading {ControlsXml}…");
            ReadControlsXml(ctx, P(ControlsXml), mameDefaultForOthers, digitalCodes);
            r.FilesRead.Add(ControlsXml);
        }
        if (r.Library.GetPlatform(Mame) is null)
            r.Library.AddPlatform(new PlatformInfo(Mame, Mame, null, false, Rgb.White));

        if (File.Exists(P(ColorsIni)))
        {
            progress?.Report($"Reading {ColorsIni}…");
            ReadColorsIni(ctx, P(ColorsIni));
            r.FilesRead.Add(ColorsIni);
        }
        if (File.Exists(P(ControlsIni)))
        {
            progress?.Report($"Reading {ControlsIni}…");
            ReadControlsIni(ctx, P(ControlsIni));
            r.FilesRead.Add(ControlsIni);
        }
        if (File.Exists(P(MinimizedMame)))
        {
            progress?.Report($"Reading {MinimizedMame}…");
            ReadMameXml(ctx, P(MinimizedMame));
            r.FilesRead.Add(MinimizedMame);
        }
        else
        {
            r.Notes.Add($"{MinimizedMame} not found: MAME clones will not inherit their parent's lights.");
        }

        progress?.Report("Working out inheritance…");
        ComputeLighting(r.Library);

        if (File.Exists(P(InputMapXml)))
        {
            progress?.Report("Building the control map…");
            ApplyMameCfg(r, mameFolder, digitalCodes);
            var used = r.Library.Lights.Values.SelectMany(l => l.Controls).Select(c => c.Control);
            r.ControlMap = ControlMapBuilder.Build(LedBlinkyInputMap.Load(P(InputMapXml)), digitalCodes, used, wiring, panel, r.Notes);
            r.FilesRead.Add(InputMapXml);
        }
        else
        {
            r.Notes.Add($"{InputMapXml} not found: controls are matched to panel buttons by name only (P1_BUTTON1 → P1_B1).");
        }

        AppLog.Info($"LEDBlinky import from {folder}: {r.Summary} Files: {string.Join(", ", r.FilesRead)}.");
        return r;
    }

    private sealed class Context(LedBlinkyImportResult result)
    {
        public LedBlinkyImportResult Result { get; } = result;
        public LedBlinkyColors Colors { get; set; } = new();
        public HashSet<string> UnknownColors { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Adds a control to a profile, merging collapsed entries (the four joystick directions).</summary>
        public void AddLight(List<ControlLight> list, string rawName, string? colorSpec, string? label, bool alwaysActive, string where)
        {
            var (name, isDigital) = ControlNames.Classify(rawName);
            if (name is null) return;

            if (!Colors.TryParse(colorSpec, out var color))
            {
                if (UnknownColors.Add(colorSpec!.Trim()))
                    Result.Warnings.Add($"Unknown colour '{colorSpec}' (first seen in {where}); the default colour is used.");
                color = null;
            }
            string? colorName = color is null ? null : colorSpec!.Trim();
            // A direction's description ("Rotate Left") is not a label for the whole joystick.
            string? text = isDigital && !string.IsNullOrWhiteSpace(label) ? label.Trim() : null;

            int i = list.FindIndex(c => string.Equals(c.Control, name, StringComparison.OrdinalIgnoreCase));
            if (i < 0)
            {
                list.Add(new ControlLight(name, color, colorName, text, alwaysActive));
                return;
            }
            var existing = list[i];
            list[i] = existing with
            {
                Color = existing.Color ?? color,
                ColorName = existing.Color is null ? colorName : existing.ColorName,
                Label = existing.Label ?? text,
                AlwaysActive = existing.AlwaysActive || alwaysActive,
            };
        }
    }

    // ---- LEDBlinkyControls.xml --------------------------------------------------------------

    private static void ReadControlsXml(Context ctx, string path, bool mameDefaultForOthers, Dictionary<string, HashSet<string>> digitalCodes)
    {
        var lib = ctx.Result.Library;
        var root = XDocument.Load(path).Root ?? throw new InvalidDataException($"{path} is empty.");

        foreach (var c in root.Elements("controlDefaults").Where(d => (string?)d.Attribute("groupName") == Mame).Elements("control"))
        {
            var (name, isDigital) = ControlNames.Classify((string?)c.Attribute("name") ?? "");
            if (name is null || !isDigital) continue;
            if (!digitalCodes.TryGetValue(name, out var set)) digitalCodes[name] = set = new(StringComparer.OrdinalIgnoreCase);
            set.UnionWith(LedBlinkyInputMap.SplitCodes((string?)c.Attribute("inputCodes")));
        }

        foreach (var emu in root.Elements("emulator"))
        {
            var key = ((string?)emu.Attribute("emuname"))?.Trim();
            if (string.IsNullOrEmpty(key)) continue;
            bool isMame = string.Equals(key, Mame, StringComparison.OrdinalIgnoreCase);
            bool usesMame = (string?)emu.Attribute("emulatorIsMAME") == "1";
            var groups = emu.Elements("controlGroup").ToList();
            var defaultGroup = groups.FirstOrDefault(g => (string?)g.Attribute("groupName") == "DEFAULT");

            var defaultActive = Rgb.White;
            var activeSpec = (string?)defaultGroup?.Attribute("defaultActive");
            if (activeSpec is not null && LedBlinkyColors.TryParseTriple(activeSpec, out var a)) defaultActive = a;

            string? parent = !isMame && (mameDefaultForOthers || usesMame) ? Mame : null;
            lib.AddPlatform(new PlatformInfo(isMame ? Mame : key, key.Replace('_', ' '), parent, usesMame, defaultActive));
            var platformKey = isMame ? Mame : key;

            foreach (var group in groups)
            {
                var groupName = ((string?)group.Attribute("groupName"))?.Trim();
                if (string.IsNullOrEmpty(groupName)) continue;
                var lights = new List<ControlLight>();
                var primary = new List<string>();
                foreach (var c in group.Elements("player").Elements("control"))
                {
                    var raw = (string?)c.Attribute("name") ?? "";
                    var voice = (string?)c.Attribute("voice");
                    if (raw.StartsWith("CONTROL_", StringComparison.OrdinalIgnoreCase))
                    {
                        primary.Add(!string.IsNullOrWhiteSpace(voice) ? voice.Trim() : raw[8..].Replace('_', ' '));
                        continue;
                    }
                    ctx.AddLight(lights, raw, (string?)c.Attribute("color"), voice,
                        (string?)c.Attribute("alwaysActive") == "1", $"{key}/{groupName}");
                }

                var profile = new LightProfile { Controls = lights, Source = ControlsXml };
                if (groupName == "DEFAULT")
                {
                    lib.SetLights(platformKey, null, profile);
                    continue;
                }
                lib.SetLights(platformKey, groupName, profile);
                var desc = ((string?)group.Attribute("voice"))?.Trim();
                lib.AddGame(new GameInfo(platformKey, groupName, string.IsNullOrEmpty(desc) ? groupName : desc, null,
                    (int?)group.Attribute("numPlayers") ?? 0, string.Join(", ", primary.Distinct()), ControlsXml, GameLighting.Own));
            }
        }
    }

    // ---- Colors.ini / Controls.ini ----------------------------------------------------------

    private static void ReadColorsIni(Context ctx, string path)
    {
        var lib = ctx.Result.Library;
        int overridden = 0;
        foreach (var (rom, entries) in IniFile.Load(path).Sections)
        {
            if (lib.GetLights(Mame, rom) is not null) { overridden++; continue; }
            var lights = new List<ControlLight>();
            foreach (var (key, value) in entries) ctx.AddLight(lights, key, value, null, false, $"{ColorsIni} [{rom}]");
            lib.SetLights(Mame, rom, new LightProfile { Controls = lights, Source = ColorsIni });
            AddOrUpdateGame(lib, rom, g => g with { Sources = AppendSource(g.Sources, ColorsIni) });
        }
        if (overridden > 0)
            ctx.Result.Notes.Add($"{overridden} MAME games in {ColorsIni} are overridden by {ControlsXml}.");
    }

    private static void ReadControlsIni(Context ctx, string path)
    {
        var lib = ctx.Result.Library;
        foreach (var (rom, entries) in IniFile.Load(path).Sections)
        {
            // Keys can repeat inside a section (e.g. P1_DIAL twice); the last one wins.
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (k, v) in entries) values[k] = v;
            if (!values.ContainsKey("gamename") && !values.ContainsKey("numPlayers")) continue; // not a game section

            var gameName = values.GetValueOrDefault("gamename")?.TrimStart('\'').Trim();
            int.TryParse(values.GetValueOrDefault("numPlayers"), out var players);
            var p1 = values.GetValueOrDefault("P1Controls");
            var info = p1 is null ? "" : p1.Split('+')[0].Trim();
            int.TryParse(values.GetValueOrDefault("P1NumButtons"), out var numButtons);

            AddOrUpdateGame(lib, rom, g => g with
            {
                Description = g.Description == g.Name && !string.IsNullOrEmpty(gameName) ? gameName : g.Description,
                Players = g.Players > 0 ? g.Players : players,
                Info = string.IsNullOrEmpty(g.Info) ? info : g.Info,
                Buttons = g.Buttons > 0 ? g.Buttons : numButtons,
                Sources = AppendSource(g.Sources, ControlsIni),
            });

            var profile = lib.GetLights(Mame, rom);
            if (profile is null) continue;
            foreach (var (key, label) in entries)
            {
                var (name, isDigital) = ControlNames.Classify(key);
                if (name is null || !isDigital || string.IsNullOrWhiteSpace(label)) continue;
                int i = profile.Controls.FindIndex(c => string.Equals(c.Control, name, StringComparison.OrdinalIgnoreCase));
                if (i >= 0 && profile.Controls[i].Label is null)
                    profile.Controls[i] = profile.Controls[i] with { Label = label.Trim() };
            }
        }
    }

    private static void AddOrUpdateGame(InMemoryGameLibrary lib, string rom, Func<GameInfo, GameInfo> update)
    {
        var game = lib.GetGame(Mame, rom) ?? new GameInfo(Mame, rom, rom, null, 0, "", "", GameLighting.PlatformDefault);
        lib.AddGame(update(game));
    }

    private static string AppendSource(string sources, string add) =>
        string.IsNullOrEmpty(sources) ? add : sources.Contains(add, StringComparison.Ordinal) ? sources : $"{sources}, {add}";

    // ---- LEDBlinkyMinimizedMame.xml ---------------------------------------------------------

    private sealed record Machine(string Name, string? Parent, string? Description, int Players);

    private static void ReadMameXml(Context ctx, string path)
    {
        var lib = ctx.Result.Library;
        var machines = new Dictionary<string, Machine>(StringComparer.OrdinalIgnoreCase);
        using (var reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, IgnoreComments = true }))
        {
            reader.MoveToContent();
            if (reader.Name == "mame" && reader.GetAttribute("build") is { } build)
                ctx.Result.Notes.Add($"MAME machine list is from MAME {build}.");
            while (!reader.EOF)
            {
                if (reader.NodeType == XmlNodeType.Element && reader.Name == "machine")
                {
                    var m = (XElement)XNode.ReadFrom(reader);
                    var name = (string?)m.Attribute("name");
                    if (name is null) continue;
                    machines[name] = new Machine(name, (string?)m.Attribute("cloneof"), m.Element("description")?.Value.Trim(),
                        (int?)m.Element("input")?.Attribute("players") ?? 0);
                }
                else reader.Read();
            }
        }

        bool Keep(Machine m, int depth = 0)
        {
            if (lib.GetGame(Mame, m.Name) is not null) return true;
            if (m.Parent is null) return m.Players > 0;
            return depth < 8 && machines.TryGetValue(m.Parent, out var parent) && Keep(parent, depth + 1);
        }

        foreach (var m in machines.Values)
        {
            var existing = lib.GetGame(Mame, m.Name);
            if (existing is not null)
            {
                lib.AddGame(existing with
                {
                    Parent = existing.Parent ?? m.Parent,
                    Description = existing.Description == existing.Name && m.Description is not null ? m.Description : existing.Description,
                    Players = existing.Players > 0 ? existing.Players : m.Players,
                });
                continue;
            }
            if (!Keep(m)) continue;
            var desc = m.Description
                       ?? (m.Parent is not null && machines.TryGetValue(m.Parent, out var p) ? p.Description : null)
                       ?? m.Name;
            lib.AddGame(new GameInfo(Mame, m.Name, desc, m.Parent, m.Players, "", MinimizedMame, GameLighting.PlatformDefault));
        }
    }

    // ---- Inheritance summary and control map --------------------------------------------

    private static void ComputeLighting(InMemoryGameLibrary lib)
    {
        // The game's own lights or the nearest parent's, and where they came from.
        (GameLighting, LightProfile?) For(string platform, string name)
        {
            if (lib.GetLights(platform, name) is { } own) return (GameLighting.Own, own);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { name };
            for (var parent = lib.GetGame(platform, name)?.Parent; parent is not null && seen.Add(parent); parent = lib.GetGame(platform, parent)?.Parent)
                if (lib.GetLights(platform, parent) is { } p) return (GameLighting.Parent, p);
            return (GameLighting.PlatformDefault, null);
        }

        foreach (var g in lib.Games.Values.ToList())
        {
            var (lighting, profile) = For(g.Platform, g.Name);
            if (lighting == GameLighting.PlatformDefault && g.Platform != Mame && lib.GetPlatform(g.Platform)?.UsesMameData == true)
            {
                var (mameLighting, mameProfile) = For(Mame, g.Name);
                if (mameLighting != GameLighting.PlatformDefault) (lighting, profile) = (GameLighting.Parent, mameProfile);
            }

            // Filters for smart lists: buttons used and control types.
            int buttons = ControlTags.ButtonCount(profile);
            if (buttons == 0) buttons = g.Buttons;
            var tags = ControlTags.Join(ControlTags.FromLights(profile).Concat(ControlTags.FromInfo(g.Info)));
            lib.AddGame(g with { Lighting = lighting, Buttons = buttons, Tags = tags });
        }
    }

    /// <summary>The user's own MAME key assignments replace MAME's defaults, if LEDBlinky's MAME folder has them.</summary>
    private static void ApplyMameCfg(LedBlinkyImportResult r, string? mameFolder, Dictionary<string, HashSet<string>> digitalCodes)
    {
        if (string.IsNullOrWhiteSpace(mameFolder)) return;
        var cfg = Path.Combine(mameFolder, "cfg", "default.cfg");
        if (!File.Exists(cfg))
        {
            r.Notes.Add($"MAME key assignments not found at {cfg}; MAME's built-in defaults are used.");
            return;
        }
        int changed = 0;
        foreach (var (type, codes) in LedBlinkyInputMap.LoadMameDefaultCfg(cfg, r.Notes))
        {
            var (name, isDigital) = ControlNames.Classify(type);
            if (name is null || !isDigital || codes.Count == 0) continue;
            digitalCodes[name] = codes.ToHashSet(StringComparer.OrdinalIgnoreCase);
            changed++;
        }
        r.FilesRead.Add(cfg);
        r.Notes.Add($"Using {changed} MAME key assignments from {cfg}.");
    }
}
