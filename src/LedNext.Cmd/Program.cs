using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using LedNext.FrontEnd.Lite;

namespace LedNext.Cmd
{
    /// <summary>
    /// LedNextCmd.exe (also shipped as LEDBlinky-stand-in\LEDBlinky.exe, for front ends that look for that name).
    /// <code>
    /// LedNextCmd.exe [--source NAME] &lt;LEDBlinky command line&gt;     9 sf2 MAME · sf2 MAME · 4 · 1 · 2 …
    /// LedNextCmd.exe [--source NAME] --es-de &lt;event&gt; [args…]       ES-DE event scripts
    /// LedNextCmd.exe [--source NAME] --retrobat &lt;event&gt; [args…]    RetroBat event scripts
    /// </code>
    /// Hands the event to the running LED-Next and exits (always with code 0: a front end must never stop over
    /// lights). A front-end start also starts LED-Next if it isn't running and LED-Next.exe is beside this tool.
    /// Every command is logged to &lt;LED-Next&gt;\logs\led-next-commands.log.
    /// </summary>
    internal static class Program
    {
        private const int SendTimeoutMs = 1500;
        private static readonly TimeSpan StartWait = TimeSpan.FromSeconds(20);

        [STAThread]
        private static int Main(string[] args)
        {
            var folder = LedNextFolder();
            string line = string.Join(" ", args.Select(a => a.IndexOf(' ') >= 0 || a.Length == 0 ? "\"" + a + "\"" : a));
            try
            {
                Run(args, folder, line);
            }
            catch (Exception ex)
            {
                Log(folder, line, "failed: " + ex.Message);
            }
            return 0;
        }

        private static void Run(string[] args, string folder, string line)
        {
            var rest = new List<string>(args);
            string source = null;
            string mode = null;
            while (rest.Count > 0 && rest[0].StartsWith("--", StringComparison.Ordinal))
            {
                var opt = rest[0].ToLowerInvariant();
                rest.RemoveAt(0);
                if (opt == "--source" && rest.Count > 0) { source = rest[0]; rest.RemoveAt(0); }
                else if (opt == "--es-de" || opt == "--retrobat") mode = opt;
                else if (opt == "--help") { Log(folder, line, "help: see docs/FRONT_ENDS.md"); return; }
            }
            source = source ?? FrontEndName();

            var parsed = mode == "--es-de" ? EsEvent.Parse(EsFlavor.EsDe, rest, source)
                : mode == "--retrobat" ? EsEvent.Parse(EsFlavor.RetroBat, rest, source)
                : LedBlinkyCommand.Parse(rest, source);
            var m = parsed.Message;
            if (m == null)
            {
                Log(folder, line, source + ": " + parsed.Note + " (not sent)");
                return;
            }
            // When this tool started, not when it got here: several copies run at once while a front end scrolls
            // (RetroBat starts its scripts without waiting), and LED-Next keeps the newest selection by this time.
            m.SentAtMs = StartedAtMs();

            if (LitePipe.SendOnce(m, SendTimeoutMs, out var error))
            {
                Log(folder, line, source + ": " + parsed.Note + " → sent");
                return;
            }
            if (m.Event == LiteMessage.Started && StartLedNext(folder, out var started))
            {
                var until = DateTime.UtcNow + StartWait;
                while (DateTime.UtcNow < until)
                {
                    if (LitePipe.SendOnce(m, SendTimeoutMs, out error))
                    {
                        Log(folder, line, source + ": " + parsed.Note + " → started " + started + " and sent");
                        return;
                    }
                    Thread.Sleep(250);
                }
                Log(folder, line, source + ": " + parsed.Note + " → started " + started + " but it didn't answer: " + error);
                return;
            }
            Log(folder, line, source + ": " + parsed.Note + " → not sent: " + error);
        }

        /// <summary>LED-Next's folder: this tool's, or the one above it (the LEDBlinky stand-in sits in a subfolder).</summary>
        private static string LedNextFolder()
        {
            var here = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
            if (File.Exists(Path.Combine(here, "LED-Next.exe"))) return here;
            var up = Path.GetDirectoryName(here);
            if (up != null && File.Exists(Path.Combine(up, "LED-Next.exe"))) return up;
            return here;
        }

