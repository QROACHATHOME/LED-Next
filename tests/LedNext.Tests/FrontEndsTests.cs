using LedNext.Core;
using LedNext.Core.FrontEnd;
using LedNext.Core.Import.LaunchBox;
using LedNext.FrontEnd;
using LedNext.FrontEnd.Lite;

namespace LedNext.Tests;

/// <summary>LedNextCmd's LEDBlinky / EmulationStation commands, the session's platform fill-in, and front-end setup.</summary>
public class FrontEndsTests
{
    private static FrontEndMessage RoundTrip(LiteMessage m) => FrontEndProtocol.Parse(m.ToJson())!;

    [Fact]
    public void LiteMessagesReadAsFrontEndMessages()
    {
        var m = new LiteMessage(LiteMessage.Select)
        {
            Source = "HyperSpin", Title = "Say \"hi\"\n", Platform = "MAME", ApplicationPath = @"C:\roms\sf2.zip", SentAtMs = 1234,
        };
        var back = RoundTrip(m);
        Assert.Equal(FrontEndEvents.Select, back.Event);
        Assert.Equal("HyperSpin", back.Source);
        Assert.Equal("Say \"hi\"\n", back.Title);
        Assert.Equal(@"C:\roms\sf2.zip", back.ApplicationPath);
        Assert.Equal(1234, back.SentAtMs);
        Assert.Null(back.GameId);
        Assert.DoesNotContain('\n', m.ToJson()); // one line on the pipe
    }

    [Fact]
    public void LiteEventNamesMatchTheProtocol()
    {
        Assert.Equal(FrontEndEvents.Select, LiteMessage.Select);
        Assert.Equal(FrontEndEvents.Launch, LiteMessage.Launch);
        Assert.Equal(FrontEndEvents.Exit, LiteMessage.Exit);
        Assert.Equal(FrontEndEvents.Started, LiteMessage.Started);
        Assert.Equal(FrontEndEvents.Closing, LiteMessage.Closing);
        Assert.Equal(FrontEndEvents.ScreensaverStart, LiteMessage.ScreensaverStart);
        Assert.Equal(FrontEndEvents.ScreensaverStop, LiteMessage.ScreensaverStop);
        Assert.Equal(FrontEndProtocol.PipeName, LitePipe.PipeName);
    }

    [Theory]
    [InlineData(new string[0], "started", null, null)]
    [InlineData(new[] { "1" }, "started", null, null)]
    [InlineData(new[] { "2" }, "closing", null, null)]
    [InlineData(new[] { "4" }, "exit", null, null)]
    [InlineData(new[] { "5" }, "screensaver-start", null, null)]
    [InlineData(new[] { "9", "sf2", "MAME" }, "select", "sf2", "MAME")]
    [InlineData(new[] { "9", "sf2" }, "select", "sf2", null)]
    [InlineData(new[] { "15", "dkong", "MAME" }, "select", "dkong", "MAME")]
    [InlineData(new[] { "sf2", "MAME" }, "launch", "sf2", "MAME")]
    [InlineData(new[] { "\"sf2\"", "\"MAME\"" }, "launch", "sf2", "MAME")]              // quotes left in
    [InlineData(new[] { "3", "Super Mario World", "snes" }, "launch", "Super Mario World", "snes")] // RetroFE's game start
    [InlineData(new[] { "C:\\roms\\sf2.zip", "MAME" }, "launch", "sf2", "MAME")]
    [InlineData(new[] { "9", "Street~Fighter~II", "Sega~Genesis" }, "select", "Street Fighter II", "Sega Genesis")] // tildes
    [InlineData(new[] { "8", "Sega_Genesis" }, "select", null, "Sega_Genesis")]
    [InlineData(new[] { "9", "sf2", "" }, "select", "sf2", null)]                        // an empty emulator
    public void ReadsLedBlinkyCommands(string[] args, string ev, string? title, string? platform)
    {
        var p = LedBlinkyCommand.Parse(args, "Test");
        Assert.NotNull(p.Message);
        Assert.Equal(ev, p.Message!.Event);
        Assert.Equal(title, p.Message.Title);
        Assert.Equal(platform, p.Message.Platform);
        Assert.Equal("Test", p.Message.Source);
    }

