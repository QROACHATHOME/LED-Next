using LedNext.Core.Games;

namespace LedNext.Core.Speech;

/// <summary>
/// One thing to say, and the panel buttons to blink while it is said (empty for the game's name).
/// <paramref name="PauseAfter"/> is the silence after it; zero means the voice's own short pause.
/// </summary>
public sealed record SpeechItem(string Text, IReadOnlyList<string> Buttons, TimeSpan PauseAfter = default);

/// <summary>
/// What to say for a game: its name, then each label once, in player order (P1 first). The same label on
/// several players ("Fire" on P1 and P2) is said once and blinks all of them. Controls without a label, or not
/// on this panel, are skipped; joysticks and other non-buttons only with <see cref="SpeechSettings.SpeakJoysticks"/>;
/// Start and Coin only with <see cref="SpeechSettings.SpeakStartCoin"/>, as one "Start Game" / "Insert Coin" each.
/// Pure logic, unit-tested.
/// </summary>
public static class SpeechPlan
{
    /// <summary>
    /// The title as it should be said: MAME's and LaunchBox's bracketed extras are dropped
    /// ("Galaga (Namco rev. B)" → "Galaga", "Street Fighter II [Turbo hack]" → "Street Fighter II").
    /// A title that is nothing but brackets is kept as it is.
    /// </summary>
    public static string SpokenTitle(string title)
    {
        var s = System.Text.RegularExpressions.Regex.Replace(title, @"\s*[\(\[][^\)\]]*[\)\]]", "");
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\s{2,}", " ").Trim();
        return s.Length == 0 ? title.Trim() : s;
    }

    public static List<SpeechItem> For(ResolvedLights lights, SpeechSettings settings, string? title)
    {
        var groups = new List<(string Text, List<string> Buttons)>();
        void Add(string text, IEnumerable<string> buttons)
        {
            var g = groups.FindIndex(x => string.Equals(x.Text, text, StringComparison.OrdinalIgnoreCase));
            if (g < 0) { groups.Add((text, new List<string>())); g = groups.Count - 1; }
            foreach (var b in buttons)
                if (!groups[g].Buttons.Contains(b, StringComparer.OrdinalIgnoreCase)) groups[g].Buttons.Add(b);
        }

        var ordered = lights.Controls
            .Select((c, i) => (c, i))
            .OrderBy(x => ControlNames.PlayerOf(x.c.Control) is var p && p == 0 ? int.MaxValue : p)
            .ThenBy(x => x.i)
            .Select(x => x.c);
        foreach (var c in ordered)
        {
            if (c.Buttons.Count == 0 || c.FromPlatformAlwaysActive) continue;
            var name = c.Control.ToUpperInvariant();
            if (name.EndsWith("_START", StringComparison.Ordinal) || name.EndsWith("_COIN", StringComparison.Ordinal))
            {
                if (settings.SpeakStartCoin) Add(name.EndsWith("_START", StringComparison.Ordinal) ? settings.StartText : settings.CoinText, c.Buttons);
                continue;
            }
            if (string.IsNullOrWhiteSpace(c.Label)) continue;
            if (!ControlNames.Classify(c.Control).IsDigital && !settings.SpeakJoysticks) continue;
            Add(c.Label.Trim(), c.Buttons);
        }

        var items = new List<SpeechItem>();
        if (groups.Count == 0) return items; // nothing labelled: say nothing, not just the name
        if (settings.SpeakGameName && !string.IsNullOrWhiteSpace(title)) items.Add(new SpeechItem(SpokenTitle(title), Array.Empty<string>(), settings.PauseAfterName));
        if (!string.IsNullOrWhiteSpace(settings.Prefix)) items.Add(new SpeechItem(settings.Prefix.Trim(), Array.Empty<string>()));
        items.AddRange(groups.Select(g => new SpeechItem(g.Text, g.Buttons)));
        return items;
    }
}
