namespace LedNext.Core.Games;

/// <summary>One logical control mapped to the panel button(s) that light for it.</summary>
public sealed class ControlMapping
{
    public string Control { get; set; } = "";
    public List<string> Buttons { get; set; } = new();
    /// <summary>How the mapping was found, e.g. "input code KEYCODE_C", for display.</summary>
    public string Source { get; set; } = "";
    /// <summary>
    /// The control is on no panel button (its key is one no button sends), so it is not lit, instead of
    /// falling back to the naming convention. Only set for one game's map (a MAME install's own keys); never saved.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool NotOnPanel { get; set; }
}

/// <summary>
/// Maps logical game controls (P1_BUTTON1, P2_START, P1_TRACKBALL…) to panel buttons. Part of
/// the cabinet config. Explicit mappings come from the LEDBlinky input map; anything not listed
/// falls back to the naming convention (P1_BUTTON3 → P1_B3, P1_START → P1_START…).
/// </summary>
public sealed class ControlMap
{
    public List<ControlMapping> Mappings { get; set; } = new();

    public ControlMapping? Find(string control) =>
        Mappings.FirstOrDefault(m => string.Equals(m.Control, control, StringComparison.OrdinalIgnoreCase));

    /// <summary>Panel buttons for a logical control (empty = not on this panel), and how they were found.</summary>
    public (IReadOnlyList<string> Buttons, string Source) Lookup(string control, PanelLayout panel)
    {
        var explicitMap = Find(control);
        if (explicitMap is not null)
        {
            if (explicitMap.NotOnPanel) return (Array.Empty<string>(), explicitMap.Source);
            var existing = explicitMap.Buttons.Where(id => panel.Find(id) is not null).ToList();
            if (existing.Count > 0) return (existing, explicitMap.Source);
        }
        var byName = ByConvention(control, panel);
        return (byName, byName.Count > 0 ? "name" : "");
    }

    /// <summary>The naming-convention match, ignoring explicit mappings.</summary>
    public static IReadOnlyList<string> ByConvention(string control, PanelLayout panel)
    {
        var c = control.ToUpperInvariant();
        int player = ControlNames.PlayerOf(c);
        var p = $"P{player}_";

        string? id = null;
        if (player > 0 && c.StartsWith(p + "BUTTON", StringComparison.Ordinal) && int.TryParse(c[(p.Length + 6)..], out var n))
            id = $"P{player}_B{n}";
        else if (c == p + "START" || c == p + "COIN")
            id = c;
        else if (c == p + "JOYSTICK" || c == p + "STICK")
            id = p + "STICK";
        else if (c == p + "TRACKBALL")
            return ByKind(panel, ControlKind.Trackball, player);
        else if (c == p + "DIAL")
            return ByKind(panel, ControlKind.Spinner, player);

        id ??= c;
        var button = panel.Buttons.FirstOrDefault(b => string.Equals(b.Id, id, StringComparison.OrdinalIgnoreCase));
        return button is null ? Array.Empty<string>() : new[] { button.Id };
    }

    /// <summary>The player's own trackball/spinner, else a shared one (player 0), else any.</summary>
    private static IReadOnlyList<string> ByKind(PanelLayout panel, ControlKind kind, int player)
    {
        var all = panel.Buttons.Where(b => b.Kind == kind).ToList();
        var pick = all.Where(b => b.Player == player).ToList();
        if (pick.Count == 0) pick = all.Where(b => b.Player == 0).ToList();
        if (pick.Count == 0) pick = all;
        return pick.Select(b => b.Id).ToList();
    }
}
