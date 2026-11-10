using LedNext.Core.Import.LedBlinky;

namespace LedNext.Core.Games;

/// <summary>A saved, named layout ("6-button fighter") to apply to many games at once.</summary>
public sealed record LightTemplate(string Name, LightProfile Profile, string Description = "");

/// <summary>Starter templates and smart lists, added to a new game library once (deleting them sticks).</summary>
public static class BuiltIns
{
    private static readonly IReadOnlyDictionary<string, Rgb> C = new LedBlinkyColors().Named;

    public static IReadOnlyList<LightTemplate> Templates { get; } = new[]
    {
        Make("6-button fighter", "Punches red (1-3), kicks blue (4-6), Start/Coin white. Players 1 and 2.", 2,
            ("BUTTON1", "Red"), ("BUTTON2", "Red"), ("BUTTON3", "Red"), ("BUTTON4", "Blue"), ("BUTTON5", "Blue"), ("BUTTON6", "Blue")),
        Make("Neo Geo 4-button", "A red, B yellow, C green, D blue (the Neo Geo colours). Players 1 and 2.", 2,
            ("BUTTON1", "Red"), ("BUTTON2", "Yellow"), ("BUTTON3", "Lime"), ("BUTTON4", "Blue")),
        Make("3-button", "Red, yellow, blue. Players 1 and 2.", 2, ("BUTTON1", "Red"), ("BUTTON2", "Yellow"), ("BUTTON3", "Blue")),
        Make("2-button", "Red and blue. Players 1 and 2.", 2, ("BUTTON1", "Red"), ("BUTTON2", "Blue")),
        Make("4-player 3-button", "Red, yellow, blue for all four players.", 4, ("BUTTON1", "Red"), ("BUTTON2", "Yellow"), ("BUTTON3", "Blue")),
        Make("Trackball", "Trackball white, button 1 red, button 2 blue. Players 1 and 2.", 2,
            ("TRACKBALL", "White"), ("BUTTON1", "Red"), ("BUTTON2", "Blue")),
    };

    public static IReadOnlyList<(string Name, GameFilter Filter)> SmartLists { get; } = new[]
    {
        ("6-button games", new GameFilter { MinButtons = 6, MaxButtons = 6, HideClones = true }),
        ("4-button games", new GameFilter { MinButtons = 4, MaxButtons = 4, HideClones = true }),
        ("2-button games", new GameFilter { MinButtons = 2, MaxButtons = 2, HideClones = true }),
        ("4-player games", new GameFilter { MinPlayers = 4, HideClones = true }),
        ("Trackball games", new GameFilter { Control = "trackball", HideClones = true }),
        ("Spinner games", new GameFilter { Control = "spinner", HideClones = true }),
        ("Lightgun games", new GameFilter { Control = "lightgun", HideClones = true }),
        ("My customized games", new GameFilter { CustomizedOnly = true, LitOnly = false }),
    };

    /// <summary>Added in a later version; seeded into existing libraries once, by name.</summary>
    public static IReadOnlyList<(string Name, GameFilter Filter)> SmartListsV2 { get; } = new[]
    {
        ("New in LaunchBox, needs setup (30 days)", new GameFilter { AddedWithinDays = 30, NeedsSetupOnly = true, LitOnly = false }),
        ("New in LaunchBox (7 days)", new GameFilter { AddedWithinDays = 7, LitOnly = false }),
        ("In LaunchBox, needs setup", new GameFilter { InLaunchBoxOnly = true, NeedsSetupOnly = true, LitOnly = false }),
    };

    private static LightTemplate Make(string name, string description, int players, params (string Control, string Color)[] perPlayer)
    {
        var controls = new List<ControlLight>();
        for (int p = 1; p <= players; p++)
        {
            foreach (var (control, color) in perPlayer)
                controls.Add(new ControlLight($"P{p}_{control}", C[color], color, null));
            controls.Add(new ControlLight($"P{p}_START", C["White"], "White", null));
            controls.Add(new ControlLight($"P{p}_COIN", C["White"], "White", null));
        }
        return new LightTemplate(name, new LightProfile { Controls = controls, Source = "LED-Next", Customized = true }, description);
    }
}
