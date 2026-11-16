using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using LedNext.Core.Games;

namespace LedNext.Core.Import.LaunchBox;

/// <summary>One game in the LaunchBox library (read from Data\Platforms\*.xml).</summary>
/// <param name="Rom">File name of ApplicationPath without extension ("sf2"), the usual key for MAME-style platforms.</param>
/// <param name="Genre">LaunchBox's genre text, e.g. "Fighting" or "Platform; Shooter" (may be empty).</param>
/// <param name="EmulatorId">The game's own emulator (LaunchBox id), empty when it uses its platform's default.</param>
/// <param name="EmulatorPath">Full path of the emulator program that runs the game (its own, else the platform's default); empty if unknown.</param>
public sealed record LaunchBoxGame(string Id, string Title, string Platform, string ApplicationPath, string Rom, DateTimeOffset? DateAdded,
    string Genre = "", string EmulatorId = "", string EmulatorPath = "");

/// <summary>LaunchBox's emulators (Data\Emulators.xml): id → full program path, and each platform's default emulator id.</summary>
public sealed record LaunchBoxEmulators(IReadOnlyDictionary<string, string> Paths, IReadOnlyDictionary<string, string> PlatformDefaults)
{
    public static readonly LaunchBoxEmulators None = new(new Dictionary<string, string>(), new Dictionary<string, string>());

    /// <summary>The program that runs a game: its own emulator, else its platform's default.</summary>
    public string PathFor(string emulatorId, string platform)
    {
        if (emulatorId.Length > 0 && Paths.TryGetValue(emulatorId, out var own)) return own;
        return PlatformDefaults.TryGetValue(platform, out var id) && Paths.TryGetValue(id, out var path) ? path : "";
    }
}

/// <summary>A LaunchBox game matched to an LED-Next platform and game name.</summary>
/// <param name="MatchedBy">"rom name", "title", or "new" (not in the LED-Next library yet).</param>
public sealed record LaunchBoxMatch(LaunchBoxGame Game, string LedPlatform, string LedGame, string MatchedBy);

/// <summary>
/// Reads a LaunchBox install (read only). Unverified against a real LaunchBox 14 library: written
/// from the documented XML layout (Data\Platforms\&lt;Platform&gt;.xml, one &lt;Game&gt; per game).
/// </summary>
public static class LaunchBoxLibrary
{
    public static string PlatformsFolder(string launchBoxFolder) => Path.Combine(launchBoxFolder, "Data", "Platforms");

    public static bool LooksLikeLaunchBox(string folder) => Directory.Exists(PlatformsFolder(folder));

    /// <summary>Every game in every platform file. Unreadable files are reported in <paramref name="warnings"/>.</summary>
    public static List<LaunchBoxGame> Read(string launchBoxFolder, List<string> warnings)
    {
        var dir = PlatformsFolder(launchBoxFolder);
        if (!Directory.Exists(dir)) throw new DirectoryNotFoundException($"No LaunchBox platform data at {dir}. Is this the LaunchBox folder?");
        var games = new List<LaunchBoxGame>();
        var emulators = EmulatorExeNames(launchBoxFolder, warnings);
        var programs = ReadEmulators(launchBoxFolder, warnings);
        foreach (var file in Directory.EnumerateFiles(dir, "*.xml").Order(StringComparer.OrdinalIgnoreCase))
        {
            try { games.AddRange(ReadPlatformFile(file, emulators).Select(g => g with { EmulatorPath = programs.PathFor(g.EmulatorId, g.Platform) })); }
            catch (Exception ex) when (ex is XmlException or IOException or UnauthorizedAccessException)
            {
                warnings.Add($"Could not read {Path.GetFileName(file)}: {ex.Message}");
            }
        }
        return games;
    }

    /// <summary>Emulator programs that a LaunchBox game's ApplicationPath must not be taken as a rom name.</summary>
    public static readonly string[] BuiltInEmulatorExes =
    {
        "mame.exe", "mame64.exe", "mameui.exe", "mameui64.exe", "retroarch.exe", "fbneo.exe", "fbneo64.exe",
        "daphne.exe", "hypseus.exe", "singe.exe", "demul.exe", "flycast.exe", "teknoparrotui.exe",
    };

