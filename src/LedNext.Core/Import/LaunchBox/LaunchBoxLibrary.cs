using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using LedNext.Core.Games;

namespace LedNext.Core.Import.LaunchBox;

/// <summary>One game in the LaunchBox library (read from Data\Platforms\*.xml).</summary>
/// <param name="Rom">File name of ApplicationPath without extension ("sf2"), the usual key for MAME-style platforms.</param>
public sealed record LaunchBoxGame(string Id, string Title, string Platform, string ApplicationPath, string Rom, DateTimeOffset? DateAdded);

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
        foreach (var file in Directory.EnumerateFiles(dir, "*.xml").Order(StringComparer.OrdinalIgnoreCase))
        {
            try { games.AddRange(ReadPlatformFile(file)); }
            catch (Exception ex) when (ex is XmlException or IOException or UnauthorizedAccessException)
            {
                warnings.Add($"Could not read {Path.GetFileName(file)}: {ex.Message}");
            }
        }
        return games;
    }

    public static IEnumerable<LaunchBoxGame> ReadPlatformFile(string file)
    {
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
                list.Add(new LaunchBoxGame(V("ID"), title, V("Platform") is { Length: > 0 } p ? p : fallbackPlatform, path,
                    RomName(path), ParseDate(V("DateAdded"))));
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
    };

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
}
