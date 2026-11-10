using LedNext.Core;
using LedNext.Core.Games;
using LedNext.Data;

namespace LedNext.Tests;

public class GameLightEditorTests
{
    private static readonly Rgb Red = new(255, 0, 0), Blue = new(0, 0, 255);

    private static PanelLayout Panel()
    {
        var p = new PanelLayout();
        foreach (var id in new[] { "P1_B1", "P1_B2", "P1_B3", "P1_B7", "P1_B8", "P1_START", "P2_B1", "P3_B3" })
            p.Buttons.Add(new PanelButton { Id = id, Player = id[1] - '0' });
        p.Buttons.Add(new PanelButton { Id = "TRACKBALL", Kind = ControlKind.Trackball });
        return p;
    }

    private static InMemoryGameLibrary Library()
    {
        var lib = new InMemoryGameLibrary();
        lib.AddPlatform(new PlatformInfo("MAME", "MAME", null, false, Rgb.White));
        lib.SetLights("MAME", null, new LightProfile
        {
            Controls =
            {
                new ControlLight("P1_BUTTON1", new Rgb(10, 10, 10), "dim", null),
                new ControlLight("UI_SELECT", Rgb.Green, "Green", null, AlwaysActive: true),
            },
        });
        lib.AddGame(new GameInfo("MAME", "game", "Game", null, 2, "", "", GameLighting.Own));
        lib.SetLights("MAME", "game", new LightProfile
        {
            Controls = { new ControlLight("P1_BUTTON1", Red, "Red", "Fire"), new ControlLight("P1_START", null, null, null) },
        });
        lib.AddGame(new GameInfo("MAME", "clone", "Clone", "game", 2, "", "", GameLighting.Parent));
        return lib;
    }

    [Fact]
    public void MaterializeCopiesWhatIsShownWithoutPlatformAlwaysLitControls()
    {
        var lights = GameLightResolver.Resolve(Library(), "MAME", "clone", new ControlMap(), Panel());
        var p = GameLightEditor.Materialize(lights);

        Assert.True(p.Customized);
        Assert.Equal(new[] { "P1_BUTTON1", "P1_START" }, p.Controls.Select(c => c.Control));
        Assert.Equal(Red, p.Find("P1_BUTTON1")!.Color);
        Assert.Equal("Fire", p.Find("P1_BUTTON1")!.Label);
        Assert.Equal(Rgb.White, p.Find("P1_START")!.Color); // the default colour, made explicit
    }

    [Fact]
    public void PlatformDefaultKeepsItsAlwaysLitControls()
    {
        var lights = GameLightResolver.Resolve(Library(), "MAME", null, new ControlMap(), Panel());
        var p = GameLightEditor.Materialize(lights);
        Assert.True(p.Find("UI_SELECT")!.AlwaysActive);
    }

    [Fact]
    public void EditedCloneOverridesItsParent()
    {
        var lib = Library();
        var panel = Panel();
        var p = GameLightEditor.Materialize(GameLightResolver.Resolve(lib, "MAME", "clone", new ControlMap(), panel));
        p = GameLightEditor.SetColor(p, "P1_BUTTON1", Blue, "Blue");
        p = GameLightEditor.SetColor(p, "P1_BUTTON3", Red, "Red");
        p = GameLightEditor.TurnOff(p, "P1_START");
        lib.SetLights("MAME", "clone", p);

        var r = GameLightResolver.Resolve(lib, "MAME", "clone", new ControlMap(), panel);
        Assert.Equal("game clone (customized)", r.From);
        Assert.Equal(Blue, r.ButtonColors["P1_B1"]);
        Assert.Equal(Red, r.ButtonColors["P1_B3"]);
        Assert.False(r.ButtonColors.ContainsKey("P1_START"));
        Assert.Equal("Fire", r.Controls.First(c => c.Control == "P1_BUTTON1").Label);

        // The parent is unchanged.
        Assert.Equal(Red, GameLightResolver.Resolve(lib, "MAME", "game", new ControlMap(), panel).ButtonColors["P1_B1"]);
    }

    [Fact]
    public void CustomizedEmptyProfileMeansAllOff()
    {
        var lib = Library();
        lib.SetLights("MAME", "clone", new LightProfile { Customized = true });
        var r = GameLightResolver.Resolve(lib, "MAME", "clone", new ControlMap(), Panel());
        Assert.Equal("game clone (customized)", r.From);
        Assert.False(r.ButtonColors.ContainsKey("P1_B1"));
    }

    [Fact]
    public void ControlForButtonUsesTheLitControlThenTheName()
    {
        var panel = Panel();
        var lights = GameLightResolver.Resolve(Library(), "MAME", "game", new ControlMap(), panel);
        Assert.Equal("P1_BUTTON1", GameLightEditor.ControlForButton("P1_B1", lights, new ControlMap(), panel));
        Assert.Equal("P1_BUTTON3", GameLightEditor.ControlForButton("P1_B3", lights, new ControlMap(), panel));
        Assert.Equal("P1_START", GameLightEditor.ControlForButton("P1_START", lights, new ControlMap(), panel));
        Assert.Equal("P1_TRACKBALL", GameLightEditor.ControlForButton("TRACKBALL", lights, new ControlMap(), panel));
    }

