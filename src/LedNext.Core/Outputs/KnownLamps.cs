namespace LedNext.Core.Outputs;

/// <summary>One output a game reported during one session: what it was mapped to and the values it took.</summary>
public sealed record LampOutputSeen(string Output, string MappedTo, int Min, int Max, int Changes);

/// <summary>
/// An output an emulator has reported for a game, as kept in the game library (games.db <c>lamp_outputs</c>,
/// schema 8): every game's lamps in one place, for the Lamps tab and for lamp triggers later.
/// </summary>
public sealed record KnownLamp(
    string Emulator, string Game, string Output, string MappedTo, string MapSource,
    DateTime FirstSeen, DateTime LastSeen, int Sessions, int Min, int Max, int Changes)
{
    public bool Mapped => MappedTo.Length > 0;
}

/// <summary>
/// Collects the outputs of the running game (UI thread), to store them in the library when the game ends rather
/// than writing to the database while it plays.
/// </summary>
public sealed class LampOutputLog(string emulator, string game, string mapSource)
{
    private readonly Dictionary<string, (string MappedTo, int Min, int Max, int Changes, int Last)> _seen =
        new(StringComparer.OrdinalIgnoreCase);

    public string Emulator { get; } = emulator;
    public string Game { get; } = game;
    public string MapSource { get; } = mapSource;
    public int Count => _seen.Count;

    /// <param name="mappedTo">The controls it drives ("P1_START, P1_BUTTON1"), empty when not mapped.</param>
    public void Add(string output, int value, IReadOnlyList<string> mappedTo)
    {
        if (string.IsNullOrWhiteSpace(output)) return;
        if (_seen.TryGetValue(output, out var s))
        {
            if (value == s.Last) return;
            _seen[output] = (s.MappedTo, Math.Min(s.Min, value), Math.Max(s.Max, value), s.Changes + 1, value);
        }
        else _seen[output] = (string.Join(", ", mappedTo), value, value, 0, value);
    }

    public IReadOnlyList<LampOutputSeen> Outputs() =>
        _seen.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase)
             .Select(k => new LampOutputSeen(k.Key, k.Value.MappedTo, k.Value.Min, k.Value.Max, k.Value.Changes))
             .ToArray();
}
