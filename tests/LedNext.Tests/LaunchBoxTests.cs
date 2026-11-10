using System.Globalization;
using LedNext.Core;
using LedNext.Core.Games;
using LedNext.Core.Import.LaunchBox;
using LedNext.Data;
using Microsoft.Data.Sqlite;

namespace LedNext.Tests;

public class LaunchBoxReaderTests
{
    [Theory]
    [InlineData(@"..\..\Games\MAME\sf2.zip", "sf2")]
    [InlineData(@"D:\roms\genesis\Sonic The Hedgehog (USA).md", "Sonic The Hedgehog (USA)")]
    [InlineData("roms/nes/mario.nes", "mario")]
    [InlineData(@"D:\Games\Some Game\", "Some Game")]
    [InlineData("", "")]
    public void RomName(string path, string expected) => Assert.Equal(expected, LaunchBoxLibrary.RomName(path));

    [Fact]
    public void PlatformMatching()
    {
        var keys = new[] { "MAME", "Sega_Genesis", "SNK_Neo_Geo_MVS" };
        Assert.Equal(("MAME", "alias"), PlatformMatcher.Match("Arcade", keys, null));
        Assert.Equal(("Sega_Genesis", "same name"), PlatformMatcher.Match("Sega Genesis", keys, null));
        Assert.Equal(("Microsoft_Xbox", "new"), PlatformMatcher.Match("Microsoft Xbox", keys, null));
        var overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["sega genesis"] = "MAME" };
        Assert.Equal(("MAME", "your mapping"), PlatformMatcher.Match("Sega Genesis", keys, overrides));
    }
}

public class LaunchBoxDatabaseTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"lednext-lb-{Guid.NewGuid():N}");
    private readonly string _dbPath;

    public LaunchBoxDatabaseTests()
    {
        _dbPath = Path.Combine(_root, "games.db");
        var platforms = Path.Combine(_root, "LaunchBox", "Data", "Platforms");
        Directory.CreateDirectory(platforms);
        string Ago(int days) => DateTimeOffset.Now.AddDays(-days).ToString("o", CultureInfo.InvariantCulture);
        string Game(string title, string platform, string path, int daysAgo, string genre = "") => $"""
              <Game>
                <ApplicationPath>{path}</ApplicationPath>
                <DateAdded>{Ago(daysAgo)}</DateAdded>
                <ID>{Guid.NewGuid()}</ID>
                <Platform>{platform}</Platform>
                <Title>{title}</Title>
                <Genre>{genre}</Genre>
              </Game>
            """;
        File.WriteAllText(Path.Combine(platforms, "Arcade.xml"), $"""
            <?xml version="1.0" standalone="yes"?>
            <LaunchBox>
            {Game("Street Fighter II", "Arcade", @"..\..\Games\MAME\sf2.zip", 3, "Fighting")}
            {Game("1942", "Arcade", @"..\..\Games\MAME\1942.zip", 100)}
            {Game("Brand New Game", "Arcade", @"..\..\Games\MAME\zzznew.zip", 1)}
              <AdditionalApplication><Name>ignored</Name></AdditionalApplication>
            </LaunchBox>
            """);
        File.WriteAllText(Path.Combine(platforms, "Sega Genesis.xml"), $"""
            <?xml version="1.0" standalone="yes"?>
            <LaunchBox>
            {Game("Sonic the Hedgehog", "Sega Genesis", @"D:\roms\genesis\Sonic the Hedgehog (USA).md", 2)}
            {Game("Streets of Rage 2", "Sega Genesis", @"D:\roms\genesis\sor2.md", 400)}
            </LaunchBox>
            """);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private static InMemoryGameLibrary Library()
    {
        var lib = new InMemoryGameLibrary();
        lib.AddPlatform(new PlatformInfo("MAME", "MAME", null, false, Rgb.White));
        lib.AddPlatform(new PlatformInfo("Sega_Genesis", "Sega Genesis", "MAME", false, Rgb.White));
        lib.SetLights("MAME", null, new LightProfile { Controls = { new ControlLight("P1_BUTTON1", Rgb.White, "White", null) } });
        foreach (var rom in new[] { "sf2", "1942", "ddonpach" })
        {
            lib.AddGame(new GameInfo("MAME", rom, rom, null, 2, "", "Colors.ini", GameLighting.Own,
                Buttons: rom == "ddonpach" ? 6 : 0, Tags: rom == "ddonpach" ? "joystick" : ""));
            lib.SetLights("MAME", rom, new LightProfile { Controls = { new ControlLight("P1_BUTTON1", Rgb.Red, "Red", null) } });
        }
        lib.AddGame(new GameInfo("Sega_Genesis", "sor2", "Streets of Rage 2", null, 2, "", "", GameLighting.PlatformDefault));
        return lib;
    }

    private GameDatabase Imported(IReadOnlyDictionary<string, string>? overrides = null, int expectedGames = 5)
    {
        var db = GameDatabase.Open(_dbPath);
        db.ReplaceAll(Library(), "test");
        var warnings = new List<string>();
        var games = LaunchBoxLibrary.Read(Path.Combine(_root, "LaunchBox"), warnings);
        Assert.Empty(warnings);
        Assert.Equal(expectedGames, games.Count);
        db.ImportLaunchBox(games, overrides ?? new Dictionary<string, string>(), Path.Combine(_root, "LaunchBox"));
        return db;
    }

    [Fact]
    public void MatchesPlatformsAndGames()
    {
        var db = Imported();
        var summary = db.LaunchBoxSummaries();
        var arcade = summary.Single(s => s.LaunchBoxPlatform == "Arcade");
        Assert.Equal(("MAME", "alias", 3, 2, 1), (arcade.Platform, arcade.How, arcade.Games, arcade.ByRom, arcade.New));
        var genesis = summary.Single(s => s.LaunchBoxPlatform == "Sega Genesis");
        Assert.Equal(("Sega_Genesis", 1, 1), (genesis.Platform, genesis.ByRom, genesis.New)); // sor2 by rom name, Sonic new

        Assert.NotNull(db.GetGame("MAME", "sf2")!.AddedToLaunchBox);
        var sonic = db.GetGame("Sega_Genesis", "Sonic the Hedgehog (USA)")!;
        Assert.Equal("Sonic the Hedgehog", sonic.Description);
        Assert.Equal(GameLighting.PlatformDefault, sonic.Lighting);
    }

    [Fact]
    public void RecentlyAddedNewestFirstAndNeedsSetup()
    {
        var db = Imported();
        var (recent, _) = db.Search(new GameFilter { AddedWithinDays = 7, LitOnly = false });
        Assert.Equal(new[] { "zzznew", "Sonic the Hedgehog (USA)", "sf2" }, recent.Select(g => g.Name));

        var (todo, _) = db.Search(new GameFilter { AddedWithinDays = 7, NeedsSetupOnly = true, LitOnly = false });
        Assert.Equal(new[] { "zzznew", "Sonic the Hedgehog (USA)" }, todo.Select(g => g.Name));

        // Customizing a game takes it off the "needs setup" list.
        db.SaveUserLights("Sega_Genesis", "Sonic the Hedgehog (USA)", new LightProfile { Customized = true });
        Assert.Equal(new[] { "zzznew" }, db.Search(new GameFilter { AddedWithinDays = 7, NeedsSetupOnly = true, LitOnly = false }).Games.Select(g => g.Name));

        Assert.Equal(5, db.Search(new GameFilter { InLaunchBoxOnly = true, LitOnly = false }).Total);
    }

    [Fact]
    public void LaunchBoxGamesSurviveALedBlinkyReimport()
    {
        var db = Imported();
        db.ReplaceAll(Library(), "re-import");
        Assert.NotNull(db.GetGame("MAME", "zzznew"));
        Assert.Equal(3, db.Search(new GameFilter { AddedWithinDays = 7, LitOnly = false }).Total);
    }

    [Fact]
    public void PlatformOverrideIsUsed()
    {
        var db = Imported(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Sega Genesis"] = "MAME" });
        var genesis = db.LaunchBoxSummaries().Single(s => s.LaunchBoxPlatform == "Sega Genesis");
        Assert.Equal(("MAME", "your mapping"), (genesis.Platform, genesis.How));
    }

    [Fact]
    public void LaunchBoxSmartListsAreSeededFirst()
    {
        var db = GameDatabase.Open(_dbPath);
        Assert.Equal("New in LaunchBox, needs setup (30 days)", db.ListSmartLists()[0].Name);
        Assert.Contains(db.ListSmartLists(), s => s.Name == "6-button fighting games");
    }

    private void WriteCaveAndLaserDisc()
    {
        var platforms = Path.Combine(_root, "LaunchBox", "Data", "Platforms");
        File.WriteAllText(Path.Combine(platforms, "Cave.xml"), """
            <?xml version="1.0" standalone="yes"?>
            <LaunchBox>
              <Game><ApplicationPath>..\roms\cave\ddonpach.zip</ApplicationPath><Platform>Cave</Platform><Title>DoDonPachi</Title><Genre>Shooter</Genre></Game>
            </LaunchBox>
            """);
        File.WriteAllText(Path.Combine(platforms, "Laser Disc.xml"), """
            <?xml version="1.0" standalone="yes"?>
            <LaunchBox>
              <Game><ApplicationPath>..\Emulators\mame\mame.exe</ApplicationPath><Platform>Laser Disc</Platform><Title>Inter Stellar Zangus</Title></Game>
              <Game><ApplicationPath>..\Emulators\odd\odd-emu.exe</ApplicationPath><Platform>Laser Disc</Platform><Title>Odd Game</Title></Game>
            </LaunchBox>
            """);
        File.WriteAllText(Path.Combine(_root, "LaunchBox", "Data", "Emulators.xml"), """
            <?xml version="1.0" standalone="yes"?>
            <LaunchBox>
              <Emulator><ApplicationPath>..\Emulators\odd\odd-emu.exe</ApplicationPath><Title>Odd</Title></Emulator>
              <EmulatorPlatform><Emulator>abc</Emulator><Platform>Laser Disc</Platform></EmulatorPlatform>
            </LaunchBox>
            """);
    }

    [Fact]
    public void AGameThatPointsAtTheEmulatorIsMatchedByTitle()
    {
        WriteCaveAndLaserDisc();
        var db = Imported(expectedGames: 8);
        Assert.NotNull(db.GetGame("Laser_Disc", "Inter Stellar Zangus")); // built-in list (mame.exe)
        Assert.NotNull(db.GetGame("Laser_Disc", "Odd Game"));             // from Emulators.xml
        Assert.Null(db.GetGame("Laser_Disc", "mame"));
    }

    [Fact]
    public void CaveUsesMameDataAndOlderLibrariesAreUpgraded()
    {
        WriteCaveAndLaserDisc();
        var db = Imported(expectedGames: 8);
        Assert.True(db.GetPlatform("Cave")!.UsesMameData);
        Assert.Equal(GameLighting.Parent, db.GetGame("Cave", "ddonpach")!.Lighting);
        Assert.Equal((6, "joystick", 2), (db.GetGame("Cave", "ddonpach")!.Buttons, db.GetGame("Cave", "ddonpach")!.Tags,
            db.GetGame("Cave", "ddonpach")!.Players)); // taken from MAME's row
        Assert.False(db.GetPlatform("Laser_Disc")!.UsesMameData);

        // A library made before this rule had Cave with uses_mame 0 and the game unlit.
        using (var c = new SqliteConnection($"Data Source={_dbPath}"))
        {
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "UPDATE platforms SET uses_mame = 0 WHERE key = 'Cave'; UPDATE games SET lighting = 0 WHERE platform = 'Cave';";
            cmd.ExecuteNonQuery();
        }
        var reopened = GameDatabase.Open(_dbPath);
        Assert.False(reopened.GetPlatform("Cave")!.UsesMameData);
        reopened.ImportLaunchBox(LaunchBoxLibrary.Read(Path.Combine(_root, "LaunchBox"), new List<string>()),
            new Dictionary<string, string>(), Path.Combine(_root, "LaunchBox"));
        Assert.True(reopened.GetPlatform("Cave")!.UsesMameData);
        Assert.Equal(GameLighting.Parent, reopened.GetGame("Cave", "ddonpach")!.Lighting);
    }

    [Fact]
    public void GenreFilter()
    {
        var db = Imported();
        var (fighters, _) = db.Search(new GameFilter { Genre = "fight" });
        Assert.Equal(new[] { "sf2" }, fighters.Select(g => g.Name));
        Assert.Equal("genre \"fight\" · with lights", new GameFilter { Genre = "fight" }.Describe());
    }

    [Fact]
    public void SchemaVersionIsWritten()
    {
        GameDatabase.Open(_dbPath);
        using var c = new SqliteConnection($"Data Source={_dbPath}");
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT value FROM meta WHERE key = 'schema_version'";
        Assert.Equal(GameDatabase.SchemaVersion.ToString(CultureInfo.InvariantCulture), cmd.ExecuteScalar());
    }
}

public class WindowPlacementTests
{
    private static readonly WindowBounds Screen = new(0, 0, 1920, 1040);

    [Fact]
    public void OffScreenWindowIsMovedBack()
    {
        // Found at the cabinet: x 919–2359 on a 1920-wide display.
        var b = WindowPlacement.Clamp(new WindowBounds(919, 100, 1440, 860), Screen);
        Assert.Equal(new WindowBounds(480, 100, 1440, 860), b);
    }

    [Fact]
    public void TooBigWindowShrinksButNotBelowItsMinimum()
    {
        var small = new WindowBounds(0, 0, 1280, 720);
        Assert.Equal(new WindowBounds(0, 0, 1280, 720), WindowPlacement.Clamp(new WindowBounds(-50, -20, 1440, 860), small, 1000, 600));
        Assert.Equal(new WindowBounds(10, 10, 1000, 600), WindowPlacement.Clamp(new WindowBounds(10, 10, 400, 300), small, 1000, 600));
    }

    [Fact]
    public void WindowOnScreenIsUnchangedAndSaveRoundTrips()
    {
        var w = new WindowBounds(100, 50, 1440, 860, Maximized: true);
        Assert.Equal(w, WindowPlacement.Clamp(w, Screen));
        var path = Path.Combine(Path.GetTempPath(), $"lednext-window-{Guid.NewGuid():N}.json");
        try
        {
            WindowPlacement.Save(path, w);
            Assert.Equal(w, WindowPlacement.Load(path));
            File.WriteAllText(path, "not json");
            Assert.Null(WindowPlacement.Load(path));
        }
        finally { File.Delete(path); }
    }
}
