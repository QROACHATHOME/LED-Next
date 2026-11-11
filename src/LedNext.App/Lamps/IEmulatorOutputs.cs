namespace LedNext.App.Lamps;

/// <summary>A source of an emulator's live outputs. Events are raised on the UI thread.</summary>
public interface IEmulatorOutputs : IDisposable
{
    /// <summary>How the outputs arrive, for the log ("Windows messages", "network port 8000").</summary>
    string Via { get; }
    /// <summary>An emulator started a game: (rom, via).</summary>
    event Action<string, string>? Started;
    /// <summary>The game ended: (via).</summary>
    event Action<string>? Stopped;
    /// <summary>An output changed: (name, value). 0 = off, anything else = on.</summary>
    event Action<string, int>? Output;
    void Start();
}