        private static bool StartLedNext(string folder, out string exe)
        {
            exe = Path.Combine(folder, "LED-Next.exe");
            if (!File.Exists(exe)) return false;
            // A second LED-Next would only bring the first one forward; one that is still starting answers soon.
            if (Process.GetProcessesByName("LED-Next").Length > 0) return true;
            Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = folder, UseShellExecute = true });
            return true;
        }

        private static long StartedAtMs()
        {
            try
            {
                var start = Process.GetCurrentProcess().StartTime.ToUniversalTime();
                return (long)(start - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
            }
            catch
            {
                return LiteMessage.NowMs();
            }
        }

        // ---- the front end that ran us ------------------------------------------------------------------------

        /// <summary>
        /// The program that started this tool, by its process name ("HyperSpin", "RetroFE"…), skipping cmd.exe
        /// and other shells a script runs in. "Command line" when there is none to name.
        /// </summary>
        private static string FrontEndName()
        {
            try
            {
                var names = ParentNames(Process.GetCurrentProcess().Id, 4);
                foreach (var n in names)
                {
                    var lower = n.ToLowerInvariant();
                    if (lower == "cmd" || lower == "conhost" || lower == "powershell" || lower == "pwsh" || lower == "wscript"
                        || lower == "cscript" || lower == "explorer" || lower == "ledblinky" || lower == "lednextcmd") continue;
                    return Friendly(n);
                }
            }
            catch { }
            return "Command line";
        }

        private static string Friendly(string process)
        {
            switch (process.ToLowerInvariant())
            {
                case "hyperspin": return "HyperSpin";
                case "hyperhq": return "HyperHQ";
                case "retrofe": return "RetroFE";
                case "attract": case "attractplus": return "Attract-Mode";
                case "rocketlauncher": return "RocketLauncher";
                case "es-de": return "ES-DE";
                case "emulationstation": case "retrobat": return "RetroBat";
                case "pinballx": return "PinballX";
                case "pinupmenu": case "pinupmenusetup": case "pinupdisplay": return "PinUP Popper";
                case "gameex": return "GameEx";
                default: return process;
            }
        }

        private static List<string> ParentNames(int pid, int depth)
        {
            var parents = new Dictionary<int, int>();
            var names = new Dictionary<int, string>();
            var snap = CreateToolhelp32Snapshot(0x2, 0);
            if (snap == IntPtr.Zero || snap == new IntPtr(-1)) return new List<string>();
            try
            {
                var e = new PROCESSENTRY32 { dwSize = (uint)Marshal.SizeOf(typeof(PROCESSENTRY32)) };
                if (Process32First(snap, ref e))
                {
                    do
                    {
                        parents[(int)e.th32ProcessID] = (int)e.th32ParentProcessID;
                        names[(int)e.th32ProcessID] = Path.GetFileNameWithoutExtension(e.szExeFile);
                    } while (Process32Next(snap, ref e));
                }
            }
            finally
            {
                CloseHandle(snap);
            }
            var list = new List<string>();
            int current = pid;
            for (int i = 0; i < depth && parents.TryGetValue(current, out var parent) && parent != 0 && parent != current; i++)
            {
                if (!names.TryGetValue(parent, out var name)) break;
                list.Add(name);
                current = parent;
            }
            return list;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct PROCESSENTRY32
        {
            public uint dwSize;
            public uint cntUsage;
            public uint th32ProcessID;
            public IntPtr th32DefaultHeapID;
            public uint th32ModuleID;
            public uint cntThreads;
            public uint th32ParentProcessID;
            public int pcPriClassBase;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExeFile;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "Process32FirstW")]
        private static extern bool Process32First(IntPtr snapshot, ref PROCESSENTRY32 entry);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "Process32NextW")]
        private static extern bool Process32Next(IntPtr snapshot, ref PROCESSENTRY32 entry);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);

        // ---- log ------------------------------------------------------------------------------------------------

        /// <summary>One line per command; the file is started again once it passes 512 KB. Never throws.</summary>
        private static void Log(string folder, string commandLine, string what)
        {
            try
            {
                var dir = Path.Combine(folder, "logs");
                Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, "led-next-commands.log");
                var info = new FileInfo(path);
                if (info.Exists && info.Length > 512 * 1024) File.Delete(path);
                var text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  [" + commandLine + "]  " + what + Environment.NewLine;
                for (int attempt = 0; attempt < 5; attempt++)
                {
                    try
                    {
                        using (var f = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                        {
                            var bytes = new UTF8Encoding(false).GetBytes(text);
                            f.Write(bytes, 0, bytes.Length);
                        }
                        return;
                    }
                    catch (IOException)
                    {
                        Thread.Sleep(15); // another copy of this tool is writing
                    }
                }
            }
            catch { }
        }
    }
}