    [Fact]
    public void ControlForButtonFollowsASwappedMapAndIgnoresMahjongKeys()
    {
        var panel = Panel();
        var map = new ControlMap();
        map.Mappings.Add(new ControlMapping { Control = "P1_BUTTON7", Buttons = { "P1_B8" }, Source = "input code KEYCODE_C" });
        map.Mappings.Add(new ControlMapping { Control = "P1_BUTTON8", Buttons = { "P1_B7" }, Source = "input code KEYCODE_V" });
        map.Mappings.Add(new ControlMapping { Control = "P1_MAHJONG_C", Buttons = { "P1_B8" }, Source = "input code KEYCODE_C" });
        var lights = GameLightResolver.Resolve(Library(), "MAME", "game", map, panel);
        Assert.Equal("P1_BUTTON7", GameLightEditor.ControlForButton("P1_B8", lights, map, panel));
        Assert.Equal("P1_BUTTON8", GameLightEditor.ControlForButton("P1_B7", lights, map, panel));
    }
}

public class UserEditDatabaseTests
{
    private static readonly Rgb Blue = new(0, 0, 255);

    private static InMemoryGameLibrary Library()
    {
        var lib = new InMemoryGameLibrary();
        lib.AddPlatform(new PlatformInfo("MAME", "MAME", null, false, Rgb.White));
        lib.SetLights("MAME", null, new LightProfile { Controls = { new ControlLight("P1_BUTTON1", Rgb.White, "White", null) } });
        lib.AddGame(new GameInfo("MAME", "1942", "1942", null, 2, "", "Colors.ini", GameLighting.Own));
        lib.SetLights("MAME", "1942", new LightProfile { Controls = { new ControlLight("P1_BUTTON1", Rgb.Red, "Red", "Fire") } });
        lib.AddGame(new GameInfo("MAME", "unlit", "Unlit game", null, 1, "", "", GameLighting.PlatformDefault));
        return lib;
    }

    private static void WithDb(Action<GameDatabase> test)
    {
        var path = Path.Combine(Path.GetTempPath(), $"lednext-edit-{Guid.NewGuid():N}.db");
        try
        {
            var db = GameDatabase.Open(path);
            db.ReplaceAll(Library(), "test");
            test(db);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (var f in new[] { path, path + "-wal", path + "-shm" }) if (File.Exists(f)) File.Delete(f);
        }
    }

    private static PanelLayout Panel()
    {
        var p = new PanelLayout();
        p.Buttons.Add(new PanelButton { Id = "P1_B1", Player = 1 });
        p.Buttons.Add(new PanelButton { Id = "P1_B2", Player = 1 });
        return p;
    }

    [Fact]
    public void UserEditWinsSurvivesReimportAndReverts() => WithDb(db =>
    {
        var edit = new LightProfile { Customized = true, Controls = { new ControlLight("P1_BUTTON2", Blue, "Blue", null) } };
        db.SaveUserLights("MAME", "1942", edit);

        var r = GameLightResolver.Resolve(db, "MAME", "1942", new ControlMap(), Panel());
        Assert.Equal("game 1942 (customized)", r.From);
        Assert.Equal(Blue, r.ButtonColors["P1_B2"]);
        Assert.False(r.ButtonColors.ContainsKey("P1_B1"));
        Assert.True(db.GetGame("MAME", "1942")!.Customized);

        db.ReplaceAll(Library(), "re-import");
        Assert.True(db.IsCustomized("MAME", "1942"));
        Assert.Equal(Blue, GameLightResolver.Resolve(db, "MAME", "1942", new ControlMap(), Panel()).ButtonColors["P1_B2"]);

        db.RevertUserLights("MAME", "1942");
        var back = GameLightResolver.Resolve(db, "MAME", "1942", new ControlMap(), Panel());
        Assert.Equal("game 1942", back.From);
        Assert.Equal(Rgb.Red, back.ButtonColors["P1_B1"]);
    });

    [Fact]
    public void EmptyEditTurnsTheGameOffAndPlatformEditReachesUnlitGames() => WithDb(db =>
    {
        db.SaveUserLights("MAME", "1942", new LightProfile { Customized = true });
        Assert.Empty(GameLightResolver.Resolve(db, "MAME", "1942", new ControlMap(), Panel()).ButtonColors);

        db.SaveUserLights("MAME", null, new LightProfile { Customized = true, Controls = { new ControlLight("P1_BUTTON1", Blue, "Blue", null) } });
        var unlit = GameLightResolver.Resolve(db, "MAME", "unlit", new ControlMap(), Panel());
        Assert.Equal("MAME default (customized)", unlit.From);
        Assert.Equal(Blue, unlit.ButtonColors["P1_B1"]);
    });

    [Fact]
    public void SearchCanListOnlyCustomizedGames() => WithDb(db =>
    {
        db.SaveUserLights("MAME", "unlit", new LightProfile { Customized = true, Controls = { new ControlLight("P1_BUTTON1", Blue, "Blue", null) } });
        var (custom, total) = db.Search("MAME", null, litOnly: false, customizedOnly: true);
        Assert.Equal(1, total);
        Assert.Equal("unlit", custom.Single().Name);
        // A customized game counts as lit even if the import had no data for it.
        Assert.Contains(db.Search("MAME", null, litOnly: true).Games, g => g.Name == "unlit");
        Assert.Equal(1, db.CustomizedCount());
    });
}
