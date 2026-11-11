namespace LedNext.Core.Games;

/// <summary>One lit control after inheritance, with the panel buttons it lights.</summary>
/// <param name="Buttons">Empty when the control is not on this panel.</param>
/// <param name="MapSource">How the buttons were found ("input code KEYCODE_C", "name").</param>
/// <param name="FromPlatformAlwaysActive">Added because a platform default marks it always lit, not
/// listed by the level the game's lights come from.</param>
public sealed record ResolvedControl(
    string Control,
    string? Label,
    Rgb Color,
    string? ColorName,
    bool AlwaysActive,
    IReadOnlyList<string> Buttons,
    string MapSource,
    bool FromPlatformAlwaysActive = false);

/// <summary>What the panel shows for one game.</summary>
/// <param name="From">Which level supplied the lit controls, e.g. "game 1942", "parent 005", "MAME default".</param>
public sealed record ResolvedLights(
    string Platform,
    string? Game,
    string From,
    IReadOnlyList<ResolvedControl> Controls,
    IReadOnlyDictionary<string, Rgb> ButtonColors,
    bool GameSpecific = false);
// GameSpecific: the lit controls come from the game, a parent ROM or MAME's game data, not a platform default.

/// <summary>
/// Works out a game's lights through the inheritance chain:
/// game → parent ROM(s) → (MAME's data for MAME-based platforms) → platform default → parent platform default.
/// </summary>
/// <remarks>
/// The first level that lists any controls decides <i>which</i> controls are lit (unlisted ones are off),
/// the same as LEDBlinky. A control without a colour takes the colour of the same control further up the
/// chain, else the platform's default-active colour. Always-active controls of the platform defaults
/// are added to every game. Always-active UI controls (no player: UI_SELECT, UI_PAUSE…) light only
/// shared (player 0) buttons: on a cabinet where a player button also sends the UI key (P3 button 3
/// sends Enter = UI_SELECT), LEDBlinky lit that button green in every game; here it follows the game.
/// </remarks>
public static class GameLightResolver
{
    private const int MaxDepth = 8;
    private const string Mame = "MAME";

    private sealed record Level(LightProfile Profile, string Description, bool IsPlatformDefault);

    public static ResolvedLights Resolve(IGameLibrary library, string platform, string? game, ControlMap map, PanelLayout panel)
    {
        var levels = BuildChain(library, platform, game, out var defaultActive);

        // A customized profile counts even when empty: the user turned everything off on purpose.
        int activeIndex = levels.FindIndex(l => l.Profile.Controls.Count > 0 || l.Profile.Customized);
        UseButtonBanner(library, platform, game, levels, ref activeIndex, defaultActive);
        var controls = new List<ResolvedControl>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(ControlLight c, int fromIndex, bool sharedButtonsOnly = false, bool platformAlwaysActive = false)
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
            if (sharedButtonsOnly)
            {
                var shared = buttons.Where(id => panel.Find(id)?.Player == 0).ToList();
                if (shared.Count < buttons.Count)
                    source = shared.Count > 0 ? source : $"{source}; not lit: {string.Join(", ", buttons)} is a player button";
                buttons = shared;
            }
            controls.Add(new ResolvedControl(c.Control, label, color ?? defaultActive, color is null ? "default" : colorName,
                c.AlwaysActive, buttons, source, platformAlwaysActive));
        }

        string from = "nothing (no lighting data)";
        if (activeIndex >= 0)
        {
            from = levels[activeIndex].Description;
            // When a platform default decides (a game without its own lights, or the platform wheel), its
            // always-active UI controls follow the same rule as below: shared buttons only.
            bool fromDefault = levels[activeIndex].IsPlatformDefault;
            foreach (var c in levels[activeIndex].Profile.Controls)
                Add(c, activeIndex, sharedButtonsOnly: fromDefault && c.AlwaysActive && ControlNames.PlayerOf(c.Control) == 0);
        }
        for (int i = 0; i < levels.Count; i++)
        {
            if (!levels[i].IsPlatformDefault) continue;
            foreach (var c in levels[i].Profile.Controls)
                if (c.AlwaysActive) Add(c, i, sharedButtonsOnly: ControlNames.PlayerOf(c.Control) == 0, platformAlwaysActive: i != activeIndex);
        }

        // A game's own controls come first, so they win if two controls share a button.
        var colors = new Dictionary<string, Rgb>(StringComparer.Ordinal);
        foreach (var c in controls)
            foreach (var id in c.Buttons)
                colors.TryAdd(id, c.Color);

        bool gameSpecific = activeIndex >= 0 && !levels[activeIndex].IsPlatformDefault;
        return new ResolvedLights(platform, game, from, controls, colors, gameSpecific);
    }

    /// <summary>A stand-alone profile (a template) on this panel, with no inheritance; colourless controls are white.</summary>
    public static ResolvedLights ResolveProfile(LightProfile profile, string title, ControlMap map, PanelLayout panel)
    {
        const string key = "\u0001template";
        var lib = new InMemoryGameLibrary();
        lib.AddPlatform(new PlatformInfo(key, key, null, false, Rgb.White));
        lib.SetLights(key, title, profile);
        var r = Resolve(lib, key, title, map, panel);
        return r with { From = $"template {title}" };
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
            if (lights is not null) levels.Add(new Level(lights, $"{p.Name} default{Custom(lights)}", true));
        }
        return levels;
    }

    /// <summary>
    /// A MAME game with no lights of its own (its platform default decides) lights as many buttons as its button
    /// banner shows in use (the rom's banner, else its parent's), in the default-active colour, instead of the
    /// platform default's buttons. A customized platform default is left alone.
    /// </summary>
    private static void UseButtonBanner(IGameLibrary library, string platform, string? game, List<Level> levels,
        ref int activeIndex, Rgb defaultActive)
    {
        if (string.IsNullOrWhiteSpace(game)) return;
        if (activeIndex >= 0 && (!levels[activeIndex].IsPlatformDefault || levels[activeIndex].Profile.Customized)) return;
        var platforms = PlatformChain(library, platform);
        bool mame = string.Equals(platform, Mame, StringComparison.OrdinalIgnoreCase) || platforms.Any(p => p.UsesMameData);
        if (!mame) return;

        int? buttons = null;
        string? from = null;
        int players = 0;
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in new[] { platform, Mame })
        {
            for (string? name = game; name is not null && buttons is null && visited.Count < 32 && visited.Add(p + "/" + name);
                 name = library.GetGame(p, name)?.Parent)
            {
                var info = library.GetGame(p, name);
                if (players == 0 && info is { Players: > 0 }) players = info.Players;
                if (library.GetBannerButtons(name) is { } n) { buttons = n; from = name; }
            }
            if (buttons is not null) break;
        }
        if (buttons is not { } count) return;

        var basis = activeIndex >= 0 ? levels[activeIndex] : null;
        var profile = ButtonBanners.Apply(basis?.Profile, count, players > 0 ? players : 2, defaultActive);
        var what = $"button banner {from} ({count} button{(count == 1 ? "" : "s")})";
        var level = new Level(profile, basis is null ? what : $"{basis.Description} + {what}", true);
        if (activeIndex < 0) { levels.Insert(0, level); activeIndex = 0; }
        else levels.Insert(activeIndex, level);
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
                levels.Add(new Level(lights, (own ? $"{what} {name}" : $"{what} {name} (MAME data)") + Custom(lights), false));
            }
            name = library.GetGame(platform, name)?.Parent;
        }
    }

    private static string Custom(LightProfile p) => p.Customized ? " (customized)" : "";

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
