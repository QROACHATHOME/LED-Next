using LedNext.Core;
using LedNext.Core.Games;
using LedNext.Core.Import;
using LedNext.Core.Lighting;
using LedNext.Core.Outputs;

namespace LedNext.Tests;

public class LampTests
{
    [LedBlinkySamplesFact]
    public void ReadsLedBlinkysMameOutputsIni()
    {
        var path = Path.Combine(LedBlinkyImportTests.RepoRoot(), "import-samples", "ledblinky", "MameOutputs.ini");
        var map = MameOutputMap.Load(path)!;
        // [default] LED0=Start1, LED1=Start2
        Assert.Equal(new[] { "P1_START" }, map.ControlsFor("default", "led0"));
        Assert.Equal(new[] { "P2_START" }, map.ControlsFor("default", "LED1"));
        // [sampleROM]: control names are used; hardware ports, Pixelcade and web requests are skipped.
        Assert.Equal(new[] { "P1_BUTTON1", "P1_BUTTON9" }, map.ControlsFor("sampleROM", "LED0"));
        Assert.Empty(map.ControlsFor("sampleROM", "LAMP1"));
        Assert.True(map.Skipped >= 10);
        Assert.Equal("sampleROM", map.SectionFor(new[] { "sampleromclone", "SAMPLEROM" })); // a clone uses its parent's
        Assert.Equal("default", map.SectionFor(new[] { "galaga" }));
    }

    [Fact]
    public void WithoutAFileTheStartLampsAreMapped()
    {
        var map = MameOutputMap.BuiltIn();
        Assert.Equal(new[] { "P1_START" }, map.ControlsFor("default", "led0"));
        // A file without [default] still gets the start lamps.
        var only = MameOutputMap.Parse(IniFile.Parse(new[] { "[digdug]", "LAMP0=P1_BUTTON1" }));
        Assert.Equal(new[] { "P1_START" }, only.ControlsFor("default", "led0"));
        Assert.Equal(new[] { "P1_BUTTON1" }, only.ControlsFor("digdug", "lamp0"));
    }

    [Fact]
    public void ASessionTurnsOutputsIntoLampChanges()
    {
        var map = MameOutputMap.Parse(IniFile.Parse(new[] { "[default]", "LED0=Start1", "LAMP0=Start1", "LED1=Start2" }));
        var s = new OutputSession(map, "MAME", "galaga", new[] { "galaga" });
        Assert.Equal("default", s.Section);
        Assert.Equal(new[] { ("P1_START", false) }, s.Update("led0", 0));   // first value counts, even off
        Assert.Equal(new[] { ("P1_START", true) }, s.Update("led0", 1));
        Assert.Empty(s.Update("led0", 2));                                   // still on
        Assert.Empty(s.Update("lamp0", 1));                                  // already on through led0
        Assert.Empty(s.Update("led0", 0));                                   // lamp0 keeps it on
        Assert.Equal(new[] { ("P1_START", false) }, s.Update("lamp0", 0));
        Assert.Empty(s.Update("coin_counter0", 1));                          // not mapped: no lamp
        Assert.Equal(3, s.Values.Count);                                     // led0, lamp0, coin_counter0
    }

    private static LampMapEntry Row(string emulator, string game, string output, params string[] controls) =>
        new() { Emulator = emulator, Game = game, Output = output, Controls = controls.ToList() };

