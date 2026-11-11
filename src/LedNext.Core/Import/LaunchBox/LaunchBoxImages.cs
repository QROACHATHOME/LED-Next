using System.Text.RegularExpressions;
using System.Xml;

namespace LedNext.Core.Import.LaunchBox;

/// <summary>
/// Finds a game's pictures in a LaunchBox install (read only): the media type's folder from
/// <c>Data\Platforms.xml</c> (else <c>Images\&lt;platform&gt;\&lt;media type&gt;</c>), searched with region
/// subfolders. LaunchBox names files after the title with characters a file name can't hold replaced
/// ("Street Fighter II_ The World Warrior-01.png"), so titles are compared by letters and digits only.
/// </summary>
public sealed partial class LaunchBoxImages
{
    public const string ControlPanel = "Arcade - Control Panel";
    public const string ControlsInformation = "Arcade - Controls Information";

    private static readonly string[] Extensions = { ".png", ".jpg", ".jpeg", ".gif", ".bmp" };
    private readonly string _launchBoxFolder;
    private readonly Dictionary<(string Platform, string MediaType), string> _folders = new();
    private readonly Dictionary<string, Dictionary<string, List<string>>> _indexes = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    public LaunchBoxImages(string launchBoxFolder)
    {
        _launchBoxFolder = launchBoxFolder;
        ReadPlatformFolders(Path.Combine(launchBoxFolder, "Data", "Platforms.xml"));
    }

    public string LaunchBoxFolder => _launchBoxFolder;

    /// <summary>The folder LaunchBox keeps this platform's pictures of this type in.</summary>
    public string FolderFor(string platform, string mediaType)
    {
        var path = _folders.TryGetValue((platform.ToLowerInvariant(), mediaType.ToLowerInvariant()), out var configured)
            ? configured
            : Path.Combine("Images", platform, mediaType);
        return Path.IsPathRooted(path) ? path : Path.Combine(_launchBoxFolder, path);
    }

    /// <summary>
    /// The game's pictures of one media type, best first: files directly in the folder before region
    /// subfolders, then by number (-01, -02…). Empty when there are none or the folder is missing.
    /// </summary>
    public IReadOnlyList<string> Find(string platform, string title, string mediaType)
    {
        if (string.IsNullOrWhiteSpace(platform) || string.IsNullOrWhiteSpace(title)) return Array.Empty<string>();
        var folder = FolderFor(platform, mediaType);
        var index = Index(folder);
        return index.TryGetValue(Key(title), out var files) ? files : Array.Empty<string>();
    }

    /// <summary>Forgets the folder listings (after LaunchBox was read again or pictures were added).</summary>
    public void Refresh()
    {
        lock (_lock) _indexes.Clear();
    }

    /// <summary>Title → files for one folder, listed once and kept.</summary>
    private Dictionary<string, List<string>> Index(string folder)
    {
        lock (_lock)
            if (_indexes.TryGetValue(folder, out var cached)) return cached;

        var index = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        if (Directory.Exists(folder))
        {
            try
            {
                var files = Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories)
                    .Where(f => Extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase));
                foreach (var file in files)
                {
                    var key = Key(StripSuffix(Path.GetFileNameWithoutExtension(file)));
                    if (key.Length == 0) continue;
                    if (!index.TryGetValue(key, out var list)) index[key] = list = new List<string>();
                    list.Add(file);
                }
                foreach (var list in index.Values)
                    list.Sort((a, b) => Rank(folder, a).CompareTo(Rank(folder, b)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Warn($"LaunchBox pictures in {folder} could not be listed: {ex.Message}");
            }
        }
        lock (_lock) _indexes[folder] = index;
        return index;
    }

    /// <summary>"Street Fighter II_ The World Warrior-01" → "Street Fighter II_ The World Warrior"; a ".&lt;guid&gt;" is dropped too.</summary>
    public static string StripSuffix(string name)
    {
        var m = NumberSuffix().Match(name);
        if (m.Success) name = name[..m.Index];
        return GuidSuffix().Replace(name, "");
    }

    /// <summary>Letters and digits, lower case: how a title and its file name are compared.</summary>
    public static string Key(string title) => new(title.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static (int Depth, int Number, string Path) Rank(string folder, string file)
    {
        int depth = Path.GetRelativePath(folder, file).Count(c => c == Path.DirectorySeparatorChar);
        var m = NumberSuffix().Match(Path.GetFileNameWithoutExtension(file));
        int number = m.Success && int.TryParse(m.Groups[1].Value, out var n) ? n : 0;
        return (depth, number, file);
    }

    private void ReadPlatformFolders(string platformsXml)
    {
        if (!File.Exists(platformsXml)) return;
        try
        {
            using var reader = XmlReader.Create(platformsXml, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, IgnoreComments = true });
            string? mediaType = null, folderPath = null, platform = null, element = null;
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element)
                {
                    element = reader.Name;
                    if (element == "PlatformFolder") mediaType = folderPath = platform = null;
                }
                else if (reader.NodeType == XmlNodeType.Text)
                {
                    switch (element)
                    {
                        case "MediaType": mediaType = reader.Value.Trim(); break;
                        case "FolderPath": folderPath = reader.Value.Trim(); break;
                        case "Platform": platform = reader.Value.Trim(); break;
                    }
                }
                else if (reader.NodeType == XmlNodeType.EndElement)
                {
                    if (reader.Name == "PlatformFolder" && mediaType is { Length: > 0 } && folderPath is { Length: > 0 } && platform is { Length: > 0 })
                        _folders[(platform.ToLowerInvariant(), mediaType.ToLowerInvariant())] = folderPath;
                    element = null;
                }
            }
        }
        catch (Exception ex) when (ex is XmlException or IOException or UnauthorizedAccessException)
        {
            AppLog.Warn($"Could not read LaunchBox's picture folders from {platformsXml}: {ex.Message}; using the default folders.");
        }
    }

    [GeneratedRegex(@"-(\d{1,3})$")]
    private static partial Regex NumberSuffix();

    [GeneratedRegex(@"\.[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$")]
    private static partial Regex GuidSuffix();
}
