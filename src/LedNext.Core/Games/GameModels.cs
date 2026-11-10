namespace LedNext.Core.Games;

/// <summary>A platform / emulator (LEDBlinky "emulator"), e.g. "MAME" or "Sega_Genesis".</summary>
/// <param name="ParentKey">Platform whose defaults are used when this one has none (usually "MAME").</param>
/// <param name="UsesMameData">Games are MAME ROM names, so MAME's per-game data applies too (Neo Geo, clones lists…).</param>
/// <param name="DefaultActive">Colour for a lit control that has no colour of its own.</param>
public sealed record PlatformInfo(string Key, string Name, string? ParentKey, bool UsesMameData, Rgb DefaultActive);

/// <summary>Where a game's lights come from.</summary>
public enum GameLighting
{
    /// <summary>No data for this game: the platform default is shown.</summary>
    PlatformDefault = 0,
    /// <summary>The game has its own lights.</summary>
    Own = 1,
    /// <summary>A clone that uses its parent ROM's lights.</summary>
    Parent = 2,
}

/// <summary>One game in the library.</summary>
/// <param name="Name">ROM / game name as the front end passes it (MAME short name, or a title).</param>
/// <param name="Parent">Parent ROM for clones, otherwise null.</param>
/// <param name="Info">Free text, e.g. "8-Way Joystick".</param>
/// <param name="Sources">Which files the data came from, comma separated.</param>
public sealed record GameInfo(
    string Platform,
    string Name,
    string Description,
    string? Parent,
    int Players,
    string Info,
    string Sources,
    GameLighting Lighting,
    bool Customized = false,
    int Buttons = 0,
    string Tags = "",
    DateTimeOffset? AddedToLaunchBox = null);
// AddedToLaunchBox: newest LaunchBox DateAdded of the LaunchBox games matched to this one (null = not in LaunchBox).
// Buttons: highest button number the game lights for any player (P1_BUTTON6 → 6), 0 if unknown.
// Tags: space-separated control types (joystick trackball spinner lightgun paddle pedal), see ControlTags.

/// <summary>One logical control of a game (e.g. P1_BUTTON1) and how it is lit.</summary>
/// <param name="Control">Normalised logical control name, see <see cref="ControlNames"/>.</param>
/// <param name="Color">Null = not specified here; inherited from the platform default.</param>
/// <param name="ColorName">The colour as written in the source ("Red", "41,0,0"), for display.</param>
/// <param name="Label">What the control does in this game ("Fire"), if known.</param>
/// <param name="AlwaysActive">On a platform default: lit in every game of the platform.</param>
public sealed record ControlLight(string Control, Rgb? Color, string? ColorName, string? Label, bool AlwaysActive = false);

/// <summary>The controls lit for one game or one platform default. Unlisted controls are off.</summary>
public sealed class LightProfile
{
    public List<ControlLight> Controls { get; init; } = new();
    /// <summary>File the profile came from, for display.</summary>
    public string Source { get; init; } = "";
    /// <summary>The user's own edit, stored apart from imported data. Counts even when empty (all off).</summary>
    public bool Customized { get; init; }

    public ControlLight? Find(string control) =>
        Controls.FirstOrDefault(c => string.Equals(c.Control, control, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Read access to the game database. Name lookups are case-insensitive.</summary>
public interface IGameLibrary
{
    PlatformInfo? GetPlatform(string key);
    GameInfo? GetGame(string platform, string name);
    /// <summary>A game's own lights, or the platform default when <paramref name="game"/> is null.</summary>
    LightProfile? GetLights(string platform, string? game);
}

/// <summary>An in-memory library: the importer's output, and the test double.</summary>
public sealed class InMemoryGameLibrary : IGameLibrary
{
    private static readonly StringComparer Cmp = StringComparer.OrdinalIgnoreCase;

    public Dictionary<string, PlatformInfo> Platforms { get; } = new(Cmp);
    /// <summary>Keyed by <see cref="Key"/>(platform, name).</summary>
    public Dictionary<string, GameInfo> Games { get; } = new(Cmp);
    /// <summary>Keyed by <see cref="Key"/>(platform, game or "").</summary>
    public Dictionary<string, LightProfile> Lights { get; } = new(Cmp);

    public static string Key(string platform, string? name) => platform + "\u0001" + (name ?? "");

    public void AddPlatform(PlatformInfo p) => Platforms[p.Key] = p;
    public void AddGame(GameInfo g) => Games[Key(g.Platform, g.Name)] = g;
    public void SetLights(string platform, string? game, LightProfile profile) => Lights[Key(platform, game)] = profile;

    public PlatformInfo? GetPlatform(string key) => Platforms.GetValueOrDefault(key);
    public GameInfo? GetGame(string platform, string name) => Games.GetValueOrDefault(Key(platform, name));
    public LightProfile? GetLights(string platform, string? game) => Lights.GetValueOrDefault(Key(platform, game));
}