    [Theory]
    [InlineData("8")]
    [InlineData("9")]
    [InlineData("11")]
    [InlineData("14", "1,48")]
    [InlineData("16")]
    [InlineData("99")]
    [InlineData("rainbow.lwax", "S")]
    public void SkipsLedBlinkyCommandsWithNothingToLight(params string[] args)
    {
        var p = LedBlinkyCommand.Parse(args, "Test");
        Assert.Null(p.Message);
        Assert.False(string.IsNullOrEmpty(p.Note));
    }

    [Theory]
    [InlineData("sf2", "sf2")]
    [InlineData(@"D:\roms\snes\Super Mario World (USA).sfc", "Super Mario World (USA)")]
    [InlineData("Mr. Do!", "Mr. Do!")]                      // a dot that isn't an extension
    [InlineData("D:/roms/mame/dkong.zip", "dkong")]
    public void RomNames(string path, string rom) => Assert.Equal(rom, LedBlinkyCommand.RomName(path));

    [Fact]
    public void ReadsEsDeEvents()
    {
        var sel = EsEvent.Parse(EsFlavor.EsDe, new[] { "game-select", @"C:\roms\snes\Super Metroid.sfc", "Super Metroid", "snes", "Nintendo SNES" }, null).Message!;
        Assert.Equal(("select", "Super Metroid", "snes", @"C:\roms\snes\Super Metroid.sfc", "ES-DE"),
            (sel.Event, sel.Title, sel.Platform, sel.ApplicationPath, sel.Source));
        var start = EsEvent.Parse(EsFlavor.EsDe, new[] { "game-start", @"C:\roms\mame\sf2.zip", "Street Fighter II", "mame", "MAME" }, null).Message!;
        Assert.Equal(("launch", "mame"), (start.Event, start.Platform));
        Assert.Equal("exit", EsEvent.Parse(EsFlavor.EsDe, new[] { "game-end", "x", "y", "z", "w" }, null).Message!.Event);
        Assert.Equal("started", EsEvent.Parse(EsFlavor.EsDe, new[] { "startup" }, null).Message!.Event);
        Assert.Equal("closing", EsEvent.Parse(EsFlavor.EsDe, new[] { "quit" }, null).Message!.Event);
        var sys = EsEvent.Parse(EsFlavor.EsDe, new[] { "system-select", "genesis", "Sega Genesis" }, null).Message!;
        Assert.Equal(("select", null, "genesis"), (sys.Event, sys.Title, sys.Platform));
        Assert.Null(EsEvent.Parse(EsFlavor.EsDe, new[] { "game-select", "", "", "", "" }, null).Message); // fast scrolling
        Assert.Null(EsEvent.Parse(EsFlavor.EsDe, new[] { "screensaver-game-select", "a", "b", "c", "d" }, null).Message);
        Assert.Null(EsEvent.Parse(EsFlavor.EsDe, new[] { "theme-changed", "a", "b" }, null).Message);
    }

