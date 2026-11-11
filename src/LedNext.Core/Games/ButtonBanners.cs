namespace LedNext.Core.Games;

/// <summary>
/// "6 button layout" banners: one picture per MAME rom (<c>&lt;rom&gt;.png</c>, 800 x 480) of a stick and six
/// buttons in two rows of three, with the buttons the game uses drawn in colour and their names under them.
/// The pictures show where each function sits on a 6-button panel, not MAME's button numbers (JoJo puts its
/// 4th button bottom-left), so only the <i>number</i> of buttons used is taken from them.
/// </summary>
public static class ButtonBanners
{
    public const int TemplateWidth = 800;
    public const int TemplateHeight = 480;

    /// <summary>Button centres in the 800 x 480 template: bottom row left → right, then top row.</summary>
    private static readonly (int X, int Y)[] Centres =
    {
        (410, 340), (540, 288), (682, 312),
        (438, 192), (568, 136), (707, 160),
    };

    /// <summary>
    /// Which of the six drawn buttons are in use (1-3 bottom row, 4-6 top row): an unused button is drawn dark
    /// grey, a used one in a colour. Null when the picture isn't the banner's shape (5:3).
    /// </summary>
    /// <param name="pixel">Reads one pixel as (R, G, B, A).</param>
    public static IReadOnlyList<int>? UsedPositions(int width, int height, Func<int, int, (byte R, byte G, byte B, byte A)> pixel)
    {
        if (width < 100 || height < 60 || Math.Abs(width * 3 - height * 5) > width / 20) return null;
        double sx = width / (double)TemplateWidth, sy = height / (double)TemplateHeight;
        var used = new List<int>();
        for (int i = 0; i < Centres.Length; i++)
        {
            int colourful = 0;
            foreach (var dx in new[] { -15, 0, 15 })
            foreach (var dy in new[] { -15, 0, 15 })
            {
                int x = (int)Math.Round((Centres[i].X + dx) * sx), y = (int)Math.Round((Centres[i].Y + dy) * sy);
                if (x < 0 || y < 0 || x >= width || y >= height) continue;
                var (r, g, b, a) = pixel(x, y);
                if (a > 200 && Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b)) > 60) colourful++;
            }
            if (colourful >= 5) used.Add(i + 1);
        }
        return used;
    }

    /// <summary>
    /// A platform default with each player's buttons replaced by buttons 1..<paramref name="buttons"/> in
    /// <paramref name="color"/> (the platform's default-active colour); everything else (stick, always-active
    /// controls) is kept. For a game that has no lights of its own.
    /// </summary>
    public static LightProfile Apply(LightProfile? platformDefault, int buttons, int players, Rgb color)
    {
        players = Math.Clamp(players, 1, 4);
        var controls = (platformDefault?.Controls ?? new List<ControlLight>())
            .Where(c => !IsPlayerButton(c.Control)).ToList();
        for (int p = 1; p <= players; p++)
            for (int b = 1; b <= buttons; b++)
                controls.Add(new ControlLight($"P{p}_BUTTON{b}", color, "default", null));
        return new LightProfile { Controls = controls, Source = "button banner" };
    }

    /// <summary>P1_BUTTON1 … P4_BUTTON10.</summary>
    public static bool IsPlayerButton(string control) =>
        control.Length > 9 && control[0] is 'P' or 'p' && char.IsDigit(control[1]) &&
        control.AsSpan(2).StartsWith("_BUTTON", StringComparison.OrdinalIgnoreCase) && control.Skip(9).All(char.IsDigit);
}
