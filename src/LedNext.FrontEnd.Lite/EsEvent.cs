using System;
using System.Collections.Generic;
using System.Linq;

namespace LedNext.FrontEnd.Lite
{
    /// <summary>Which EmulationStation sent an event script's arguments.</summary>
    public enum EsFlavor
    {
        /// <summary>
        /// ES-DE: scripts in &lt;ES-DE&gt;\scripts\&lt;event&gt;\*.bat, run with ROM path, game name, system name,
        /// system full name (game-start, game-end, game-select, screensaver-game-select); system-select gets system
        /// name, full name; startup / quit get nothing. game-select / system-select only with "Browsing custom events".
        /// </summary>
        EsDe,
        /// <summary>
        /// RetroBat (Batocera's EmulationStation): a script in &lt;RetroBat&gt;\emulationstation\.emulationstation\scripts
        /// is run with the event name first. game-start: ROM path, ROM name, game name (no system);
        /// game-selected: system name, ROM path, game name; system-selected: system name; start; quit.
        /// </summary>
        RetroBat,
    }

    /// <summary>EmulationStation's custom event scripts (ES-DE and RetroBat), as LED-Next messages.</summary>
    public static class EsEvent
    {
        public static ParsedCommand Parse(EsFlavor flavor, IReadOnlyList<string> args, string source)
        {
            var a = (args ?? new string[0]).Select(Unquote).ToList();
            if (a.Count == 0) return new ParsedCommand(null, "no event name");
            var ev = a[0].ToLowerInvariant();
            string Arg(int i) => i < a.Count && a[i].Length > 0 ? a[i] : null;
            source = source ?? (flavor == EsFlavor.EsDe ? "ES-DE" : "RetroBat");

            switch (ev)
            {
                case "startup":
                case "start":
                    return Msg(LiteMessage.Started, source, ev);
                case "quit":
                    return Msg(LiteMessage.Closing, source, ev + (Arg(1) is string why ? " (" + why + ")" : ""));
                case "game-end":
                    return Msg(LiteMessage.Exit, source, ev);
                case "screensaver-start":
                    return Msg(LiteMessage.ScreensaverStart, source, ev);
                case "screensaver-end":
                case "screensaver-stop":
                    return Msg(LiteMessage.ScreensaverStop, source, ev);
            }

            if (flavor == EsFlavor.EsDe)
            {
                switch (ev)
                {
                    case "game-start":
                        return Game(LiteMessage.Launch, source, rom: Arg(1), name: Arg(2), system: Arg(3), ev);
                    case "game-select":
                        if (Arg(1) == null) return new ParsedCommand(null, "game-select while fast scrolling (no game yet)");
                        return Game(LiteMessage.Select, source, rom: Arg(1), name: Arg(2), system: Arg(3), ev);
                    case "system-select":
                        if (Arg(1) == null) return new ParsedCommand(null, "system-select without a system");
                        return new ParsedCommand(new LiteMessage(LiteMessage.Select) { Source = source, Platform = Arg(1) },
                            ev + ": " + Arg(1) + (Arg(2) is string full ? " (" + full + ")" : ""));
                    case "screensaver-game-select":
                        return new ParsedCommand(null, ev + ": the screensaver's games are not lit");
                }
            }
            else
            {
                switch (ev)
                {
                    case "game-start":
                        // No system here: the folder the ROM is in ("…\roms\snes\Game.sfc" → snes), else the last selected.
                        return Game(LiteMessage.Launch, source, rom: Arg(1), name: Arg(3) ?? Arg(2), system: SystemFromPath(Arg(1)), ev);
                    case "game-selected":
                        if (Arg(2) == null) return new ParsedCommand(null, "game-selected without a game");
                        return Game(LiteMessage.Select, source, rom: Arg(2), name: Arg(3), system: Arg(1), ev);
                    case "system-selected":
                        if (Arg(1) == null) return new ParsedCommand(null, "system-selected without a system");
                        return new ParsedCommand(new LiteMessage(LiteMessage.Select) { Source = source, Platform = Arg(1) }, ev + ": " + Arg(1));
                }
            }
            return new ParsedCommand(null, "event '" + ev + "' isn't used by LED-Next");
        }

        private static ParsedCommand Msg(string ev, string source, string note) =>
            new ParsedCommand(new LiteMessage(ev) { Source = source }, note);

        private static ParsedCommand Game(string ev, string source, string rom, string name, string system, string what)
        {
            if (rom == null && name == null) return new ParsedCommand(null, what + " without a game");
            var m = new LiteMessage(ev)
            {
                Source = source,
                Title = name ?? LedBlinkyCommand.RomName(rom),
                ApplicationPath = rom ?? name,
                Platform = system,
            };
            return new ParsedCommand(m, what + ": " + (name ?? rom) + (system == null ? "" : " (" + system + ")"));
        }

        /// <summary>"D:\RetroBat\roms\snes\Game.sfc" → "snes" (the folder under "roms"); null when it isn't there.</summary>
        public static string SystemFromPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            var parts = path.Replace('/', '\\').Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = parts.Length - 3; i >= 0; i--)
                if (parts[i].Equals("roms", StringComparison.OrdinalIgnoreCase)) return parts[i + 1];
            return null;
        }

        private static string Unquote(string s)
        {
            if (s == null) return "";
            s = s.Trim();
            if (s.Length >= 2 && s[0] == '"' && s[s.Length - 1] == '"') s = s.Substring(1, s.Length - 2).Trim();
            return s;
        }
    }
}