    [Fact]
    public void ReadsRetroBatEvents()
    {
        var sel = EsEvent.Parse(EsFlavor.RetroBat, new[] { "game-selected", "snes", @"D:\RetroBat\roms\snes\Super Metroid.sfc", "Super Metroid" }, null).Message!;
        Assert.Equal(("select", "Super Metroid", "snes", "RetroBat"), (sel.Event, sel.Title, sel.Platform, sel.Source));
        // game-start: rom, rom name, game name; the system comes from the rom's folder.
        var start = EsEvent.Parse(EsFlavor.RetroBat, new[] { "game-start", @"D:\RetroBat\roms\mame\sf2.zip", "sf2", "Street Fighter II" }, null).Message!;
        Assert.Equal(("launch", "Street Fighter II", "mame", @"D:\RetroBat\roms\mame\sf2.zip"), (start.Event, start.Title, start.Platform, start.ApplicationPath));
        var elsewhere = EsEvent.Parse(EsFlavor.RetroBat, new[] { "game-start", @"E:\games\sf2.zip", "sf2", "Street Fighter II" }, null).Message!;
        Assert.Null(elsewhere.Platform); // LED-Next uses the system selected before
        Assert.Equal("select", EsEvent.Parse(EsFlavor.RetroBat, new[] { "system-selected", "mame" }, null).Message!.Event);
        Assert.Equal("started", EsEvent.Parse(EsFlavor.RetroBat, new[] { "start" }, null).Message!.Event);
        Assert.Equal("exit", EsEvent.Parse(EsFlavor.RetroBat, new[] { "game-end" }, null).Message!.Event);
        Assert.Null(EsEvent.Parse(EsFlavor.RetroBat, new[] { "config-changed" }, null).Message);
    }

    [Fact]
    public void AGameWithoutAPlatformTakesTheLastOneElseMame()
    {
        var s = new FrontEndSession();
        var a = (FrontEndAction.LightGame)s.Handle(new FrontEndMessage(FrontEndEvents.Select, "HyperSpin", Title: "sf2"))!;
        Assert.Equal("MAME", a.Game.Platform);
        s.Handle(new FrontEndMessage(FrontEndEvents.Select, "HyperSpin", Platform: "Sega Genesis"));
        Assert.Equal("Sega Genesis", s.LastListPlatform);
        var b = (FrontEndAction.LightGame)s.Handle(new FrontEndMessage(FrontEndEvents.Launch, "HyperSpin", Title: "sonic"))!;
        Assert.Equal("Sega Genesis", b.Game.Platform);
        Assert.Equal("Sega Genesis", s.Playing!.Platform);
    }

    [Fact]
    public void AnOlderSelectionArrivingLateIsIgnored()
    {
        var s = new FrontEndSession();
        Assert.NotNull(s.Handle(new FrontEndMessage(FrontEndEvents.Select, "RetroBat", Title: "b", Platform: "mame", SentAtMs: 2000)));
        Assert.Null(s.Handle(new FrontEndMessage(FrontEndEvents.Select, "RetroBat", Title: "a", Platform: "mame", SentAtMs: 1990)));
        Assert.Equal("b", s.Selected!.Title);
        // Another front end's clock doesn't count against it.
        Assert.NotNull(s.Handle(new FrontEndMessage(FrontEndEvents.Select, "LaunchBox", Title: "c", Platform: "Arcade", SentAtMs: 1500)));
    }

    [Theory]
    [InlineData("snes", "Super_Nintendo_Entertainment_System")]
    [InlineData("megadrive", "Sega_Genesis")]
    [InlineData("arcade", "MAME")]
    [InlineData("Visual Pinball", "Visual_Pinball")] // PinballX's system name, by its own name
    [InlineData("vpx", "Visual_Pinball")]
    public void EmulationStationAndPinballSystemNamesMatchPlatforms(string name, string key)
    {
        var keys = new[] { "MAME", "Sega_Genesis", "Super_Nintendo_Entertainment_System", "Visual_Pinball" };
        Assert.Equal(key, PlatformMatcher.Match(name, keys, null).Key);
    }

