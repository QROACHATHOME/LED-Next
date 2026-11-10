using LedNext.Core;
using LedNext.Core.Games;
using LedNext.Core.Import;
using LedNext.Core.Import.LedBlinky;
using Xunit.Abstractions;

namespace LedNext.Tests;

public class ControlNameTests
{
    [Theory]
    [InlineData("P1_BUTTON1", "P1_BUTTON1", true)]
    [InlineData("START2", "P2_START", true)]
    [InlineData("COIN1", "P1_COIN", true)]
    [InlineData("P1_COIN", "P1_COIN", true)]
    [InlineData("P1_JOYSTICK_UP", "P1_JOYSTICK", false)]
    [InlineData("P2_JOYSTICKLEFT_DOWN", "P2_JOYSTICK", false)]
    [InlineData("P1_TRACKBALL_X_EXT", "P1_TRACKBALL", false)]
    [InlineData("P1_DIAL_EXT", "P1_DIAL", false)]
    [InlineData("P1_AD_STICK_X", "P1_STICK", false)]
    [InlineData("P1_PEDAL2_EXT", "P1_PEDAL2", false)]
    [InlineData("UI_SELECT", "UI_SELECT", true)]
    [InlineData("ui_cancel", "UI_CANCEL", true)]
    public void Classify(string raw, string expected, bool digital)
    {
        Assert.Equal((expected, digital), ControlNames.Classify(raw));
    }

    [Fact]
    public void ControlDescriptorsAreNotLitControls()
    {
        Assert.Null(ControlNames.Normalize("CONTROL_JOY8WAY"));
        Assert.Null(ControlNames.Normalize(""));
    }
}

public class LedBlinkyColorTests
{
    [Fact]
    public void NamesAndIntensitiesScaleTo255()
    {
        var c = new LedBlinkyColors();
        Assert.True(c.TryParse("red", out var red));
        Assert.Equal(new Rgb(255, 0, 0), red);
        Assert.True(c.TryParse("Orange", out var orange));
        Assert.Equal(new Rgb(255, 128, 0), orange);
        Assert.True(c.TryParse("48,24,0", out var triple));
        Assert.Equal(orange, triple);
        Assert.True(c.TryParse("48,48,48,48", out var white));
        Assert.Equal(Rgb.White, white);
    }

    [Fact]
    public void EmptyIsUnspecifiedAndUnknownFails()
    {
        var c = new LedBlinkyColors();
        Assert.True(c.TryParse("", out var none));
        Assert.Null(none);
        Assert.False(c.TryParse("Whie", out _));
    }
}

public class ResolverTests
{
    private static readonly Rgb Red = new(255, 0, 0), Blue = new(0, 0, 255), Grey = new(10, 10, 10);

    private static PanelLayout Panel()
    {
        var p = new PanelLayout();
        foreach (var id in new[] { "P1_B1", "P1_B2", "P1_B3", "P1_START", "P2_B1", "P3_B3" })
            p.Buttons.Add(new PanelButton { Id = id, Player = id[1] - '0' });
        p.Buttons.Add(new PanelButton { Id = "TRACKBALL", Kind = ControlKind.Trackball });
        return p;
    }

    private static InMemoryGameLibrary Library()
    {
        var lib = new InMemoryGameLibrary();
        lib.AddPlatform(new PlatformInfo("MAME", "MAME", null, false, Rgb.White));
        lib.AddPlatform(new PlatformInfo("NeoGeo", "Neo Geo", "MAME", true, Rgb.White));
        lib.SetLights("MAME", null, new LightProfile
        {
            Controls =
            {
                new ControlLight("P1_BUTTON1", Grey, "dim", "default b1"),
                new ControlLight("P1_START", Blue, "Blue", null),
                new ControlLight("UI_SELECT", Rgb.Green, "Green", null, AlwaysActive: true),
            },
        });
        lib.AddGame(new GameInfo("MAME", "game", "Game", null, 2, "", "", GameLighting.Own));
        lib.SetLights("MAME", "game", new LightProfile
        {
            Controls =
            {
                new ControlLight("P1_BUTTON1", Red, "Red", "Fire"),
                new ControlLight("P1_START", null, null, null),
                new ControlLight("P1_BUTTON9", Red, "Red", null), // not on this panel
                new ControlLight("P1_TRACKBALL", Blue, "Blue", null),
            },
        });
        lib.AddGame(new GameInfo("MAME", "gameclone", "Game (clone)", "game", 2, "", "", GameLighting.Parent));
        return lib;
    }

