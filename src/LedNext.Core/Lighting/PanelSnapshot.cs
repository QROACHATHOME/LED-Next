namespace LedNext.Core.Lighting;

/// <summary>Read-only view of one button for light sources. NormX/NormY are 0..1 across the panel.</summary>
public sealed record ButtonInfo(string Id, string Label, int Player, ControlKind Kind, double X, double Y, double NormX, double NormY);

public readonly record struct PinTarget(string BoardKey, int Pin, ColorChannel Channel);

public sealed record BoardInfo(string Key, string Name, int PinCount);

/// <summary>
/// Immutable compiled copy of the cabinet config. The engine thread only ever reads one of these,
/// so the UI can edit the live config freely and swap in a new snapshot.
/// </summary>
public sealed class PanelSnapshot
{
    public required IReadOnlyList<ButtonInfo> Buttons { get; init; }
    public required IReadOnlyList<BoardInfo> Boards { get; init; }
    public required IReadOnlyDictionary<string, PinTarget[]> Targets { get; init; }
    public double Brightness { get; init; } = 1;
    public double Gamma { get; init; } = 1;
    public int RefreshHz { get; init; } = 30;

    public static PanelSnapshot From(CabinetConfig config)
    {
        var panel = config.Panel;
        double w = Math.Max(1, panel.Width), h = Math.Max(1, panel.Height);

        var buttons = panel.Buttons
            .Select(b => new ButtonInfo(b.Id, b.Label, b.Player, b.Kind, b.X, b.Y, Math.Clamp(b.X / w, 0, 1), Math.Clamp(b.Y / h, 0, 1)))
            .ToArray();

        var targetLists = config.Wiring.Bindings
            .GroupBy(b => b.ButtonId)
            .ToDictionary(g => g.Key, g => g.Select(b => new PinTarget(b.BoardKey, b.Pin, b.Channel)).ToList());
        // A strip segment: every LED in it takes the control's colour (R, G, B on its three pins).
        foreach (var board in config.Wiring.Boards)
        {
            if (board.Segments is not { Count: > 0 } segments) continue;
            foreach (var s in segments)
            {
                if (string.IsNullOrEmpty(s.ButtonId) || s.Count < 1) continue;
                if (!targetLists.TryGetValue(s.ButtonId, out var list)) targetLists[s.ButtonId] = list = new();
                for (int led = Math.Max(1, s.FirstLed); led <= s.LastLed && led * 3 <= board.PinCount; led++)
                {
                    int pin = led * 3 - 2;
                    list.Add(new PinTarget(board.Key, pin, ColorChannel.R));
                    list.Add(new PinTarget(board.Key, pin + 1, ColorChannel.G));
                    list.Add(new PinTarget(board.Key, pin + 2, ColorChannel.B));
                }
            }
        }
        var targets = targetLists.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray());

        var boards = config.Wiring.Boards.Select(b => new BoardInfo(b.Key, b.Name, b.PinCount)).ToArray();

        return new PanelSnapshot
        {
            Buttons = buttons,
            Boards = boards,
            Targets = targets,
            Brightness = Math.Clamp(config.Output.Brightness, 0, 1),
            Gamma = Math.Clamp(config.Output.Gamma, 0.1, 5),
            RefreshHz = Math.Clamp(config.Output.RefreshHz, 1, 120),
        };
    }
}