    private static string TempDir()
    {
        var d = Path.Combine(Path.GetTempPath(), "lednext-fe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    [Fact]
    public void InstallsAndRemovesEsDeScripts()
    {
        var esde = TempDir();
        try
        {
            Directory.CreateDirectory(Path.Combine(esde, "settings"));
            Assert.Equal(esde, FrontEndSetup.FindEsDeData(esde));
            var r = FrontEndSetup.InstallEsDe(esde, @"D:\LED-Next\LedNextCmd.exe");
            Assert.True(r.Ok, r.Message);
            Assert.True(FrontEndSetup.EsDeInstalled(esde));
            var bat = File.ReadAllText(Path.Combine(esde, "scripts", "game-select", "LED-Next.bat"));
            Assert.Contains("\"D:\\LED-Next\\LedNextCmd.exe\" --es-de game-select %*", bat);
            Assert.StartsWith("@echo off", bat); // no BOM in front of it

            // Someone else's script with our name is never overwritten or removed.
            var theirs = Path.Combine(esde, "scripts", "game-start", "LED-Next.bat");
            File.WriteAllText(theirs, "@echo my own");
            Assert.False(FrontEndSetup.InstallEsDe(esde, "x.exe").Ok);
            FrontEndSetup.RemoveEsDe(esde);
            Assert.Equal("@echo my own", File.ReadAllText(theirs));
            Assert.False(File.Exists(Path.Combine(esde, "scripts", "game-select", "LED-Next.bat")));
        }
        finally { Directory.Delete(esde, true); }
    }

    [Fact]
    public void InstallsTheRetroBatScriptAndAttractModePlugin()
    {
        var root = TempDir();
        try
        {
            var rb = Path.Combine(root, "RetroBat");
            Directory.CreateDirectory(Path.Combine(rb, "emulationstation", ".emulationstation"));
            Assert.Equal(rb, FrontEndSetup.FindRetroBat(rb));
            Assert.True(FrontEndSetup.InstallRetroBat(rb, @"D:\LED-Next\LedNextCmd.exe").Ok);
            Assert.Contains("--retrobat %*", File.ReadAllText(FrontEndSetup.RetroBatScriptPath(rb)));
            Assert.True(FrontEndSetup.RetroBatInstalled(rb));
            FrontEndSetup.RemoveRetroBat(rb);
            Assert.False(FrontEndSetup.RetroBatInstalled(rb));

            var am = Path.Combine(root, "Attract");
            Directory.CreateDirectory(am);
            Assert.Null(FrontEndSetup.FindAttractMode(am));
            File.WriteAllText(Path.Combine(am, "attract.cfg"), "");
            Assert.True(FrontEndSetup.InstallAttractMode(am, @"D:\LED-Next\LedNextCmd.exe").Ok);
            var nut = File.ReadAllText(FrontEndSetup.AttractPluginPath(am));
            Assert.Contains("command = \"D:\\\\LED-Next\\\\LedNextCmd.exe\";", nut); // Squirrel string escapes
            Assert.Contains("fe.add_transition_callback", nut);
            FrontEndSetup.RemoveAttractMode(am);
            Assert.False(Directory.Exists(Path.Combine(am, "plugins", "LED-Next")));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void MakesTheLedBlinkyStandIn()
    {
        var app = TempDir();
        try
        {
            Assert.False(FrontEndSetup.EnsureStandIn(app).Ok); // no LedNextCmd.exe yet
            File.WriteAllText(FrontEndSetup.ToolPath(app), "tool");
            var r = FrontEndSetup.EnsureStandIn(app);
            Assert.True(r.Ok, r.Message);
            Assert.Equal("tool", File.ReadAllText(FrontEndSetup.StandInExe(app)));
            Assert.Contains("up to date", FrontEndSetup.EnsureStandIn(app).Message);
        }
        finally { Directory.Delete(app, true); }
    }

    [Fact]
    public void Migrate_From14_AddsTheFrontEndFolders()
    {
        var c = new CabinetConfig { Version = 14, FrontEnds = null! };
        ConfigStore.Migrate(c);
        Assert.Equal(CabinetConfig.CurrentVersion, c.Version);
        Assert.NotNull(c.FrontEnds);
        Assert.Null(c.FrontEnds.EsDe);
    }
}