    [Fact]
    public void GameControlsWinAndUnspecifiedColoursInherit()
    {
        var r = GameLightResolver.Resolve(Library(), "MAME", "game", new ControlMap(), Panel());
        Assert.Equal("game game", r.From);
        Assert.Equal(Red, r.ButtonColors["P1_B1"]);
        Assert.Equal(Blue, r.ButtonColors["P1_START"]);       // colour from the platform default
        Assert.Equal(Blue, r.ButtonColors["TRACKBALL"]);      // P1_TRACKBALL → the trackball by kind
        Assert.False(r.ButtonColors.ContainsKey("P1_B2"));    // not listed → off
        Assert.Empty(r.Controls.Single(c => c.Control == "P1_BUTTON9").Buttons);
    }

    [Fact]
    public void AlwaysActiveUiControlsLightSharedButtonsInEveryGame()
    {
        var panel = Panel();
        panel.Buttons.Add(new PanelButton { Id = "ADMIN_1", Kind = ControlKind.Admin });
        var map = new ControlMap();
        map.Mappings.Add(new ControlMapping { Control = "UI_SELECT", Buttons = { "ADMIN_1" }, Source = "input code KEYCODE_ENTER" });
        var r = GameLightResolver.Resolve(Library(), "MAME", "game", map, panel);
        Assert.Equal(Rgb.Green, r.ButtonColors["ADMIN_1"]);
    }

    [Fact]
    public void AlwaysActiveUiControlsDoNotLightPlayerButtons()
    {
        // The cabinet's P3 button 3 sends Enter, which is also MAME's UI_SELECT. LEDBlinky lit it green
        // in every game; it should follow the game instead.
        var map = new ControlMap();
        map.Mappings.Add(new ControlMapping { Control = "UI_SELECT", Buttons = { "P3_B3" }, Source = "input code KEYCODE_ENTER" });
        var r = GameLightResolver.Resolve(Library(), "MAME", "game", map, Panel());
        Assert.False(r.ButtonColors.ContainsKey("P3_B3"));
        Assert.Empty(r.Controls.Single(c => c.Control == "UI_SELECT").Buttons);
    }

    [Theory]
    [InlineData(null)]          // the platform wheel: the platform's own default
    [InlineData("nolights")]    // a game with no lights of its own: lit by the platform default
    public void AlwaysActiveUiControlsDoNotLightPlayerButtonsFromAPlatformDefault(string? game)
    {
        // Visit 6: P3 button 3 was still green in MAME games without their own lights, because the
        // platform default was the level that decides the lit controls and the rule above was skipped.
        var lib = Library();
        lib.AddGame(new GameInfo("MAME", "nolights", "No lights", null, 2, "", "", GameLighting.PlatformDefault));
        var map = new ControlMap();
        map.Mappings.Add(new ControlMapping { Control = "UI_SELECT", Buttons = { "P3_B3" }, Source = "input code KEYCODE_ENTER" });
        var r = GameLightResolver.Resolve(lib, "MAME", game, map, Panel());
        Assert.Equal("MAME default", r.From);
        Assert.False(r.ButtonColors.ContainsKey("P3_B3"));
        Assert.Equal(Grey, r.ButtonColors["P1_B1"]);   // the default's other controls still light
    }

    [Fact]
    public void CloneUsesParentAndUnknownGameUsesPlatformDefault()
    {
        var lib = Library();
        var clone = GameLightResolver.Resolve(lib, "MAME", "gameclone", new ControlMap(), Panel());
        Assert.Equal("parent game", clone.From);
        Assert.Equal(Red, clone.ButtonColors["P1_B1"]);

        var unknown = GameLightResolver.Resolve(lib, "MAME", "nosuchgame", new ControlMap(), Panel());
        Assert.Equal("MAME default", unknown.From);
        Assert.Equal(Grey, unknown.ButtonColors["P1_B1"]);
    }