    /// <summary>
    /// File names (with extension, any case) of every emulator in <c>Data\Emulators.xml</c>, plus
    /// <see cref="BuiltInEmulatorExes"/>. A game whose ApplicationPath is one of these has no rom name.
    /// </summary>
    public static HashSet<string> EmulatorExeNames(string launchBoxFolder, List<string>? warnings = null)
    {
        var set = new HashSet<string>(BuiltInEmulatorExes, StringComparer.OrdinalIgnoreCase);
        var file = Path.Combine(launchBoxFolder, "Data", "Emulators.xml");
        if (!File.Exists(file)) return set;
        try
        {
            using var reader = XmlReader.Create(file, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, IgnoreComments = true });
            while (reader.ReadToFollowing("Emulator"))
            {
                var e = (XElement)XNode.ReadFrom(reader);
                var exe = FileName(e.Element("ApplicationPath")?.Value ?? "");
                if (exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) set.Add(exe);
            }
        }
        catch (Exception ex) when (ex is XmlException or IOException or UnauthorizedAccessException)
        {
            warnings?.Add($"Could not read Emulators.xml: {ex.Message}");
        }
        return set;
    }

    /// <summary>
    /// Every emulator's program (relative paths are relative to the LaunchBox folder) and each platform's default
    /// emulator, from <c>Data\Emulators.xml</c>. Empty when the file is missing or unreadable.
    /// </summary>
    public static LaunchBoxEmulators ReadEmulators(string launchBoxFolder, List<string>? warnings = null)
    {
        var file = Path.Combine(launchBoxFolder, "Data", "Emulators.xml");
        if (!File.Exists(file)) return LaunchBoxEmulators.None;
        var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var defaults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var reader = XmlReader.Create(file, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, IgnoreComments = true });
            reader.MoveToContent();
            while (!reader.EOF)
            {
                if (reader.NodeType == XmlNodeType.Element && reader.Name is "Emulator" or "EmulatorPlatform")
                {
                    var e = (XElement)XNode.ReadFrom(reader);
                    string V(string name) => e.Element(name)?.Value.Trim() ?? "";
                    if (e.Name == "Emulator")
                    {
                        if (V("ID").Length > 0 && V("ApplicationPath").Length > 0)
                            paths[V("ID")] = FullPath(launchBoxFolder, V("ApplicationPath"));
                    }
                    else if (V("Default").Equals("true", StringComparison.OrdinalIgnoreCase) && V("Platform").Length > 0)
                    {
                        defaults[V("Platform")] = V("Emulator");
                    }
                }
                else reader.Read();
            }
        }
        catch (Exception ex) when (ex is XmlException or IOException or UnauthorizedAccessException)
        {
            warnings?.Add($"Could not read Emulators.xml: {ex.Message}");
        }
        return new LaunchBoxEmulators(paths, defaults);
    }

    private static string FullPath(string baseFolder, string path)
    {
        try { return Path.GetFullPath(Path.Combine(baseFolder, path)); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return path; }
    }

    public static IEnumerable<LaunchBoxGame> ReadPlatformFile(string file, IReadOnlySet<string>? emulatorExes = null)
    {
        emulatorExes ??= new HashSet<string>(BuiltInEmulatorExes, StringComparer.OrdinalIgnoreCase);
        var fallbackPlatform = Path.GetFileNameWithoutExtension(file);
        using var reader = XmlReader.Create(file, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, IgnoreComments = true });
        reader.MoveToContent();
        var list = new List<LaunchBoxGame>();
        while (!reader.EOF)
        {
            if (reader.NodeType == XmlNodeType.Element && reader.Name == "Game")
            {
                var g = (XElement)XNode.ReadFrom(reader);
                string V(string name) => g.Element(name)?.Value.Trim() ?? "";
                var title = V("Title");
                var path = V("ApplicationPath");
                if (title.Length == 0 && path.Length == 0) continue;
                // A game that points at the emulator itself ("…\mame.exe") has no rom name; it is matched by title.
                var rom = emulatorExes.Contains(FileName(path)) ? "" : RomName(path);
                list.Add(new LaunchBoxGame(V("ID"), title, V("Platform") is { Length: > 0 } p ? p : fallbackPlatform, path,
                    rom, ParseDate(V("DateAdded")), V("Genre"), V("Emulator")));
            }
            else reader.Read();
        }
        return list;
    }

    /// <summary>"..\Games\MAME\sf2.zip" → "sf2"; a folder or empty path → "".</summary>
    public static string RomName(string applicationPath)
    {
        if (string.IsNullOrWhiteSpace(applicationPath)) return "";
        var name = applicationPath.Trim().TrimEnd('\\', '/');
        int slash = name.LastIndexOfAny(new[] { '\\', '/' });
        if (slash >= 0) name = name[(slash + 1)..];
        int dot = name.LastIndexOf('.');
        return dot > 0 ? name[..dot] : name;
    }

    private static string FileName(string path)
    {
        var name = path.Trim().TrimEnd('\\', '/');
        int slash = name.LastIndexOfAny(new[] { '\\', '/' });
        return slash >= 0 ? name[(slash + 1)..] : name;
    }

    public static DateTimeOffset? ParseDate(string text) =>
        DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var d) ? d : null;
}

