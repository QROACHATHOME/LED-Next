namespace LedNext.Core;

public enum ControlKind
{
    Button,
    Start,
    Coin,
    Admin,
    Trackball,
    Spinner,
    Other,
    /// <summary>A lit joystick (LED ball-top or lit base). Added after the others so existing configs keep their values.</summary>
    Joystick,
}

/// <summary>One lit control on the panel. X/Y are the centre, in panel units.</summary>
public sealed class PanelButton
{
    /// <summary>Stable name used by wiring and (later) game profiles, e.g. "P1_B1", "P2_START".</summary>
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    /// <summary>1-4, or 0 for shared controls.</summary>
    public int Player { get; set; }
    public ControlKind Kind { get; set; } = ControlKind.Button;
    public double X { get; set; }
    public double Y { get; set; }
    public double Size { get; set; } = 56;
}

public sealed class PanelLayout
{
    public double Width { get; set; } = 1600;
    public double Height { get; set; } = 640;
    /// <summary>Image file name, relative to the config folder (copied there so the config stays portable).</summary>
    public string? BackgroundImage { get; set; }
    public double BackgroundOpacity { get; set; } = 0.6;
    public List<PanelButton> Buttons { get; set; } = new();

    public PanelButton? Find(string id) => Buttons.FirstOrDefault(b => b.Id == id);

    /// <summary>
    /// ID for a copy of <paramref name="sourceId"/>: bumps a trailing number if there is one
    /// ("P1_B8" → "P1_B9"), otherwise appends a suffix.
    /// </summary>
    public string NextIdAfter(string sourceId)
    {
        int digits = sourceId.Length - sourceId.TrimEnd("0123456789".ToCharArray()).Length;
        if (digits == 0) return MakeUniqueId(sourceId);
        var prefix = sourceId[..^digits];
        for (int n = int.Parse(sourceId[^digits..]) + 1; ; n++)
        {
            var candidate = $"{prefix}{n}";
            if (Find(candidate) is null) return candidate;
        }
    }

    public static PanelButton CloneButton(PanelButton b) => new()
    {
        Id = b.Id, Label = b.Label, Player = b.Player, Kind = b.Kind, X = b.X, Y = b.Y, Size = b.Size,
    };

    /// <summary>Returns an ID based on <paramref name="baseId"/> that no button uses yet.</summary>
    public string MakeUniqueId(string baseId)
    {
        if (Find(baseId) is null) return baseId;
        for (int i = 2; ; i++)
        {
            var candidate = $"{baseId}_{i}";
            if (Find(candidate) is null) return candidate;
        }
    }
}