    [Fact]
    public void MameBasedPlatformFallsBackToMameGameData()
    {
        var r = GameLightResolver.Resolve(Library(), "NeoGeo", "game", new ControlMap(), Panel());
        Assert.Equal("game game (MAME data)", r.From);
        Assert.Equal(Red, r.ButtonColors["P1_B1"]);
    }

    [Fact]
    public void ExplicitMappingBeatsTheNamingConvention()
    {
        var map = new ControlMap();
        map.Mappings.Add(new ControlMapping { Control = "P1_BUTTON1", Buttons = { "P1_B3" }, Source = "input code KEYCODE_C" });
        var r = GameLightResolver.Resolve(Library(), "MAME", "game", map, Panel());
        Assert.Equal(Red, r.ButtonColors["P1_B3"]);
        Assert.False(r.ButtonColors.ContainsKey("P1_B1"));
        Assert.Equal("input code KEYCODE_C", r.Controls.First().MapSource);
    }
}

public class ControlMapBuilderTests
{
    [Fact]
    public void FollowsInputCodeThroughWiringToTheButton()
    {
        var panel = new PanelLayout();
        panel.Buttons.Add(new PanelButton { Id = "P1_B7", Player = 1 });
        panel.Buttons.Add(new PanelButton { Id = "P1_B8", Player = 1 });
        var wiring = new WiringMap();
        wiring.Boards.Add(new BoardDefinition { Key = "PacLED64#1" });
        wiring.Connect("P1_B8", ColorChannel.R, "PacLED64#1", 16);
        wiring.Connect("P1_B7", ColorChannel.R, "PacLED64#1", 31);

        var ports = new List<InputMapPort>
        {
            new(1, "PACLED64", 16, "P1B8", "R", new[] { "KEYCODE_C" }),
            new(1, "PACLED64", 31, "P1B7", "R", new[] { "KEYCODE_V" }),
        };
        var codes = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["P1_BUTTON7"] = new(StringComparer.OrdinalIgnoreCase) { "KEYCODE_C", "JOYCODE_1_BUTTON7" },
        };
        var notes = new List<string>();
        var map = ControlMapBuilder.Build(ports, codes, new[] { "P1_BUTTON7", "P1_BUTTON8" }, wiring, panel, notes);

        Assert.Equal(new[] { "P1_B8" }, map.Find("P1_BUTTON7")!.Buttons);
        Assert.Null(map.Find("P1_BUTTON8")); // no codes known → name convention at lookup time
        Assert.Equal(new[] { "P1_B8" }, map.Lookup("P1_BUTTON8", panel).Buttons);
        Assert.Contains(notes, n => n.Contains("P1_BUTTON7 lights P1_B8"));
    }

    [Fact]
    public void ReadsMameDefaultCfg()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lednext-default-{Guid.NewGuid():N}.cfg");
        File.WriteAllText(path, """
            <?xml version="1.0"?>
            <mameconfig version="10">
              <system name="default">
                <input>
                  <port type="P3_BUTTON1"><newseq type="standard">KEYCODE_COMMA OR JOYCODE_3_BUTTON1</newseq></port>
                  <port type="START1"><newseq type="standard">KEYCODE_1</newseq></port>
                </input>
              </system>
            </mameconfig>
            """);
        try
        {
            var cfg = LedBlinkyInputMap.LoadMameDefaultCfg(path, new List<string>());
            Assert.Equal(new[] { "KEYCODE_COMMA", "JOYCODE_3_BUTTON1" }, cfg["P3_BUTTON1"]);
            Assert.Equal(new[] { "KEYCODE_1" }, cfg["START1"]);
        }
        finally { File.Delete(path); }
    }
}

