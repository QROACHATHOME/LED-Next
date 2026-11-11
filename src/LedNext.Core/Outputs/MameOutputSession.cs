namespace LedNext.Core.Outputs;

/// <summary>
/// One running emulator's outputs, turned into lamp states per control. Pure logic (no windows, sockets or
/// boards), so it is unit-tested. An output value of 0 is off, anything else is on (as in LEDBlinky). A
/// control driven by several outputs is on while any of them is on.
/// </summary>
public sealed class MameOutputSession
{
    private readonly MameOutputMap _map;
    private readonly Dictionary<string, int> _values = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _reported = new(StringComparer.OrdinalIgnoreCase);

    public MameOutputSession(MameOutputMap map, string rom, IEnumerable<string> romChain)
    {
        _map = map;
        Rom = rom;
        Section = map.SectionFor(romChain);
    }

    public string Rom { get; }
    /// <summary>The MameOutputs.ini section in use ("galaga", or "default").</summary>
    public string Section { get; }
    /// <summary>Outputs the game has reported so far (all of them, mapped or not), for the log.</summary>
    public IReadOnlyDictionary<string, int> Values => _values;

    /// <summary>Records an output's value; returns the controls whose lamp state this changes, with the new state.</summary>
    public List<(string Control, bool On)> Update(string output, int value)
    {
        var controls = _map.ControlsFor(Section, output);
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
        _values.Any(kv => kv.Value != 0 && _map.ControlsFor(Section, kv.Key).Contains(control, StringComparer.OrdinalIgnoreCase));
}
