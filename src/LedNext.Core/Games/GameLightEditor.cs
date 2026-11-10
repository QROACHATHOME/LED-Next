namespace LedNext.Core.Games;

/// <summary>
/// Turns a click on a panel button into an edit of a game's (or a platform default's) lights.
/// Edits are made on a full copy of what the panel shows now (<see cref="Materialize"/>), saved as
/// a customized profile that replaces the imported one at that level.
/// </summary>
public static class GameLightEditor
{
    /// <summary>
    /// The profile that reproduces <paramref name="lights"/> exactly, as a starting point for edits.
    /// Controls that only appear because a platform default marks them always lit are left out when
    /// editing a game (they still come from the platform); a platform default keeps them.
    /// </summary>
    public static LightProfile Materialize(ResolvedLights lights)
    {
        bool platformDefault = lights.Game is null;
        var controls = lights.Controls
            .Where(c => platformDefault || !c.FromPlatformAlwaysActive)
            .Select(c => new ControlLight(c.Control, c.Color, c.ColorName == "default" ? null : c.ColorName, c.Label, c.AlwaysActive))
            .ToList();
        return new LightProfile { Controls = controls, Source = "LED-Next", Customized = true };
    }

    /// <summary>
    /// The logical control a panel button stands for in this game: the control that lights it now,
    /// else the one the control map / naming convention would send to it (P1_B3 → P1_BUTTON3).
    /// Null when the button has no sensible control (e.g. a custom button with an unusual ID).
    /// </summary>
    public static string? ControlForButton(string buttonId, ResolvedLights lights, ControlMap map, PanelLayout panel)
    {
        var lit = lights.Controls.Where(c => c.Buttons.Contains(buttonId, StringComparer.Ordinal)).ToList();
        var pick = lit.FirstOrDefault(c => !c.FromPlatformAlwaysActive) ?? lit.FirstOrDefault();
        if (pick is not null) return pick.Control;

        var button = panel.Find(buttonId);
        if (button is null) return null;
        foreach (var candidate in ConventionCandidates(button))
            if (map.Lookup(candidate, panel).Buttons.Contains(buttonId, StringComparer.OrdinalIgnoreCase))
                return candidate;

        // The name doesn't lead here (e.g. MAME's button 7 is wired to the button labelled 8):
        // use a mapped button/start/coin control, never a mahjong key or UI control that shares its key.
        var mapped = map.Mappings
            .Where(m => m.Buttons.Contains(buttonId, StringComparer.OrdinalIgnoreCase) && ControlNames.PlayerOf(m.Control) > 0)
            .OrderBy(m => m.Control.Contains("_BUTTON", StringComparison.OrdinalIgnoreCase) ? 0
                        : m.Control.EndsWith("_START", StringComparison.OrdinalIgnoreCase) || m.Control.EndsWith("_COIN", StringComparison.OrdinalIgnoreCase) ? 1 : 2)
            .FirstOrDefault();
        if (mapped is not null) return mapped.Control;

        // Last resort: a custom button whose ID is itself a control name.
        return map.Lookup(button.Id, panel).Buttons.Contains(buttonId, StringComparer.OrdinalIgnoreCase) ? button.Id.ToUpperInvariant() : null;
    }

    private static IEnumerable<string> ConventionCandidates(PanelButton b)
    {
        var id = b.Id.ToUpperInvariant();
        int player = b.Player > 0 ? b.Player : 1;
        var bm = System.Text.RegularExpressions.Regex.Match(id, @"^P(\d)_B(\d+)$");
        if (bm.Success) yield return $"P{bm.Groups[1].Value}_BUTTON{bm.Groups[2].Value}";
        if (id.EndsWith("_START", StringComparison.Ordinal) || id.EndsWith("_COIN", StringComparison.Ordinal)) yield return id;
        if (b.Kind == ControlKind.Joystick || id.EndsWith("_STICK", StringComparison.Ordinal)) yield return $"P{player}_JOYSTICK";
        if (b.Kind == ControlKind.Trackball) yield return $"P{player}_TRACKBALL";
        if (b.Kind == ControlKind.Spinner) yield return $"P{player}_DIAL";
    }

    /// <summary>Lights (or recolours) a control. Keeps its label and position in the list.</summary>
    public static LightProfile SetColor(LightProfile profile, string control, Rgb color, string? colorName)
    {
        var list = profile.Controls.ToList();
        int i = list.FindIndex(c => string.Equals(c.Control, control, StringComparison.OrdinalIgnoreCase));
        if (i >= 0) list[i] = list[i] with { Color = color, ColorName = colorName };
        else list.Add(new ControlLight(control, color, colorName, null));
        return With(profile, list);
    }

    /// <summary>Turns a control off (removes it: unlisted controls are off).</summary>
    public static LightProfile TurnOff(LightProfile profile, string control) =>
        With(profile, profile.Controls.Where(c => !string.Equals(c.Control, control, StringComparison.OrdinalIgnoreCase)).ToList());

    /// <summary>Sets what the control does in this game ("Fire"); empty clears it.</summary>
    public static LightProfile SetLabel(LightProfile profile, string control, string? label)
    {
        var list = profile.Controls.ToList();
        int i = list.FindIndex(c => string.Equals(c.Control, control, StringComparison.OrdinalIgnoreCase));
        if (i < 0) return profile;
        list[i] = list[i] with { Label = string.IsNullOrWhiteSpace(label) ? null : label.Trim() };
        return With(profile, list);
    }

    private static LightProfile With(LightProfile p, List<ControlLight> controls) =>
        new() { Controls = controls, Source = "LED-Next", Customized = true };

    /// <summary>
    /// Puts copied lights onto another game. A whole-layout copy replaces the target's lit controls
    /// (keeping the target's own labels, since "Fire" in one game is not "Fire" in another). A copy of
    /// some controls changes only those: lit ones take the copied colour, ones that were off are turned off.
    /// </summary>
    public static LightProfile Paste(LightProfile target, LightClipboard clip)
    {
        if (clip.Controls is null)
        {
            var controls = clip.Profile.Controls
                .Select(c => c with { Label = target.Find(c.Control)?.Label })
                .ToList();
            return With(target, controls);
        }

        var result = With(target, target.Controls.ToList());
        foreach (var control in clip.Controls)
        {
            var source = clip.Profile.Find(control);
            result = source is { Color: { } color }
                ? SetColor(result, control, color, source.ColorName)
                : TurnOff(result, control);
        }
        return result;
    }
}

/// <summary>Lights copied from one game, to paste onto others.</summary>
/// <param name="Profile">Everything the source game showed (see <see cref="GameLightEditor.Materialize"/>).</param>
/// <param name="Controls">Null = the whole layout; otherwise only these controls are pasted.</param>
public sealed record LightClipboard(string SourceTitle, LightProfile Profile, IReadOnlyCollection<string>? Controls)
{
    public string Describe()
    {
        if (Controls is null) return $"all lights of {SourceTitle}";
        var parts = Controls.Select(c => Profile.Find(c) is { } lit ? $"{c} {lit.ColorName ?? lit.Color?.ToHex()}" : $"{c} off");
        return $"{string.Join(", ", parts)} from {SourceTitle}";
    }
}
