namespace LedNext.Core.Outputs;

/// <summary>
/// Which panel controls an emulator's outputs drive. One map per emulator (MAME: LEDBlinky's MameOutputs.ini;
/// any emulator: LED-Next's own table in cabinet.json). Pure data, no I/O after loading.
/// </summary>
public interface IOutputMap
{
    /// <summary>Where the map comes from, for the log ("C:\LEDBlinky\MameOutputs.ini", "cabinet.json").</summary>
    string Source { get; }
    /// <summary>The outputs of one game. <paramref name="gameChain"/> is the game, then its parents (MAME clones).</summary>
    GameOutputMap For(IReadOnlyList<string> gameChain);
}

/// <summary>One game's output map: which section is used, and output name → controls.</summary>
public sealed class GameOutputMap
{
    private readonly Func<string, IReadOnlyList<string>> _controls;

    public GameOutputMap(string section, Func<string, IReadOnlyList<string>> controls)
    {
        Section = section;
        _controls = controls;
    }

    /// <summary>What the lookup uses, for the log ("galaga", "default", "cabinet.json [*], then MameOutputs.ini [default]").</summary>
    public string Section { get; }

    /// <summary>The controls an output drives (empty when it drives none).</summary>
    public IReadOnlyList<string> ControlsFor(string output) => _controls(output);

    public static GameOutputMap Empty { get; } = new("nothing mapped", _ => Array.Empty<string>());
}

public static class OutputMaps
{
    /// <summary>
    /// <paramref name="first"/> wins per output; outputs it doesn't map fall back to <paramref name="then"/>.
    /// Used so cabinet.json can add to or override LEDBlinky's MameOutputs.ini without writing to it.
    /// </summary>
    public static IOutputMap Layer(IOutputMap first, IOutputMap then) => new Layered(first, then);

    private sealed class Layered(IOutputMap first, IOutputMap then) : IOutputMap
    {
        public string Source => $"{first.Source}, then {then.Source}";

        public GameOutputMap For(IReadOnlyList<string> gameChain)
        {
            var a = first.For(gameChain);
            var b = then.For(gameChain);
            return new GameOutputMap($"[{a.Section}], then [{b.Section}]", o => a.ControlsFor(o) is { Count: > 0 } c ? c : b.ControlsFor(o));
        }
    }
}
