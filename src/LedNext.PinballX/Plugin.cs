using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using LedNext.FrontEnd.Lite;

namespace PinballX
{
    /// <summary>What PinballX passes to <see cref="Plugin.Initialize"/> (as DirectOutput's plugin declares it).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct PinballXInfo
    {
        public string Version;
    }

    /// <summary>What PinballX passes to the game events (field order as in DirectOutput's plugin; do not reorder).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct GameInfo
    {
        public string System;
        public string SystemName;
        public string INI;
        public string GameName;
        public string GameDescription;
        public string GameShortDescription;
        public string TableFile;
        public string TablePath;
        public string Parameters;
        public int DisplayWidth;
        public int DisplayHeight;
        public int DisplayRotation;
        public bool DisplayWindowed;
        public int PlayFieldDisplayNbr;
        public int BackGlassDisplayNbr;
        public int DMDDisplayNbr;
        public string Filter;
        public string VideoImage_Playfield;
        public string VideoImage_BackGlass;
        public string VideoImage_DMD;
        public string VideoImage_Wheel;
        public string LastPlayed;
        public long TimesPlayed;
        public long SecondsPlayed;
        public string HighScore_1;
        public string HighScore_2;
        public string HighScore_3;
        public int Rating;
        public string ROM;
        public string PinMAMEROM;
    }

    /// <summary>
    /// LED-Next's PinballX plugin: the selected table, a game start and exit, the screensaver and PinballX closing go
    /// to LED-Next over its pipe, without ever making PinballX wait (a background thread writes them). Every method
    /// catches everything: a plugin must never take PinballX down. Logs to "LED-Next PinballX Plugin.log" beside
    /// the DLL.
    /// </summary>
    public class Plugin
    {
        private const string Source = "PinballX";
        private LiteClient _client;

        public string Name => "LED-Next";
        public string Version => typeof(Plugin).Assembly.GetName().Version.ToString(3);
        public string Author => "LED-Next contributors";
        public string Description => "Lights the cabinet's buttons with LED-Next (the selected table, game start and exit).";
        public string PluginVersion => "2.0";

        public Plugin() { }

        public bool Initialize(IntPtr InfoPtr)
        {
            try
            {
                var info = InfoPtr == IntPtr.Zero ? default : (PinballXInfo)Marshal.PtrToStructure(InfoPtr, typeof(PinballXInfo));
                _client = new LiteClient();
                _client.Log += Log;
                Log("LED-Next PinballX plugin " + Version + " started (PinballX " + (info.Version ?? "?") + ").");
                _client.Send(new LiteMessage(LiteMessage.Started) { Source = Source });
                return true;
            }
            catch (Exception ex)
            {
                Log("Initialize failed: " + ex);
                return false;
            }
        }

        public void Configure()
        {
            try
            {
                System.Windows.Forms.MessageBox.Show(
                    "LED-Next lights the buttons for the table PinballX selects and plays.\n\n" +
                    "Nothing to set here: start LED-Next (it can run before or after PinballX). Its log shows each table; " +
                    "this plugin's own log is \"LED-Next PinballX Plugin.log\" next to the plugin.\n\n" +
                    "A table gets the lights of its system's platform in LED-Next (Visual Pinball, Future Pinball…), " +
                    "or its own when you set them in LED-Next's Games tab.",
                    "LED-Next", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                Log("Configure failed: " + ex.Message);
            }
        }

        public void Event_App_Exit()
        {
            try
            {
                _client?.Send(new LiteMessage(LiteMessage.Closing) { Source = Source });
                _client?.Dispose();
                _client = null;
            }
            catch (Exception ex)
            {
                Log("App_Exit failed: " + ex.Message);
            }
        }

        public void Event_GameSelect(IntPtr InfoPtr) => SendGame(LiteMessage.Select, InfoPtr);

        public bool Event_GameRun(IntPtr InfoPtr)
        {
            SendGame(LiteMessage.Launch, InfoPtr);
            return true; // PinballX launches the table
        }

        public string Event_Parameters(IntPtr InfoPtr)
        {
            try
            {
                return InfoPtr == IntPtr.Zero ? "" : ((GameInfo)Marshal.PtrToStructure(InfoPtr, typeof(GameInfo))).Parameters ?? "";
            }
            catch
            {
                return ""; // empty: PinballX keeps its command line
            }
        }

        public void Event_GameExit(IntPtr InfoPtr) => SendGame(LiteMessage.Exit, InfoPtr);

        public bool Event_Input(bool[] Input_Keys, bool[] Input_Buttons, int PinballXStatus) => true;

        public bool Event_ScreenSaver(int Type)
        {
            try
            {
                if (Type == 1) _client?.Send(new LiteMessage(LiteMessage.ScreensaverStart) { Source = Source });
                else if (Type == 2) _client?.Send(new LiteMessage(LiteMessage.ScreensaverStop) { Source = Source });
            }
            catch (Exception ex)
            {
                Log("ScreenSaver failed: " + ex.Message);
            }
            return true; // PinballX runs its screensaver as usual
        }

        public bool Event_DisableDMD() => false;

        public void Dispose()
        {
            try { _client?.Dispose(); } catch { }
            _client = null;
        }

        /// <summary>The table as a LED-Next game: its table file (as the rom), description and system.</summary>
        public static LiteMessage ToMessage(string ev, GameInfo g)
        {
            string First(params string[] values)
            {
                foreach (var v in values) if (!string.IsNullOrWhiteSpace(v)) return v.Trim();
                return null;
            }
            var table = First(g.TableFile, g.GameName, g.PinMAMEROM, g.ROM);
            return new LiteMessage(ev)
            {
                Source = Source,
                Title = First(g.GameDescription, g.GameShortDescription, g.GameName, table),
                ApplicationPath = table,
                Platform = First(g.SystemName, g.System),
            };
        }

        private void SendGame(string ev, IntPtr infoPtr)
        {
            try
            {
                if (_client == null) return;
                if (infoPtr == IntPtr.Zero)
                {
                    if (ev != LiteMessage.Select) _client.Send(new LiteMessage(ev) { Source = Source });
                    return;
                }
                var g = (GameInfo)Marshal.PtrToStructure(infoPtr, typeof(GameInfo));
                var m = ToMessage(ev, g);
                if (ev == LiteMessage.Launch) Log("Playing " + m.Title + " (" + m.Platform + ", " + m.ApplicationPath + ").");
                _client.Send(m);
            }
            catch (Exception ex)
            {
                Log(ev + " failed: " + ex.Message);
            }
        }

        private static readonly object LogLock = new object();

        private static void Log(string text)
        {
            try
            {
                var dir = Path.GetDirectoryName(typeof(Plugin).Assembly.Location) ?? ".";
                var path = Path.Combine(dir, "LED-Next PinballX Plugin.log");
                lock (LogLock)
                {
                    if (File.Exists(path) && new FileInfo(path).Length > 256 * 1024) File.Delete(path);
                    File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + text + Environment.NewLine,
                        new UTF8Encoding(false));
                }
            }
            catch { }
        }
    }
}
