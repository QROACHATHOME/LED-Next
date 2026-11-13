namespace LedNext.Core.Outputs;

/// <summary>
/// One running emulator's outputs, turned into lamp states per control. Pure logic (no windows, sockets or
/// boards), so it is unit-tested. An output value of 0 is off, anything else is on (as in LEDBlinky). A
/// control driven by several outputs is on while any of them is on.
/// </summary>
public sealed class OutputSession
{
    private readonly GameOutputMap _map;
    private readonly Dictionary<string, int> _values = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _reported = new(StringComparer.OrdinalIgnoreCase);

    public OutputSession(GameOutputMap map, string emulator, string game)
    {
        _map = map;
        Emulator = emulator;
        Game = game;
    }

    public OutputSession(IOutputMap map, string emulator, string game, IReadOnlyList<string> gameChain)
        : this(map.For(gameChain), emulator, game) { }

    /// <summary>"MAME", "Flycast"…</summary>
    public string Emulator { get; }
    /// <summary>The game id the emulator reported (a MAME rom, a Flycast game name).</summary>
    public string Game { get; }
    /// <summary>What the map uses for this game ("galaga", "default"…), for the log.</summary>
    public string Section => _map.Section;
    /// <summary>Outputs the game has reported so far (all of them, mapped or not), for the log.</summary>
    public IReadOnlyDictionary<string, int> Values => _values;

    /// <summary>The controls an output drives in this game.</summary>
    public IReadOnlyList<string> ControlsFor(string output) => _map.ControlsFor(output);

    /// <summary>Records an output's value; returns the controls whose lamp state this changes, with the new state.</summary>
    public List<(string Control, bool On)> Update(string output, int value)
    {
        var controls = _map.ControlsFor(output);
        var before = controls.ToDictionary(c => c, IsOn, StringComparer.OrdinalIgnoreCase);
        _values[output] = value;
        var changes = new List<(string, bool)>();
        foreach (var c in controls)
        {
            bool now = IsOn(c);
            // A control's first state counts as a change, so its lamp is set (even to off).
            if (_reported.Add(c) || now != before[c]) changes.Add((c, now));
        }
        return changes;
    }

    /// <summary>True while any output mapped to this control is on.</summary>
    public bool IsOn(string control) =>
        _values.Any(kv => kv.Value != 0 && _map.ControlsFor(kv.Key).Contains(control, StringComparer.OrdinalIgnoreCase));
}
