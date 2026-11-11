using LedNext.Core;
using LedNext.Core.Games;
using LedNext.Core.Import.LaunchBox;
using LedNext.Core.Import.Mame;
using LedNext.Data;
using Microsoft.Data.Sqlite;

namespace LedNext.Tests;

/// <summary>
/// A MAME install with its own keys (the cabinet's "Mame02XX Race": P1_BUTTON1 on Left Shift = panel button 4)
/// lights the buttons the game really uses there.
/// </summary>
public class MameKeyLayoutTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"lednext-mamekeys-{Guid.NewGuid():N}");
    private static readonly Rgb Red = new(255, 0, 0), Blue = new(0, 0, 255);

    public MameKeyLayoutTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private static PanelLayout Panel()
    {
        var p = new PanelLayout();
        foreach (var id in new[] { "P1_B1", "P1_B2", "P1_B3", "P1_B4", "P1_B5", "P1_START" })
            p.Buttons.Add(new PanelButton { Id = id, Player = 1 });
        return p;
    }

    /// <summary>The keys each panel button sends (LEDBlinky's input map); start sends nothing listed there.</summary>
    private static MameKeyLayouts Layouts() => new(new Dictionary<string, HashSet<string>>
    {
        ["P1_B1"] = new() { "KEYCODE_LCONTROL" },
        ["P1_B2"] = new() { "KEYCODE_LALT" },
        ["P1_B3"] = new() { "KEYCODE_SPACE" },
        ["P1_B4"] = new() { "KEYCODE_LSHIFT" },
        ["P1_B5"] = new() { "KEYCODE_Z" },
    });

    private static string Port(string type, string seq) =>
        $"""<port type="{type}"><newseq type="standard">{seq}</newseq></port>""";

    private static string Cfg(string system, params string[] ports) =>
        $"""<?xml version="1.0"?><mameconfig version="10"><system name="{system}"><input>{string.Join("", ports)}</input></system></mameconfig>""";

    /// <summary>A racing-style install: ctrlr file + default.cfg + one game's own cfg.</summary>
    private string RaceInstall()
    {
        var folder = Path.Combine(_root, "Mame02XX Race");
        Directory.CreateDirectory(Path.Combine(folder, "cfg"));
        Directory.CreateDirectory(Path.Combine(folder, "ctrlr"));
        File.WriteAllText(Path.Combine(folder, "mame.exe"), "");
        File.WriteAllText(Path.Combine(folder, "mame.ini"), "ctrlrpath                 ctrlr\nctrlr                     racing\ncfg_directory             cfg\n");
        File.WriteAllText(Path.Combine(folder, "ctrlr", "racing.cfg"), Cfg("default",
            Port("P1_BUTTON4", "KEYCODE_Z"),
            Port("P1_BUTTON1", "KEYCODE_LCONTROL"))); // default.cfg below wins over this
        File.WriteAllText(Path.Combine(folder, "cfg", "default.cfg"), Cfg("default",
            Port("P1_BUTTON1", "KEYCODE_LSHIFT"),
            Port("P1_BUTTON2", "KEYCODE_LALT OR JOYCODE_1_BUTTON2"), // same button as the cabinet's map
            Port("P1_BUTTON5", "KEYCODE_5"),                         // the coin key: no panel button
            Port("START1", "KEYCODE_1"),                             // not in the input map: left alone
            Port("UI_SELECT", "KEYCODE_LSHIFT")));                   // UI keys are never moved
        File.WriteAllText(Path.Combine(folder, "cfg", "outrun.cfg"),
            """
            <?xml version="1.0"?>
            <mameconfig version="10"><system name="outrun"><input>
              <port tag=":IN0" type="P1_BUTTON1" mask="16" defvalue="16"><newseq type="standard">KEYCODE_SPACE</newseq></port>
            </input></system></mameconfig>
            """);
        return folder;
    }

    [Fact]
    public void AnInstallsOwnKeysMoveTheButtonsAGameLights()
    {
        var folder = RaceInstall();
        var layout = Layouts().For(new ControlMap(), Panel(), folder, rom: null);
        var panel = Panel();

        Assert.Equal(new[] { "P1_B4" }, layout.Map.Lookup("P1_BUTTON1", panel).Buttons); // default.cfg over the ctrlr file
        Assert.Equal(new[] { "P1_B5" }, layout.Map.Lookup("P1_BUTTON4", panel).Buttons); // from the ctrlr file
        Assert.Empty(layout.Map.Lookup("P1_BUTTON5", panel).Buttons);                     // coin key: not lit, not P1_B5 by name
        Assert.Equal(new[] { "P1_B2" }, layout.Map.Lookup("P1_BUTTON2", panel).Buttons);
        Assert.Equal(new[] { "P1_START" }, layout.Map.Lookup("P1_START", panel).Buttons);
        Assert.Null(layout.Map.Find("UI_SELECT"));
        Assert.Contains("Mame02XX Race", layout.Note);
        Assert.Contains("P1_BUTTON1 → P1_B4", layout.Note);

        // A game lit with that map: button 1 red is panel button 4; button 5 is dark.
        var lib = new InMemoryGameLibrary();
        lib.AddPlatform(new PlatformInfo("MAME", "MAME", null, false, Rgb.White));
        lib.AddGame(new GameInfo("MAME", "game", "Game", null, 1, "", "", GameLighting.Own));
        lib.SetLights("MAME", "game", new LightProfile
        {
            Controls = { new ControlLight("P1_BUTTON1", Red, "Red", null), new ControlLight("P1_BUTTON5", Blue, "Blue", null) },
        });
        var lights = GameLightResolver.Resolve(lib, "MAME", "game", layout.Map, panel);
        Assert.Equal(Red, lights.ButtonColors["P1_B4"]);
        Assert.False(lights.ButtonColors.ContainsKey("P1_B1"));
        Assert.False(lights.ButtonColors.ContainsKey("P1_B5"));
    }

    [Fact]
    public void AGamesOwnCfgWinsOverTheInstallsDefaults()
    {
        var folder = RaceInstall();
        var layout = Layouts().For(new ControlMap(), Panel(), folder, rom: "outrun");
        Assert.Equal(new[] { "P1_B3" }, layout.Map.Lookup("P1_BUTTON1", Panel()).Buttons);
        Assert.Contains("outrun.cfg", layout.Note);
    }

    [Fact]
    public void AnInstallThatMatchesTheCabinetChangesNothing()
    {
        var folder = Path.Combine(_root, "Mame02XX");
        Directory.CreateDirectory(Path.Combine(folder, "cfg"));
        File.WriteAllText(Path.Combine(folder, "cfg", "default.cfg"), Cfg("default",
            Port("P1_BUTTON1", "KEYCODE_LCONTROL"), Port("P1_BUTTON4", "KEYCODE_LSHIFT"), Port("START1", "KEYCODE_1")));
        var cabinet = new ControlMap();
        var layout = Layouts().For(cabinet, Panel(), folder, rom: "sf2");
        Assert.Same(cabinet, layout.Map);
        Assert.Null(layout.Note);
        Assert.Equal(folder, MameKeyLayouts.MameFolderOf(Path.Combine(folder, "mame.exe")));
        Assert.Null(MameKeyLayouts.MameFolderOf(Path.Combine(_root, "elsewhere", "teknoparrotui.exe")));
    }

    [Fact]
    public void ACfgFileIsReadAgainWhenItChanges()
    {
        var folder = RaceInstall();
        var layouts = Layouts();
        Assert.Equal(new[] { "P1_B4" }, layouts.For(new ControlMap(), Panel(), folder, null).Map.Lookup("P1_BUTTON1", Panel()).Buttons);
        var cfg = Path.Combine(folder, "cfg", "default.cfg");
        File.WriteAllText(cfg, Cfg("default", Port("P1_BUTTON1", "KEYCODE_Z")));
        File.SetLastWriteTimeUtc(cfg, DateTime.UtcNow.AddMinutes(1));
        Assert.Equal(new[] { "P1_B5" }, layouts.For(new ControlMap(), Panel(), folder, null).Map.Lookup("P1_BUTTON1", Panel()).Buttons);
    }

    [Theory]
    [InlineData("KEYCODE_A", "KEYCODE_A")]
    [InlineData("KEYCODE_A OR JOYCODE_1_BUTTON1", "KEYCODE_A,JOYCODE_1_BUTTON1")]
    [InlineData("KEYCODE_A NOT KEYCODE_LSHIFT", "KEYCODE_A")]
    [InlineData("NONE", "")]
    public void SequenceCodes(string sequence, string expected) =>
        Assert.Equal(expected, string.Join(",", MameKeyLayouts.Codes(sequence)));

    [Fact]
    public void LaunchBoxTellsWhichEmulatorRunsEachGame()
    {
        var lb = Path.Combine(_root, "LaunchBox");
        Directory.CreateDirectory(Path.Combine(lb, "Data", "Platforms"));
        File.WriteAllText(Path.Combine(lb, "Data", "Emulators.xml"), """
            <?xml version="1.0" standalone="yes"?>
            <LaunchBox>
              <Emulator><ID>cur</ID><ApplicationPath>..\Emu\Mame02XX\mame.exe</ApplicationPath><Title>MAME (Current)</Title></Emulator>
              <Emulator><ID>race</ID><ApplicationPath>..\Emu\Mame02XX Race\mame.exe</ApplicationPath><Title>MAME Race</Title></Emulator>
              <EmulatorPlatform><Emulator>cur</Emulator><Platform>Arcade</Platform><Default>true</Default></EmulatorPlatform>
              <EmulatorPlatform><Emulator>race</Emulator><Platform>Arcade</Platform><Default>false</Default></EmulatorPlatform>
            </LaunchBox>
            """);
        File.WriteAllText(Path.Combine(lb, "Data", "Platforms", "Arcade.xml"), """
            <?xml version="1.0" standalone="yes"?>
            <LaunchBox>
              <Game><ID>g1</ID><ApplicationPath>..\roms\sf2.zip</ApplicationPath><Platform>Arcade</Platform><Title>Street Fighter II</Title></Game>
              <Game><ID>g2</ID><ApplicationPath>..\roms\outrun.zip</ApplicationPath><Platform>Arcade</Platform><Title>Out Run</Title><Emulator>race</Emulator></Game>
            </LaunchBox>
            """);
        var games = LaunchBoxLibrary.Read(lb, new List<string>());
        var emu = Path.GetFullPath(Path.Combine(_root, "Emu"));
        Assert.Equal(Path.Combine(emu, "Mame02XX", "mame.exe"), games.Single(g => g.Id == "g1").EmulatorPath);
        Assert.Equal(Path.Combine(emu, "Mame02XX Race", "mame.exe"), games.Single(g => g.Id == "g2").EmulatorPath);

        var lib = new InMemoryGameLibrary();
        lib.AddPlatform(new PlatformInfo("MAME", "MAME", null, false, Rgb.White));
        foreach (var rom in new[] { "sf2", "outrun" })
            lib.AddGame(new GameInfo("MAME", rom, rom, null, 2, "", "Colors.ini", GameLighting.Own));
        var db = GameDatabase.Open(Path.Combine(_root, "games.db"));
        db.ReplaceAll(lib, "test");
        db.ImportLaunchBox(games, new Dictionary<string, string>(), lb);

        Assert.Equal((Path.Combine(emu, "Mame02XX Race", "mame.exe"), "outrun"), db.LaunchBoxEmulator("g2", "MAME", null));
        Assert.Equal((Path.Combine(emu, "Mame02XX Race", "mame.exe"), "outrun"), db.LaunchBoxEmulator(null, "MAME", "outrun"));
        Assert.Equal((Path.Combine(emu, "Mame02XX", "mame.exe"), "sf2"), db.LaunchBoxEmulator("unknown", "MAME", "sf2"));
        Assert.Null(db.LaunchBoxEmulator(null, "MAME", "notinlaunchbox"));
    }
}