/// <summary>
/// Maps LaunchBox platform names to LED-Next (LEDBlinky) platform keys: "Sega Genesis" → "Sega_Genesis",
/// "Arcade" → "MAME". User overrides win; a LaunchBox platform with no match gets its own key.
/// </summary>
public static class PlatformMatcher
{
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Arcade"] = "MAME",
        ["MAME"] = "MAME",
        ["Arcade MAME"] = "MAME",
        ["SNK Neo Geo AES"] = "SNK_Neo_Geo_MVS",
        ["SNK Neo Geo"] = "SNK_Neo_Geo_MVS",
        ["TeknoParrot"] = "TEKNO_PARROT",
        ["Sega Mega Drive"] = "Sega_Genesis",

        // EmulationStation's system short names (ES-DE, RetroBat / Batocera; also RetroFE launcher names).
        ["arcade"] = "MAME", ["mame"] = "MAME", ["fbneo"] = "MAME", ["fba"] = "MAME", ["cps1"] = "MAME", ["cps2"] = "MAME",
        ["cps3"] = "MAME", ["neogeo"] = "SNK_Neo_Geo_MVS", ["naomi"] = "Sega_Naomi", ["naomi2"] = "Sega_Naomi",
        ["atomiswave"] = "Sammy_Atomiswave", ["model2"] = "Sega_Model_2", ["model3"] = "Sega_Model_3",
        ["triforce"] = "Sega_Triforce", ["daphne"] = "Daphne", ["laserdisc"] = "Daphne", ["teknoparrot"] = "TEKNO_PARROT",
        ["nes"] = "Nintendo_Entertainment_System", ["famicom"] = "Nintendo_Famicom", ["fds"] = "Nintendo_Famicom_Disk_System",
        ["snes"] = "Super_Nintendo_Entertainment_System", ["sfc"] = "Nintendo_Super_Famicom", ["n64"] = "Nintendo_64",
        ["n64dd"] = "Nintendo_64DD", ["gc"] = "Nintendo_GameCube", ["gamecube"] = "Nintendo_GameCube", ["wii"] = "Nintendo_Wii",
        ["gb"] = "Nintendo_Game_Boy", ["gbc"] = "Nintendo_Game_Boy_Color", ["gba"] = "Nintendo_Game_Boy_Advance",
        ["nds"] = "Nintendo_DS", ["virtualboy"] = "Nintendo_Virtual_Boy", ["pokemini"] = "Nintendo_Pokemon_Mini",
        ["genesis"] = "Sega_Genesis", ["megadrive"] = "Sega_Genesis", ["mastersystem"] = "Sega_Master_System",
        ["markiii"] = "Sega_Mark_III", ["gamegear"] = "Sega_Game_Gear", ["segacd"] = "Sega_CD", ["megacd"] = "Sega_CD",
        ["sega32x"] = "Sega_32X", ["saturn"] = "Sega_Saturn", ["dreamcast"] = "Sega_Dreamcast", ["sg-1000"] = "Sega_SG-1000",
        ["sg1000"] = "Sega_SG-1000", ["psx"] = "Sony_Playstation", ["psp"] = "Sony_PSP", ["pcengine"] = "NEC_PC_Engine",
        ["tg16"] = "NEC_TurboGrafx-16", ["pcenginecd"] = "NEC_PC_Engine-CD", ["tg-cd"] = "NEC_TurboGrafx-CD",
        ["supergrafx"] = "NEC_SuperGrafx", ["pcfx"] = "NEC_PC-FX", ["atari2600"] = "Atari_2600", ["atari5200"] = "Atari_5200",
        ["atari7800"] = "Atari_7800", ["atari800"] = "Atari_8-bit", ["lynx"] = "Atari_Lynx", ["atarilynx"] = "Atari_Lynx",
        ["jaguar"] = "Atari_Jaguar", ["atarijaguar"] = "Atari_Jaguar", ["jaguarcd"] = "Atari_Jaguar_CD", ["atarist"] = "Atari_ST",
        ["colecovision"] = "ColecoVision", ["vectrex"] = "GCE_Vectrex", ["3do"] = "Panasonic_3DO", ["neogeocd"] = "SNK_Neo_Geo_CD",
        ["ngp"] = "SNK_Neo_Geo_Pocket", ["ngpc"] = "SNK_Neo_Geo_Pocket_Color", ["wswan"] = "Bandai_WonderSwan",
        ["wonderswan"] = "Bandai_WonderSwan", ["wswanc"] = "Bandai_WonderSwan_Color", ["wonderswancolor"] = "Bandai_WonderSwan_Color",
        ["msx"] = "Microsoft_MSX", ["msx1"] = "Microsoft_MSX", ["msx2"] = "Microsoft_MSX2", ["c64"] = "Commodore_64",
        ["amiga"] = "Commodore_Amiga", ["amiga500"] = "Commodore_Amiga", ["amigacd32"] = "Commodore_Amiga_CD32",
        ["cd32"] = "Commodore_Amiga_CD32", ["amstradcpc"] = "Amstrad_CPC", ["zxspectrum"] = "Sinclair_ZX_Spectrum",
        ["zx81"] = "Sinclair_ZX81", ["x68000"] = "Sharp_X68000", ["dos"] = "Microsoft_MS-DOS", ["scummvm"] = "ScummVM",
        ["openbor"] = "OpenBOR", ["mugen"] = "MUGEN",

