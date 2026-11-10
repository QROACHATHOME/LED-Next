namespace LedNext.Core;

/// <summary>A physical (or simulated) LED controller board.</summary>
public interface ILedBoard : IDisposable
{
    /// <summary>Stable key, e.g. "PacLED64#1". Must not depend on USB enumeration order.</summary>
    string Key { get; }
    string Model { get; }
    string DisplayName { get; }
    int PinCount { get; }
    bool IsSimulated { get; }

    /// <summary>
    /// Sets every pin's intensity. <paramref name="intensities"/>[0] is pin 1.
    /// Called only from the engine output thread.
    /// </summary>
    void Write(ReadOnlySpan<byte> intensities);
}

public interface ILedBoardProvider : IDisposable
{
    string Name { get; }
    IReadOnlyList<ILedBoard> Discover();
}
