namespace LedNext.Core;

public enum ColorChannel { R, G, B }

/// <summary>A board the cabinet expects to have. It may be offline at any given time.</summary>
public sealed class BoardDefinition
{
    /// <summary>Stable key, e.g. "PacLED64#1". Matches <see cref="ILedBoard.Key"/>.</summary>
    public string Key { get; set; } = "";
    public string Model { get; set; } = "PacLED64";
    public string Name { get; set; } = "";
    public int PinCount { get; set; } = 64;
    public double GraphX { get; set; }
    public double GraphY { get; set; }
}

/// <summary>One colour channel of one button, wired to one board pin. Pins are 1-based.</summary>
public sealed class PinBinding
{
    public string ButtonId { get; set; } = "";
    public ColorChannel Channel { get; set; }
    public string BoardKey { get; set; } = "";
    public int Pin { get; set; }
}

public sealed class GraphPoint
{
    public double X { get; set; }
    public double Y { get; set; }
}

public sealed class WiringMap
{
    public List<BoardDefinition> Boards { get; set; } = new();
    public List<PinBinding> Bindings { get; set; } = new();
    /// <summary>Wiring-graph node positions for buttons, by button ID.</summary>
    public Dictionary<string, GraphPoint> ButtonNodePositions { get; set; } = new();

    public BoardDefinition? FindBoard(string key) => Boards.FirstOrDefault(b => b.Key == key);

    public PinBinding? Find(string buttonId, ColorChannel channel) =>
        Bindings.FirstOrDefault(b => b.ButtonId == buttonId && b.Channel == channel);

    public PinBinding? FindByPin(string boardKey, int pin) =>
        Bindings.FirstOrDefault(b => b.BoardKey == boardKey && b.Pin == pin);

    /// <summary>
    /// Wires a button channel to a pin. Any existing binding on that channel or that pin is
    /// replaced, so a pin is always driven by at most one channel and vice versa.
    /// </summary>
    public void Connect(string buttonId, ColorChannel channel, string boardKey, int pin)
    {
        var board = FindBoard(boardKey) ?? throw new ArgumentException($"Unknown board '{boardKey}'", nameof(boardKey));
        if (pin < 1 || pin > board.PinCount)
            throw new ArgumentOutOfRangeException(nameof(pin), $"Pin {pin} is outside 1..{board.PinCount} on {boardKey}");

        Bindings.RemoveAll(b => (b.ButtonId == buttonId && b.Channel == channel) || (b.BoardKey == boardKey && b.Pin == pin));
        Bindings.Add(new PinBinding { ButtonId = buttonId, Channel = channel, BoardKey = boardKey, Pin = pin });
    }

    public void Disconnect(string buttonId, ColorChannel channel) =>
        Bindings.RemoveAll(b => b.ButtonId == buttonId && b.Channel == channel);

    public void DisconnectPin(string boardKey, int pin) =>
        Bindings.RemoveAll(b => b.BoardKey == boardKey && b.Pin == pin);

    public void RemoveButton(string buttonId)
    {
        Bindings.RemoveAll(b => b.ButtonId == buttonId);
        ButtonNodePositions.Remove(buttonId);
    }

    public void RenameButton(string oldId, string newId)
    {
        foreach (var b in Bindings.Where(b => b.ButtonId == oldId)) b.ButtonId = newId;
        if (ButtonNodePositions.Remove(oldId, out var pos)) ButtonNodePositions[newId] = pos;
    }

    /// <summary>
    /// Clears all bindings and wires buttons in panel order: R, G, B on consecutive pins,
    /// filling each board before moving to the next. Returns the number of buttons that did not fit.
    /// </summary>
    public int AutoWire(PanelLayout panel)
    {
        Bindings.Clear();
        int boardIndex = 0, pin = 1, unwired = 0;
        foreach (var button in panel.Buttons)
        {
            while (boardIndex < Boards.Count && pin + 2 > Boards[boardIndex].PinCount)
            {
                boardIndex++;
                pin = 1;
            }
            if (boardIndex >= Boards.Count) { unwired++; continue; }

            var key = Boards[boardIndex].Key;
            Connect(button.Id, ColorChannel.R, key, pin);
            Connect(button.Id, ColorChannel.G, key, pin + 1);
            Connect(button.Id, ColorChannel.B, key, pin + 2);
            pin += 3;
        }
        return unwired;
    }
}