        // Pinball front ends (PinballX, PinUP Popper) and ES's pinball systems.
        ["vpinball"] = "Visual_Pinball", ["vpx"] = "Visual_Pinball", ["visualpinball"] = "Visual_Pinball",
        ["Visual Pinball X"] = "Visual_Pinball", ["fpinball"] = "Future_Pinball", ["futurepinball"] = "Future_Pinball",
        ["Pinball FX"] = "Pinball_FX2", ["Pinball FX3"] = "Pinball_FX2",
    };

    /// <summary>
    /// Arcade platforms that LEDBlinky has no data for but whose games use MAME rom names (Cave runs
    /// FBNeo, ZiNc runs MAME). A new LED-Next platform made for one of these uses MAME's game data.
    /// </summary>
    private static readonly HashSet<string> MameRomsetPlatforms = new(StringComparer.OrdinalIgnoreCase)
    {
        "Cave", "ZiNc", "Final Burn Neo", "FBNeo", "FinalBurn Neo", "FinalBurn Alpha", "Final Burn Alpha",
    };

    /// <summary>True when a new platform for this LaunchBox platform should use MAME's game data.</summary>
    public static bool UsesMameDataByDefault(string launchBoxPlatform) => MameRomsetPlatforms.Contains(launchBoxPlatform.Trim());

    /// <summary>The LED-Next platform key and how it was found ("your mapping", "same name", "alias", "new").</summary>
    public static (string Key, string How) Match(string launchBoxPlatform, IEnumerable<string> ledPlatforms, IReadOnlyDictionary<string, string>? overrides)
    {
        if (overrides is not null && overrides.TryGetValue(launchBoxPlatform, out var forced) && !string.IsNullOrWhiteSpace(forced))
            return (forced, "your mapping");
        var keys = ledPlatforms.ToList();
        var norm = Normalize(launchBoxPlatform);
        var same = keys.FirstOrDefault(k => Normalize(k) == norm);
        if (same is not null) return (same, "same name");
        if (Aliases.TryGetValue(launchBoxPlatform, out var alias) && keys.Contains(alias, StringComparer.OrdinalIgnoreCase))
            return (alias, "alias");
        return (launchBoxPlatform.Trim().Replace(' ', '_'), "new");
    }

    /// <summary>Lower case letters and digits only: "Sega_Genesis" and "Sega Genesis" compare equal.</summary>
    public static string Normalize(string s) => new(s.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}

/// <summary>Finds the LED-Next game for each LaunchBox game.</summary>
public static class GameMatcher
{
    /// <param name="titleIndex">Per platform: normalised description → game name, for title matches.</param>
    public static LaunchBoxMatch Match(LaunchBoxGame g, string platform, IGameLibrary library,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> titleIndex)
    {
        if (g.Rom.Length > 0 && library.GetGame(platform, g.Rom) is not null)
            return new LaunchBoxMatch(g, platform, g.Rom, "rom name");
        if (titleIndex.TryGetValue(platform, out var titles) && titles.TryGetValue(PlatformMatcher.Normalize(g.Title), out var byTitle))
            return new LaunchBoxMatch(g, platform, byTitle, "title");
        // MAME-based platforms still find MAME's data by rom name through the resolver.
        var name = g.Rom.Length > 0 ? g.Rom : g.Title;
        return new LaunchBoxMatch(g, platform, name, "new");
    }

    /// <summary>
    /// True when a LaunchBox title and a library description name the same game: equal after dropping
    /// bracketed parts ("(version 1.6)", "[Rev A]"), a leading "The", and anything but letters and digits.
    /// Strict on purpose: "Star Wars: Battle Pod" is not "Star Wars".
    /// </summary>
    public static bool SameTitle(string launchBoxTitle, string description)
    {
        static string Key(string s)
        {
            s = System.Text.RegularExpressions.Regex.Replace(s, @"\([^)]*\)|\[[^\]]*\]", " ").Trim();
            if (s.StartsWith("The ", StringComparison.OrdinalIgnoreCase)) s = s[4..];
            return PlatformMatcher.Normalize(s);
        }
        var a = Key(launchBoxTitle);
        return a.Length > 0 && a == Key(description);
    }
}
