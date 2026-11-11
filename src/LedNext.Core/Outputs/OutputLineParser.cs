using System.Globalization;

namespace LedNext.Core.Outputs;

/// <summary>What one line of an emulator's network output means.</summary>
public abstract record OutputLine
{
    /// <summary>A game started (MAME "mame_start = rom", Flycast "game = id").</summary>
    public sealed record Start(string Game, string Emulator) : OutputLine;
    /// <summary>The game ended (MAME "mame_stop = 1"; Flycast just closes the connection).</summary>
    public sealed record Stop : OutputLine;
    public sealed record Value(string Name, int Number) : OutputLine;
}

/// <summary>
/// Reads the network output of one connection, line by line ("name = value"). Two dialects:
/// <list type="bullet">
/// <item>MAME (network.cpp): "mame_start = rom" first, decimal values, "mame_stop = 1", "pause = 0/1".</item>
/// <item>Flycast (core/network/output.h): "game = id" before the first value, values in hexadecimal, lines end
/// in '\n', no stop message.</item>
/// </list>
/// Values before a start line are dropped. Pure logic, so it is unit-tested.
/// </summary>
public sealed class OutputLineParser
{
    private bool _hex;

    /// <summary>The game of this connection, or null before a start line.</summary>
    public string? Game { get; private set; }

    public OutputLine? Feed(string line)
    {
        int eq = line.IndexOf('=');
        if (eq <= 0) return null;
        var name = line[..eq].Trim();
        var value = line[(eq + 1)..].Trim();
        switch (name)
        {
            case "mame_start":
                return StartGame(value, "MAME", hex: false);
            case "game":
                return Game == value ? null : StartGame(value, "Flycast", hex: true);
            case "mame_stop":
                if (Game is null) return null;
                Game = null;
                return new OutputLine.Stop();
            case "pause":
                return null;
            default:
                if (Game is null) return null;
                bool ok = _hex
                    ? int.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var n)
                    : int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out n);
                return ok ? new OutputLine.Value(name, n) : null;
        }
    }

    private OutputLine? StartGame(string game, string emulator, bool hex)
    {
        if (game.Length == 0) return null;
        Game = game;
        _hex = hex;
        return new OutputLine.Start(game, emulator);
    }
}
