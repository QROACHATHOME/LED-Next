using System;
using System.Text;

namespace LedNext.FrontEnd.Lite
{
    /// <summary>
    /// One front-end message, as LedNext.FrontEnd's FrontEndMessage (same event names and JSON fields), for the
    /// .NET Framework helpers. <see cref="ToJson"/> writes the line LED-Next reads from the pipe.
    /// </summary>
    public sealed class LiteMessage
    {
        // The same values as FrontEndEvents (LedNext.FrontEnd); a test checks they stay equal.
        public const string Select = "select";
        public const string Launch = "launch";
        public const string Exit = "exit";
        public const string Started = "started";
        public const string Closing = "closing";
        /// <summary>The front end's screensaver started / stopped (logged; LED-Next runs its own idle show).</summary>
        public const string ScreensaverStart = "screensaver-start";
        public const string ScreensaverStop = "screensaver-stop";

        public LiteMessage(string eventName) { Event = eventName; }

        public string Event { get; }
        /// <summary>"HyperSpin", "RetroFE", "ES-DE", "PinballX"…</summary>
        public string Source { get; set; }
        public string GameId { get; set; }
        public string Title { get; set; }
        /// <summary>The front end's system / emulator / platform name ("MAME", "Sega Genesis", "snes").</summary>
        public string Platform { get; set; }
        /// <summary>The rom or table path, or just the rom name: LED-Next matches games by its file name.</summary>
        public string ApplicationPath { get; set; }
        public long? SentAtMs { get; set; }

        public override string ToString()
        {
            var s = (Source ?? "?") + " " + Event;
            if (!string.IsNullOrEmpty(Title)) s += ": " + Title + (string.IsNullOrEmpty(Platform) ? "" : " (" + Platform + ")");
            else if (!string.IsNullOrEmpty(Platform)) s += ": platform " + Platform;
            return s;
        }

        /// <summary>One JSON object on one line, camelCase names, nulls left out (as FrontEndProtocol.Serialize).</summary>
        public string ToJson()
        {
            var b = new StringBuilder("{");
            Add(b, "event", Event);
            Add(b, "source", Source);
            Add(b, "gameId", GameId);
            Add(b, "title", Title);
            Add(b, "platform", Platform);
            Add(b, "applicationPath", ApplicationPath);
            if (SentAtMs.HasValue)
            {
                if (b.Length > 1) b.Append(',');
                b.Append("\"sentAtMs\":").Append(SentAtMs.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            return b.Append('}').ToString();
        }

        private static void Add(StringBuilder b, string name, string value)
        {
            if (value == null) return;
            if (b.Length > 1) b.Append(',');
            b.Append('"').Append(name).Append("\":");
            Quote(b, value);
        }

        /// <summary>A JSON string: quotes, backslashes and control characters escaped, so the line stays one line.</summary>
        public static void Quote(StringBuilder b, string s)
        {
            b.Append('"');
            foreach (var c in s)
            {
                switch (c)
                {
                    case '"': b.Append("\\\""); break;
                    case '\\': b.Append("\\\\"); break;
                    case '\n': b.Append("\\n"); break;
                    case '\r': b.Append("\\r"); break;
                    case '\t': b.Append("\\t"); break;
                    default:
                        if (c < 0x20) b.Append("\\u").Append(((int)c).ToString("x4"));
                        else b.Append(c);
                        break;
                }
            }
            b.Append('"');
        }

        public static long NowMs() => (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
    }
}