    [Fact]
    public void LedNextsOwnTableMapsAnyEmulator()
    {
        var rows = new[]
        {
            Row("Flycast", "SEGA MARINE FISHING JAPAN", "lamp0", "Start1"),
            Row("flycast", "*", "lamp0", "P2_START"),
            Row("Flycast", "*", "lamp1", "P1_BUTTON1", " "),
            Row("MAME", "*", "lamp0", "P1_BUTTON2"),     // another emulator's row: not in Flycast's table
            Row("Flycast", "*", "", "P1_START"),         // no output name
        };
        var table = new LampMapTable("Flycast", rows);
        Assert.Equal(2, table.Skipped);
        var fishing = table.For(new[] { "SEGA MARINE FISHING JAPAN" });
        Assert.Equal(new[] { "P1_START" }, fishing.ControlsFor("LAMP0"));   // the game's row wins
        Assert.Equal(new[] { "P1_BUTTON1" }, fishing.ControlsFor("lamp1")); // "*" fills the rest
        Assert.Equal(new[] { "P2_START" }, table.For(new[] { "other" }).ControlsFor("lamp0"));
        Assert.Empty(new LampMapTable("Supermodel", rows).For(new[] { "x" }).ControlsFor("lamp0"));
        Assert.True(new LampMapTable("Supermodel", rows).IsEmpty);
    }

    [Fact]
    public void ForMameTheOwnTableComesBeforeMameOutputsIni()
    {
        var ini = MameOutputMap.Parse(IniFile.Parse(new[] { "[default]", "LED0=Start1", "LED1=Start2", "[galaga]", "LED0=Start1", "LAMP0=P1_BUTTON1" })); // a game section replaces [default]
        var own = new LampMapTable("MAME", new[] { Row("MAME", "galaga", "led1", "P1_BUTTON3") });
        var map = OutputMaps.Layer(own, ini);
        var galaga = new OutputSession(map, "MAME", "galagao", new[] { "galagao", "galaga" }); // a clone uses its parent's rows
        Assert.Equal(new[] { "P1_BUTTON3" }, galaga.ControlsFor("led1"));  // cabinet.json overrides the ini
        Assert.Equal(new[] { "P1_START" }, galaga.ControlsFor("led0"));    // the rest is the ini's
        Assert.Equal(new[] { "P1_BUTTON1" }, galaga.ControlsFor("lamp0"));
        Assert.Contains("galaga", galaga.Section);
        Assert.Equal("MAME", galaga.Emulator);
    }

    [Fact]
    public void AVersion6ConfigGetsAnEmptyLampTable()
    {
        var config = new CabinetConfig { Version = 6 };
        config.Lamps.Maps = null!;
        ConfigStore.Migrate(config);
        Assert.Equal(CabinetConfig.CurrentVersion, config.Version);
        Assert.Empty(config.Lamps.Maps);
    }

    private static CabinetConfig Panel()
    {
        var c = new CabinetConfig();
        c.Wiring.Boards.Add(new BoardDefinition { Key = "PacLED64#1", Name = "B1" });
        int pin = 1;
        foreach (var id in new[] { "P1_B1", "P1_B2", "P1_START" })
        {
            c.Panel.Buttons.Add(new PanelButton { Id = id, Player = 1 });
            foreach (var ch in new[] { ColorChannel.R, ColorChannel.G, ColorChannel.B }) c.Wiring.Connect(id, ch, "PacLED64#1", pin++);
        }
        return c;
    }

