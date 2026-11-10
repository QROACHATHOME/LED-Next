using LedNext.Core;

namespace LedNext.Hardware;

/// <summary>
/// Creates a virtual board for every board in the wiring config, using the same keys,
/// so wiring designed without hardware carries over unchanged to the cabinet.
/// </summary>
public sealed class SimulatedBoardProvider(IEnumerable<BoardDefinition> boards) : ILedBoardProvider
{
    private readonly BoardDefinition[] _boards = boards.ToArray();

    public string Name => "Simulator";

    public IReadOnlyList<ILedBoard> Discover() =>
        _boards.Select(b => (ILedBoard)new SimulatedBoard(b.Key, b.Model, b.Name, b.PinCount)).ToArray();

    public void Dispose() { }
}

public sealed class SimulatedBoard(string key, string model, string name, int pinCount) : ILedBoard
{
    private readonly byte[] _pins = new byte[pinCount];

    public string Key { get; } = key;
    public string Model { get; } = model;
    public string DisplayName { get; } = $"{name} (simulated)";
    public int PinCount { get; } = pinCount;
    public bool IsSimulated => true;
    public int WriteCount { get; private set; }

    public void Write(ReadOnlySpan<byte> intensities)
    {
        intensities[..Math.Min(intensities.Length, _pins.Length)].CopyTo(_pins);
        WriteCount++;
    }

    public void Dispose() { }
}
