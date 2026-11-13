using LedNext.Core.Games;

namespace LedNext.Core.Outputs;

/// <summary>
/// One row of LED-Next's own lamp table (cabinet.json <c>lamps.maps</c>, config version 7):
/// <c>Flycast / SEGA MARINE FISHING JAPAN / lamp0 → P1_START</c>.
/// </summary>
public sealed class LampMapEntry
{
    /// <summary>The emulator as its adapter names it: "MAME", "Flycast".</summary>
    public string Emulator { get; set; } = "";
    /// <summary>The game id the emulator reports (MAME rom, Flycast game name), or "*" for every game.</summary>
    public string Game { get; set; } = "*";
    /// <summary>The output name the emulator reports ("led0", "lamp0").</summary>
    public string Output { get; set; } = "";
    /// <summary>Panel controls it drives (P1_START, P1_BUTTON1…).</summary>
    public List<string> Controls { get; set; } = new();
}

/// <summary>
/// LED-Next's own lamp table for one emulator. A game's rows (the first of the game chain that has any) win per
/// output over the "*" rows. Control names are normalised like MameOutputs.ini's ("Start1" → P1_START).
/// </summary>
public sealed class LampMapTable : IOutputMap
{
    public const string AllGames = "*";
    private readonly Dictionary<string, Dictionary<string, List<string>>> _games = new(StringComparer.OrdinalIgnoreCase);

    public LampMapTable(string emulator, IEnumerable<LampMapEntry>? entries, string source = "cabinet.json")
    {
        Emulator = emulator;
        Source = source;
        foreach (var e in entries ?? Array.Empty<LampMapEntry>())
        {
            if (e is null || !string.Equals(e.Emulator?.Trim(), emulator, StringComparison.OrdinalIgnoreCase)) continue;
            var output = e.Output?.Trim() ?? "";
            var game = string.IsNullOrWhiteSpace(e.Game) ? AllGames : e.Game.Trim();
            if (output.Length == 0) { Skipped++; continue; }
            foreach (var raw in e.Controls ?? new())
            {
                if (ControlNames.Normalize(raw ?? "") is not { } control) { Skipped++; continue; }
                if (!_games.TryGetValue(game, out var outputs))
                    _games[game] = outputs = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                if (!outputs.TryGetValue(output, out var list)) outputs[output] = list = new List<string>();
                if (!list.Contains(control, StringComparer.OrdinalIgnoreCase)) list.Add(control);
            }
        }
    }

    public string Emulator { get; }
    public string Source { get; }
    /// <summary>Rows (or controls) that were left out: no output name, or not a control name.</summary>
    public int Skipped { get; }
    public bool IsEmpty => _games.Count == 0;

    public GameOutputMap For(IReadOnlyList<string> gameChain)
    {
        var game = gameChain.FirstOrDefault(g => g != AllGames && _games.ContainsKey(g));
        _games.TryGetValue(game ?? "", out var own);
        _games.TryGetValue(AllGames, out var all);
        if (own is null && all is null) return new GameOutputMap("nothing mapped", _ => Array.Empty<string>());
        var section = game is null ? AllGames : all is null ? game : $"{game}, then {AllGames}";
        return new GameOutputMap(section, o =>
            own is not null && own.TryGetValue(o, out var a) ? a :
            all is not null && all.TryGetValue(o, out var b) ? b : Array.Empty<string>());
    }
}
