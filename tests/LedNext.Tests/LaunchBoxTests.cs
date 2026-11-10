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
        string Game(string title, string platform, string path, int daysAgo) => $"""
              <Game>
                <ApplicationPath>{path}</ApplicationPath>
                <DateAdded>{Ago(daysAgo)}</DateAdded>
                <ID>{Guid.NewGuid()}</ID>
                <Platform>{platform}</Platform>
                <Title>{title}</Title>
              </Game>
            """;
        File.WriteAllText(Path.Combine(platforms, "Arcade.xml"), $"""
            <?xml version="1.0" standalone="yes"?>
            <LaunchBox>
            {Game("Street Fighter II", "Arcade", @"..\..\Games\MAME\sf2.zip", 3)}
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
        foreach (var rom in new[] { "sf2", "1942" })
        {
            lib.AddGame(new GameInfo("MAME", rom, rom, null, 2, "", "Colors.ini", GameLighting.Own));
            lib.SetLights("MAME", rom, new LightProfile { Controls = { new ControlLight("P1_BUTTON1", Rgb.Red, "Red", null) } });
        }
        lib.AddGame(new GameInfo("Sega_Genesis", "sor2", "Streets of Rage 2", null, 2, "", "", GameLighting.PlatformDefault));
        return lib;
    }

    private GameDatabase Imported(IReadOnlyDictionary<string, string>? overrides = null)
    {
        var db = GameDatabase.Open(_dbPath);
        db.ReplaceAll(Library(), "test");
        var warnings = new List<string>();
        var games = LaunchBoxLibrary.Read(Path.Combine(_root, "LaunchBox"), warnings);
        Assert.Empty(warnings);
        Assert.Equal(5, games.Count);
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
    }
}