public class ConfigMigrationTests
{
    [Fact]
    public void Version1ConfigLoadsAndUpgrades()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lednext-v1-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """{ "version": 1, "panel": { "buttons": [ { "id": "P1_B1" } ] } }""");
        try
        {
            var config = ConfigStore.LoadOrCreate(path);
            Assert.Equal(CabinetConfig.CurrentVersion, config.Version);
            Assert.Empty(config.Games.LaunchBoxPlatformMap);
            Assert.NotNull(config.ControlMap);
            Assert.Empty(config.ControlMap.Mappings);
            Assert.Equal("P1_B1", config.Panel.Buttons.Single().Id);
        }
        finally { File.Delete(path); }
    }
}

/// <summary>Runs the importer on the real LEDBlinky 8.3 files and cabinet config brought back from the arcade machine.</summary>
public class LedBlinkyImportTests(ITestOutputHelper output)
{
    internal static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "LedNext.sln"))) return dir.FullName;
        throw new InvalidOperationException("Repository root not found.");
    }

    // The copied settings.ini names the cabinet's real MAME folder (D:\…\Mame02XX). Pass the MAME folder
    // explicitly so the result is the same on every machine: a folder without cfg/default.cfg gives
    // MAME's built-in keys; import-samples/mame holds the cabinet's real cfg/default.cfg.
    private static readonly Lazy<(LedBlinkyImportResult Result, CabinetConfig Cabinet)> Imported =
        new(() => ImportWith(Path.Combine(RepoRoot(), "import-samples", "no-mame-folder")));

    private static readonly Lazy<(LedBlinkyImportResult Result, CabinetConfig Cabinet)> ImportedWithMameCfg =
        new(() => ImportWith(Path.Combine(RepoRoot(), "import-samples", "mame")));

    private static (LedBlinkyImportResult Result, CabinetConfig Cabinet) ImportWith(string mameFolder)
    {
        var root = RepoRoot();
        var cabinet = ConfigStore.LoadOrCreate(Path.Combine(root, "cabinet", "cabinet.json"));
        var result = LedBlinkyImporter.Import(Path.Combine(root, "import-samples", "ledblinky"), cabinet.Panel, cabinet.Wiring,
            mameFolder: mameFolder);
        return (result, cabinet);
    }

    private ResolvedLights Resolve(string platform, string game)
    {
        var (r, cab) = Imported.Value;
        return GameLightResolver.Resolve(r.Library, platform, game, r.ControlMap, cab.Panel);
    }

    [Fact]
    public void ImportsPlatformsGamesAndClones()
    {
        var r = Imported.Value.Result;
        output.WriteLine(r.Summary);
        foreach (var n in r.Notes.Take(60)) output.WriteLine("note: " + n);
        foreach (var w in r.Warnings) output.WriteLine("warn: " + w);

        Assert.True(r.PlatformCount > 150, $"{r.PlatformCount} platforms");
        Assert.True(r.LitGameCount > 2000, $"{r.LitGameCount} lit games");
        Assert.NotNull(r.Library.GetPlatform("Sega_Genesis"));
        Assert.Equal("MAME", r.Library.GetPlatform("SNK_Neo_Geo_MVS")!.ParentKey);

        var g1942 = r.Library.GetGame("MAME", "1942")!;
        Assert.Equal(GameLighting.Own, g1942.Lighting);
        Assert.Contains("1942", g1942.Description);
        var clone = r.Library.GetGame("MAME", "005a")!;
        Assert.Equal("005", clone.Parent);
        Assert.Equal(GameLighting.Parent, clone.Lighting);
        Assert.Single(r.Warnings, w => w.Contains("Whie"));
    }

    [Fact]
    public void Game1942LightsTheRealPanel()
    {
        var lights = Resolve("MAME", "1942");
        foreach (var c in lights.Controls)
            output.WriteLine($"{c.Control,-14} {c.ColorName,-8} {c.Label,-16} → {string.Join(",", c.Buttons)} ({c.MapSource})");

        Assert.Equal(new Rgb(255, 0, 0), lights.ButtonColors["P1_B1"]); // Colors.ini: P1_BUTTON1=Red
        Assert.Equal(Rgb.White, lights.ButtonColors["P1_B2"]);           // P1_BUTTON2=White
        Assert.Equal(new Rgb(255, 0, 0), lights.ButtonColors["P2_B1"]);
        Assert.False(lights.ButtonColors.ContainsKey("P1_B3"));          // unused button stays off
        Assert.Equal(Rgb.White, lights.ButtonColors["P1_START"]);        // Start/Coin lit by name even with no LEDs yet
        Assert.False(lights.ButtonColors.ContainsKey("P3_B3"));          // UI_SELECT (Enter) no longer lights P3 button 3
    }

    [Fact]
    public void ControlMapFollowsTheCabinetsInputMap()
    {
        var (r, cab) = Imported.Value;
        // MAME's P1_BUTTON7 is KEYCODE_C; on this cabinet KEYCODE_C is the button labelled 8.
        Assert.Equal(new[] { "P1_B8" }, r.ControlMap.Lookup("P1_BUTTON7", cab.Panel).Buttons);
        Assert.Equal(new[] { "P1_B7" }, r.ControlMap.Lookup("P1_BUTTON8", cab.Panel).Buttons);
        Assert.Equal(new[] { "TRACKBALL" }, r.ControlMap.Lookup("P1_TRACKBALL", cab.Panel).Buttons);
    }

    [Fact]
    public void CabinetMameCfgReplacesMameDefaults()
    {
        var (r, cab) = ImportedWithMameCfg.Value;
        Assert.Contains(r.Notes, n => n.StartsWith("Using ") && n.Contains("default.cfg"));
        // The cabinet's default.cfg swaps MAME's defaults: P1_BUTTON7 = KEYCODE_V, P1_BUTTON8 = KEYCODE_C,
        // so MAME's button 7 is the button labelled 7 here.
        Assert.Equal(new[] { "P1_B7" }, r.ControlMap.Lookup("P1_BUTTON7", cab.Panel).Buttons);
        Assert.Equal(new[] { "P1_B8" }, r.ControlMap.Lookup("P1_BUTTON8", cab.Panel).Buttons);
        Assert.Equal(new[] { "TRACKBALL" }, r.ControlMap.Lookup("P1_TRACKBALL", cab.Panel).Buttons);
    }

    [Fact]
    public void TrackballGameLightsTheTrackball()
    {
        var lights = Resolve("MAME", "centiped");
        output.WriteLine(lights.From);
        Assert.True(lights.ButtonColors.ContainsKey("TRACKBALL"));
    }

    [Fact]
    public void DatabaseRoundTripResolvesTheSame()
    {
        var (r, cab) = Imported.Value;
        var path = Path.Combine(Path.GetTempPath(), $"lednext-games-{Guid.NewGuid():N}.db");
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var db = LedNext.Data.GameDatabase.Open(path);
            db.ReplaceAll(r.Library, "import-samples");
            output.WriteLine($"Database written in {sw.ElapsedMilliseconds} ms");
            Assert.Equal(r.GameCount, db.GameCount());

            foreach (var (platform, game) in new[] { ("MAME", "1942"), ("MAME", "005a"), ("MAME", "centiped"), ("MAME", "nosuchgame"), ("SNK_Neo_Geo_MVS", "mslug") })
            {
                var mem = GameLightResolver.Resolve(r.Library, platform, game, r.ControlMap, cab.Panel);
                var fromDb = GameLightResolver.Resolve(db, platform, game, r.ControlMap, cab.Panel);
                Assert.Equal(mem.From, fromDb.From);
                Assert.Equal(mem.ButtonColors.OrderBy(k => k.Key), fromDb.ButtonColors.OrderBy(k => k.Key));
            }

            sw.Restart();
            var (games, total) = db.Search("MAME", "pac", litOnly: true);
            output.WriteLine($"Search 'pac': {total} matches in {sw.ElapsedMilliseconds} ms");
            Assert.Contains(games, g => g.Name == "pacman");
            Assert.Equal("MAME", db.GetPlatformSummaries()[0].Key);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (var f in new[] { path, path + "-wal", path + "-shm" }) if (File.Exists(f)) File.Delete(f);
        }
    }
}
