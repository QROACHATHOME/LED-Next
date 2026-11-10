namespace LedNext.Core.Games;

/// <summary>One lit control after inheritance, with the panel buttons it lights.</summary>
/// <param name="Buttons">Empty when the control is not on this panel.</param>
/// <param name="MapSource">How the buttons were found ("input code KEYCODE_C", "name").</param>
public sealed record ResolvedControl(
    string Control,
    string? Label,
    Rgb Color,
    string? ColorName,
    bool AlwaysActive,
    IReadOnlyList<string> Buttons,
    string MapSource);

/// <summary>What the panel shows for one game.</summary>
/// <param name="From">Which level supplied the lit controls, e.g. "game 1942", "parent 005", "MAME default".</param>
public sealed record ResolvedLights(
    string Platform,
    string? Game,
    string From,
    IReadOnlyList<ResolvedControl> Controls,
    IReadOnlyDictionary<string, Rgb> ButtonColors);

/// <summary>
/// Works out a game's lights through the inheritance chain:
/// game → parent ROM(s) → (MAME's data for MAME-based platforms) → platform default → parent platform default.
/// </summary>
/// <remarks>
/// The first level that lists any controls decides <i>which</i> controls are lit (unlisted ones are off),
/// the same as LEDBlinky. A control without a colour takes the colour of the same control further up the
/// chain, else the platform's default-active colour. Always-active controls of the platform defaults
/// (e.g. UI_SELECT) are added to every game.
/// </remarks>
public static class GameLightResolver
{
    private const int MaxDepth = 8;
    private const string Mame = "MAME";

    private sealed record Level(LightProfile Profile, string Description, bool IsPlatformDefault);

    public static ResolvedLights Resolve(IGameLibrary library, string platform, string? game, ControlMap map, PanelLayout panel)
    {
        var levels = BuildChain(library, platform, game, out var defaultActive);

        int activeIndex = levels.FindIndex(l => l.Profile.Controls.Count > 0);
        var controls = new List<ResolvedControl>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(ControlLight c, int fromIndex)
        {
            if (!seen.Add(c.Control)) return;
            Rgb? color = c.Color;
            string? colorName = c.ColorName;
            string? label = c.Label;
            for (int i = fromIndex + 1; i < levels.Count && (color is null || label is null); i++)
            {
                var up = levels[i].Profile.Find(c.Control);
                if (up is null) continue;
                if (color is null && up.Color is not null) { color = up.Color; colorName = up.ColorName; }
                label ??= up.Label;
            }
            var (buttons, source) = map.Lookup(c.Control, panel);
            controls.Add(new ResolvedControl(c.Control, label, color ?? defaultActive, color is null ? "default" : colorName,
                c.AlwaysActive, buttons, source));
        }

        string from = "nothing (no lighting data)";
        if (activeIndex >= 0)
        {
            from = levels[activeIndex].Description;
            foreach (var c in levels[activeIndex].Profile.Controls) Add(c, activeIndex);
        }
        for (int i = 0; i < levels.Count; i++)
        {
            if (!levels[i].IsPlatformDefault) continue;
            foreach (var c in levels[i].Profile.Controls)
                if (c.AlwaysActive) Add(c, i);
        }

        // A game's own controls come first, so they win if two controls share a button.
        var colors = new Dictionary<string, Rgb>(StringComparer.Ordinal);
        foreach (var c in controls)
            foreach (var id in c.Buttons)
                colors.TryAdd(id, c.Color);

        return new ResolvedLights(platform, game, from, controls, colors);
    }

    private static List<Level> BuildChain(IGameLibrary library, string platform, string? game, out Rgb defaultActive)
    {
        var levels = new List<Level>();
        var platforms = PlatformChain(library, platform);
        defaultActive = platforms.Select(p => (Rgb?)p.DefaultActive).FirstOrDefault() ?? Rgb.White;

        if (!string.IsNullOrWhiteSpace(game))
        {
            AddGameChain(library, levels, platform, game, own: true);
            if (platforms.Any(p => p.UsesMameData) && !string.Equals(platform, Mame, StringComparison.OrdinalIgnoreCase))
                AddGameChain(library, levels, Mame, game, own: false);
        }

        foreach (var p in platforms)
        {
            var lights = library.GetLights(p.Key, null);
            if (lights is not null) levels.Add(new Level(lights, $"{p.Name} default", true));
        }
        return levels;
    }

    private static void AddGameChain(IGameLibrary library, List<Level> levels, string platform, string game, bool own)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? name = game;
        for (int depth = 0; name is not null && depth < MaxDepth && visited.Add(name); depth++)
        {
            var lights = library.GetLights(platform, name);
            if (lights is not null)
            {
                string what = depth == 0 ? "game" : "parent";
                levels.Add(new Level(lights, own ? $"{what} {name}" : $"{what} {name} (MAME data)", false));
            }
            name = library.GetGame(platform, name)?.Parent;
        }
    }

    private static List<PlatformInfo> PlatformChain(IGameLibrary library, string platform)
    {
        var chain = new List<PlatformInfo>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var p = library.GetPlatform(platform); p is not null && chain.Count < MaxDepth && visited.Add(p.Key);
             p = p.ParentKey is null ? null : library.GetPlatform(p.ParentKey))
            chain.Add(p);
        return chain;
    }
}
