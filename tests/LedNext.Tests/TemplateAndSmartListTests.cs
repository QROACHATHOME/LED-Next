using LedNext.Core;
using LedNext.Core.Games;
using LedNext.Core.Import.LedBlinky;
using LedNext.Data;
using Microsoft.Data.Sqlite;

namespace LedNext.Tests;

public class FilterAndTagTests
{
    [Fact]
    public void DescribeReadsLikeTheList()
    {
        Assert.Equal("6 buttons · no clones · with lights", new GameFilter { MinButtons = 6, MaxButtons = 6, HideClones = true }.Describe());
        Assert.Equal("4+ players · trackball · with lights", new GameFilter { MinPlayers = 4, Control = "trackball" }.Describe());
        Assert.Equal("all games", new GameFilter { LitOnly = false }.Describe());
    }

    [Fact]
    public void TagsAndButtonCounts()
    {
        var p = new LightProfile
        {
            Controls =
            {
                new ControlLight("P1_BUTTON1", Rgb.Red, "Red", null),
                new ControlLight("P2_BUTTON6", Rgb.Red, "Red", null),
                new ControlLight("P1_TRACKBALL", Rgb.White, "White", null),
                new ControlLight("P1_JOYSTICK", Rgb.White, "White", null),
            },
        };
        Assert.Equal(6, ControlTags.ButtonCount(p));
        Assert.Equal("joystick trackball", ControlTags.Join(ControlTags.FromLights(p)));
        Assert.Equal("spinner", ControlTags.Join(ControlTags.FromInfo("360 Steering Wheel")));
        Assert.Equal("lightgun", ControlTags.Join(ControlTags.FromInfo("Analog Gun")));
        Assert.Equal("joystick", ControlTags.Join(ControlTags.FromInfo("8-way Joystick")));
    }

    [Fact]
    public void BuiltInFighterTemplateLightsBothPlayers()
    {
        var t = BuiltIns.Templates.Single(t => t.Name == "6-button fighter");
        var panel = new PanelLayout();
        foreach (var id in new[] { "P1_B1", "P1_B4", "P2_B6", "P1_START", "P3_B1" })
            panel.Buttons.Add(new PanelButton { Id = id, Player = id[1] - '0' });
        var r = GameLightResolver.ResolveProfile(t.Profile, t.Name, new ControlMap(), panel);
        Assert.Equal("template 6-button fighter", r.From);
        Assert.Equal(new Rgb(255, 0, 0), r.ButtonColors["P1_B1"]);
        Assert.Equal(new Rgb(0, 0, 255), r.ButtonColors["P1_B4"]);
        Assert.Equal(new Rgb(0, 0, 255), r.ButtonColors["P2_B6"]);
        Assert.False(r.ButtonColors.ContainsKey("P3_B1"));
    }
}

public class TemplateAndSmartListDatabaseTests
{
    private static string TempDb() => Path.Combine(Path.GetTempPath(), $"lednext-tpl-{Guid.NewGuid():N}.db");

    private static void Cleanup(string path)
    {
        SqliteConnection.ClearAllPools();
        foreach (var f in new[] { path, path + "-wal", path + "-shm" }) if (File.Exists(f)) File.Delete(f);
    }

    [Fact]
    public void StartersAreSeededOnceAndDeletesStick()
    {
        var path = TempDb();
        try
        {
            var db = GameDatabase.Open(path);
            Assert.Contains(db.ListTemplates(), t => t.Name == "6-button fighter");
            Assert.Contains(db.ListSmartLists(), l => l.Name == "6-button games");

            db.DeleteTemplate("6-button fighter");
            db.DeleteSmartList("6-button games");
            db = GameDatabase.Open(path); // reopening does not bring them back
            Assert.DoesNotContain(db.ListTemplates(), t => t.Name == "6-button fighter");
            Assert.DoesNotContain(db.ListSmartLists(), l => l.Name == "6-button games");
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public void TemplateAndSmartListRoundTrip()
    {
        var path = TempDb();
        try
        {
            var db = GameDatabase.Open(path);
            var profile = new LightProfile { Customized = true, Controls = { new ControlLight("P1_BUTTON1", Rgb.Red, "Red", "Fire") } };
            db.SaveTemplate(new LightTemplate("Mine", profile, "test"));
            var back = db.GetTemplate("mine")!;
            Assert.Equal(Rgb.Red, back.Profile.Find("P1_BUTTON1")!.Color);
            Assert.Null(back.Profile.Find("P1_BUTTON1")!.Label); // templates keep colours, not game labels

            var filter = new GameFilter { Platform = "MAME", MinButtons = 3, Control = "spinner", HideClones = true };
            db.SaveSmartList("Spinners 3+", filter);
            Assert.Equal(filter, db.ListSmartLists().Single(l => l.Name == "Spinners 3+").Filter);
            db.SaveSmartList("Spinners 3+", filter with { MinButtons = 4 }); // replace keeps one entry
            Assert.Single(db.ListSmartLists(), l => l.Name == "Spinners 3+");
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public void SchemaTwoLibraryGetsTheNewColumns()
    {
        var path = TempDb();
        try
        {
            using (var c = new SqliteConnection($"Data Source={path}"))
            {
                c.Open();
                using var cmd = c.CreateCommand();
                cmd.CommandText = """
                    CREATE TABLE games(platform TEXT NOT NULL COLLATE NOCASE, name TEXT NOT NULL COLLATE NOCASE,
                        description TEXT NOT NULL, parent TEXT, players INTEGER NOT NULL, info TEXT NOT NULL,
                        sources TEXT NOT NULL, lighting INTEGER NOT NULL, PRIMARY KEY(platform, name));
                    INSERT INTO games VALUES('MAME', 'old', 'Old game', NULL, 2, '', '', 1);
                    """;
                cmd.ExecuteNonQuery();
            }
            var db = GameDatabase.Open(path);
            var g = db.GetGame("MAME", "old")!;
            Assert.Equal(0, g.Buttons);
            Assert.Equal("", g.Tags);
            Assert.Equal("4", db.Meta("schema_version"));
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public void RealLibraryFiltersFindGenres()
    {
        var root = LedBlinkyImportTests.RepoRoot();
        var cabinet = ConfigStore.LoadOrCreate(Path.Combine(root, "cabinet", "cabinet.json"));
        var result = LedBlinkyImporter.Import(Path.Combine(root, "import-samples", "ledblinky"), cabinet.Panel, cabinet.Wiring,
            mameFolder: Path.Combine(root, "import-samples", "mame"));
        var path = TempDb();
        try
        {
            var db = GameDatabase.Open(path);
            db.ReplaceAll(result.Library, "test");

            var sf2 = db.GetGame("MAME", "sf2")!;
            Assert.Equal(6, sf2.Buttons);

            var (sixButton, total6) = db.Search(new GameFilter { MinButtons = 6, MaxButtons = 6, HideClones = true }, 5000);
            Assert.Contains(sixButton, g => g.Name == "sf2");
            Assert.All(sixButton, g => Assert.Null(g.Parent));

            var (trackball, _) = db.Search(new GameFilter { Control = "trackball" }, 5000);
            Assert.Contains(trackball, g => g.Name == "centiped");

            var (fourPlayer, _) = db.Search(new GameFilter { MinPlayers = 4 }, 5000);
            Assert.All(fourPlayer, g => Assert.True(g.Players >= 4));
            Assert.True(total6 > 50, $"{total6} six-button games");
        }
        finally { Cleanup(path); }
    }
}
