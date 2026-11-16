using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace LedNext.FrontEnd.Lite
{
    /// <summary>What a command line asked for: a message for LED-Next, or a note on why there is none.</summary>
    public sealed class ParsedCommand
    {
        public ParsedCommand(LiteMessage message, string note)
        {
            Message = message;
            Note = note;
        }

        /// <summary>Null when the command has nothing for LED-Next (an animation, a port test…).</summary>
        public LiteMessage Message { get; }
        /// <summary>What the command was, in words, for the tool's log.</summary>
        public string Note { get; }
    }

    /// <summary>
    /// LEDBlinky's command line (LEDBlinky's Readme.txt, "Running LEDBlinky in Stand-Alone Mode"), as front ends
    /// that support LEDBlinky send it: HyperSpin, HyperHQ, RetroFE / CoinOPS, Attract-Mode, RocketLauncher…
    /// <list type="bullet">
    /// <item><c>&lt;rom&gt; [&lt;emulator&gt;]</c> or <c>3 &lt;rom&gt; &lt;emulator&gt;</c> (RetroFE): game start · <c>4</c>: game stop</item>
    /// <item><c>9 &lt;rom&gt; [&lt;emulator&gt;]</c>: game selected · <c>15 [&lt;rom&gt;] [&lt;emulator&gt;]</c>: light a game</item>
    /// <item><c>8 [&lt;emulator&gt;]</c>: list (emulator / system) selected</item>
    /// <item><c>1</c> or nothing: front end started · <c>2</c>: front end quit · <c>5</c> / <c>6</c>: screensaver start / stop</item>
    /// <item>Not for LED-Next: <c>*.lwax</c> animations, <c>11</c> animation stop, <c>12</c> MAME controller file,
    /// <c>13</c> U360 joystick, <c>14</c> set ports, <c>16</c> / <c>17</c> pause / unpause.</item>
    /// </list>
    /// A tilde in a value is a space (as LEDBlinky does). Without an emulator, LED-Next uses the last one selected
    /// (else MAME), as LEDBlinky does.
    /// </summary>
    public static class LedBlinkyCommand
    {
        public static ParsedCommand Parse(IReadOnlyList<string> args, string source)
        {
            var a = (args ?? new string[0]).Select(Clean).ToList();
            while (a.Count > 0 && a[a.Count - 1].Length == 0) a.RemoveAt(a.Count - 1); // "9 sf2 """ from a front end
            if (a.Count == 0) return Msg(LiteMessage.Started, source, "front end start (no parameters)");

            var first = a[0];
            string Arg(int i) => i < a.Count && a[i].Length > 0 ? a[i] : null;

            if (int.TryParse(first, NumberStyles.None, CultureInfo.InvariantCulture, out var command))
            {
                switch (command)
                {
                    case 1: return Msg(LiteMessage.Started, source, "front end start");
                    case 2: return Msg(LiteMessage.Closing, source, "front end quit");
                    case 3:
                        // Not in LEDBlinky's readme, but RetroFE / CoinOPS send it for a game start: 3 "<rom>" "<emulator>".
                        if (Arg(1) == null) return new ParsedCommand(null, "game start (3) without a rom: nothing to light");
                        return Game(LiteMessage.Launch, source, Arg(1), Arg(2), "game start");
                    case 4: return Msg(LiteMessage.Exit, source, "game stop");
                    case 5: return Msg(LiteMessage.ScreensaverStart, source, "screensaver start");
                    case 6: return Msg(LiteMessage.ScreensaverStop, source, "screensaver stop");
                    case 8:
                        if (Arg(1) == null) return new ParsedCommand(null, "list change without an emulator: nothing to light");
                        return new ParsedCommand(new LiteMessage(LiteMessage.Select) { Source = source, Platform = Arg(1) },
                            "list change: " + Arg(1));
                    case 9:
                    case 15:
                        if (Arg(1) == null)
                            return new ParsedCommand(null, command == 9 ? "game selected without a rom: nothing to light"
                                : "set game without a rom: LED-Next keeps what it shows");
                        return Game(LiteMessage.Select, source, Arg(1), Arg(2), command == 9 ? "game selected" : "set game");
                    case 11: return new ParsedCommand(null, "animation stop: LED-Next plays its own light shows (Animations tab)");
                    case 12: return new ParsedCommand(null, "load MAME controller file: LED-Next reads MAME's ctrlr files itself");
                    case 13: return new ParsedCommand(null, "U360 joystick reset: not supported");
                    case 14: return new ParsedCommand(null, "set LED ports: not supported");
                    case 16: return new ParsedCommand(null, "game pause: LED-Next takes MAME's own pause output");
                    case 17: return new ParsedCommand(null, "game unpause: LED-Next takes MAME's own pause output");
                    default: return new ParsedCommand(null, "unknown LEDBlinky command " + command);
                }
            }
            if (first.EndsWith(".lwax", StringComparison.OrdinalIgnoreCase))
                return new ParsedCommand(null, "LEDBlinky animation " + first + ": LED-Next plays its own light shows (Animations tab)");
            return Game(LiteMessage.Launch, source, first, Arg(1), "game start");
        }

        private static ParsedCommand Msg(string ev, string source, string note) =>
            new ParsedCommand(new LiteMessage(ev) { Source = source }, note);

        private static ParsedCommand Game(string ev, string source, string rom, string emulator, string what)
        {
            var m = new LiteMessage(ev)
            {
                Source = source,
                Title = RomName(rom),
                ApplicationPath = rom,
                Platform = emulator,
            };
            return new ParsedCommand(m, what + ": " + rom + (emulator == null ? "" : " on " + emulator));
        }

        /// <summary>"C:\roms\sf2.zip" → "sf2"; "sf2" stays.</summary>
        public static string RomName(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            var name = path.Trim().TrimEnd('\\', '/');
            int slash = name.LastIndexOfAny(new[] { '\\', '/' });
            if (slash >= 0) name = name.Substring(slash + 1);
            int dot = name.LastIndexOf('.');
            // Only a short extension is one: "Mr. Do!" and "1942 (Rev. A)" keep their dots.
            return dot > 0 && name.Length - dot <= 5 && name.Substring(dot + 1).All(char.IsLetterOrDigit) ? name.Substring(0, dot) : name;
        }

        /// <summary>Trims, drops surrounding quotes a front end left in, and turns LEDBlinky's tildes into spaces.</summary>
        public static string Clean(string s)
        {
            if (s == null) return "";
            s = s.Trim();
            if (s.Length >= 2 && s[0] == '"' && s[s.Length - 1] == '"') s = s.Substring(1, s.Length - 2).Trim();
            return s.Replace('~', ' ');
        }
    }
}
