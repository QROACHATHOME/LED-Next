using System.Text;

namespace LedNext.Core.Import;

/// <summary>Minimal INI reader: [sections], key=value, ';' comments. Case-insensitive; repeated sections merge.</summary>
public sealed class IniFile
{
    private static readonly StringComparer Cmp = StringComparer.OrdinalIgnoreCase;

    /// <summary>Sections in file order, each with its keys in file order.</summary>
    public List<(string Name, List<KeyValuePair<string, string>> Entries)> Sections { get; } = new();
    private readonly Dictionary<string, Dictionary<string, string>> _lookup = new(Cmp);

    public static IniFile Load(string path) => Parse(File.ReadLines(path, Encoding.Latin1));

    public static IniFile Parse(IEnumerable<string> lines)
    {
        var ini = new IniFile();
        List<KeyValuePair<string, string>>? entries = null;
        Dictionary<string, string>? dict = null;
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == ';' || line[0] == '#') continue;
            if (line[0] == '[' && line.EndsWith(']'))
            {
                var name = line[1..^1].Trim();
                if (!ini._lookup.TryGetValue(name, out dict))
                {
                    dict = new Dictionary<string, string>(Cmp);
                    ini._lookup[name] = dict;
                    entries = new List<KeyValuePair<string, string>>();
                    ini.Sections.Add((name, entries));
                }
                else
                {
                    entries = ini.Sections.First(s => Cmp.Equals(s.Name, name)).Entries;
                }
                continue;
            }
            int eq = line.IndexOf('=');
            if (eq <= 0 || dict is null || entries is null) continue;
            var key = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim();
            dict[key] = value;
            entries.Add(new(key, value));
        }
        return ini;
    }

    public string? Get(string section, string key) =>
        _lookup.TryGetValue(section, out var d) && d.TryGetValue(key, out var v) ? v : null;

    public IReadOnlyDictionary<string, string>? Section(string name) => _lookup.GetValueOrDefault(name);
}