    [Fact]
    public void LampsLightOverTheGameAndStartBlinksButtonOne()
    {
        var config = Panel();
        var red = new Rgb(255, 0, 0);
        var game = new GameLights(new ResolvedLights("MAME", "galaga", "game galaga", Array.Empty<ResolvedControl>(),
            new Dictionary<string, Rgb> { ["P1_B1"] = red, ["P1_B2"] = Rgb.Blue }), "galaga");
        var now = TimeSpan.Zero;
        var overlay = new LampOverlay(game, c => c switch
        {
            "P1_START" => new[] { "P1_START" },
            "P1_BUTTON1" => new[] { "P1_B1" },
            _ => Array.Empty<string>(),
        }, Rgb.White, startLampsOnButton1: true) { Clock = () => now };
        using var engine = new LedEngine(config) { Source = overlay };
        Rgb Show(string id) => engine.RenderFrame(TimeSpan.Zero).ButtonColors[id];

        // No lamp yet: the game's lights.
        Assert.Equal(red, Show("P1_B1"));
        Assert.Equal(Rgb.Black, Show("P1_START"));

        // The start lamp blinks: Start follows it (white: the game leaves Start unlit), and so does button 1.
        overlay.SetLamp("P1_START", true);
        Assert.Equal(Rgb.White, Show("P1_START"));
        Assert.Equal(red, Show("P1_B1"));
        now += TimeSpan.FromSeconds(0.5);
        overlay.SetLamp("P1_START", false);
        Assert.Equal(Rgb.Black, Show("P1_START"));
        Assert.Equal(Rgb.Black, Show("P1_B1"));
        Assert.Equal(Rgb.Blue, Show("P1_B2"));       // other buttons untouched

        // The lamp settles (game started): button 1 shows the game's colour again, Start stays dark.
        now += LampOverlay.BlinkWindow + TimeSpan.FromSeconds(0.1);
        Assert.Equal(red, Show("P1_B1"));
        Assert.Equal(Rgb.Black, Show("P1_START"));
    }

    [Fact]
    public void WithoutTheButtonOneOptionOnlyStartFollows()
    {
        var config = Panel();
        var game = new GameLights(new ResolvedLights("MAME", "x", "x", Array.Empty<ResolvedControl>(),
            new Dictionary<string, Rgb> { ["P1_B1"] = Rgb.Green }), "x");
        var overlay = new LampOverlay(game, c => c == "P1_START" ? new[] { "P1_START" } : new[] { "P1_B1" }, Rgb.White, startLampsOnButton1: false);
        using var engine = new LedEngine(config) { Source = overlay };
        overlay.SetLamp("P1_START", false);
        var frame = engine.RenderFrame(TimeSpan.Zero);
        Assert.Equal(Rgb.Green, frame.ButtonColors["P1_B1"]);
        Assert.Equal(Rgb.Black, frame.ButtonColors["P1_START"]);
        Assert.Equal(0, overlay.LitLamps);
    }

    [Fact]
    public void ReadsMamesNetworkLines()
    {
        var p = new OutputLineParser();
        Assert.Null(p.Feed("led0 = 1"));                                    // before the start: dropped
        Assert.Equal(new OutputLine.Start("galaga", "MAME"), p.Feed("mame_start = galaga"));
        Assert.Equal(new OutputLine.Value("led0", 1), p.Feed("led0 = 1"));
        Assert.Equal(new OutputLine.Value("lamp10", 10), p.Feed("lamp10 = 10")); // decimal
        Assert.Equal(new OutputLine.Pause(true), p.Feed("pause = 1"));
        Assert.Equal(new OutputLine.Pause(false), p.Feed("pause = 0"));
        Assert.Null(p.Feed("garbage"));
        Assert.IsType<OutputLine.Stop>(p.Feed("mame_stop = 1"));
        Assert.Null(p.Game);
        Assert.Null(p.Feed("led0 = 0"));
    }

    [Fact]
    public void ReadsFlycastsNetworkLines()
    {
        // Flycast (core/network/output.h): "game = id" before the first value, hexadecimal values, no stop message.
        var p = new OutputLineParser();
        Assert.Equal(new OutputLine.Start("SEGA MARINE FISHING JAPAN", "Flycast"), p.Feed("game = SEGA MARINE FISHING JAPAN"));
        Assert.Equal(new OutputLine.Value("lamp0", 1), p.Feed("lamp0 = 1"));
        Assert.Equal(new OutputLine.Value("lamp0", 16), p.Feed("lamp0 = 10"));        // hex
        Assert.Equal(new OutputLine.Value("lamp1", -1), p.Feed("lamp1 = ffffffff"));  // on
        Assert.Null(p.Feed("game = SEGA MARINE FISHING JAPAN"));                      // the same game again: no new start
        Assert.Equal("SEGA MARINE FISHING JAPAN", p.Game);
    }
}
