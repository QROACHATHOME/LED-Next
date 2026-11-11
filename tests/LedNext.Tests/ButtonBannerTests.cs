using LedNext.Core;
using LedNext.Core.Games;
using LedNext.Data;

namespace LedNext.Tests;

public class ButtonBannerTests
{
    private static readonly Rgb Grey = new(10, 10, 10), Blue = new(0, 0, 255);

    /// <summary>A fake banner: dark grey buttons, the used ones drawn blue (as in the real 800 x 480 pictures).</summary>
    private static (byte, byte, byte, byte) Pixel(int x, int y, int[] used, double scale = 1)
    {
        (int X, int Y)[] centres = { (410, 340), (540, 288), (682, 312), (438, 192), (568, 136), (707, 160) };
        for (int i = 0; i < centres.Length; i++)
        {
            double dx = x / scale - centres[i].X, dy = y / scale - centres[i].Y;
            if (dx * dx + dy * dy < 50 * 50)
                return used.Contains(i + 1) ? ((byte)1, (byte)165, (byte)228, (byte)255) : ((byte)50, (byte)50, (byte)50, (byte)255);
        }
        return (0, 0, 0, 0);
    }

    [Fact]
    public void ReadsWhichButtonsABannerMarksAsUsed()
    {
        Assert.Equal(new[] { 1, 2 }, ButtonBanners.UsedPositions(800, 480, (x, y) => Pixel(x, y, new[] { 1, 2 })));
        // JoJo: the top row and bottom-left. Only the count is used for lighting.
        Assert.Equal(new[] { 1, 4, 5, 6 }, ButtonBanners.UsedPositions(800, 480, (x, y) => Pixel(x, y, new[] { 1, 4, 5, 6 })));
        Assert.Empty(ButtonBanners.UsedPositions(800, 480, (x, y) => Pixel(x, y, Array.Empty<int>()))!);
        // Same shape at another size; a different shape is not a banner.
        Assert.Equal(new[] { 1, 2, 3 }, ButtonBanners.UsedPositions(400, 240, (x, y) => Pixel(x, y, new[] { 1, 2, 3 }, 0.5)));
        Assert.Null(ButtonBanners.UsedPositions(640, 640, (_, _) => (0, 0, 0, 0)));
    }

    private static PanelLayout Panel()
    {
        var p = new PanelLayout();
        foreach (var id in new[] { "P1_B1", "P1_B2", "P1_B3", "P1_B4", "P2_B1", "P2_B2", "P2_B3", "P3_B1", "P1_STICK" })
            p.Buttons.Add(new PanelButton { Id = id, Player = id[1] - '0' });
        return p;
    }

    private static InMemoryGameLibrary Library()
    {
        var lib = new InMemoryGameLibrary();
        lib.AddPlatform(new PlatformInfo("MAME", "MAME", null, false, Blue));
        lib.AddPlatform(new PlatformInfo("Cave", "Cave", null, true, Blue));
        lib.AddPlatform(new PlatformInfo("Sega_Genesis", "Sega Genesis", null, false, Blue));
        // Like the cabinet's MAME default: button 1 dim, the rest off, the stick lit.
        lib.SetLights("MAME", null, new LightProfile
        {
            Controls =
            {
                new ControlLight("P1_BUTTON1", Grey, "dim", null),
                new ControlLight("P1_BUTTON2", new Rgb(0, 0, 0), "off", null),
                new ControlLight("P1_BUTTON3", new Rgb(0, 0, 0), "off", null),
                new ControlLight("P1_JOYSTICK", Rgb.White, "White", null),
            },
        });
        lib.AddGame(new GameInfo("MAME", "twobtn", "Two buttons", null, 2, "", "", GameLighting.PlatformDefault));
        lib.AddGame(new GameInfo("MAME", "twobtna", "Two buttons (clone)", "twobtn", 2, "", "", GameLighting.PlatformDefault));
        lib.AddGame(new GameInfo("MAME", "stickonly", "Stick only", null, 1, "", "", GameLighting.PlatformDefault));
        lib.AddGame(new GameInfo("MAME", "lit", "Has lights", null, 2, "", "", GameLighting.Own));
        lib.SetLights("MAME", "lit", new LightProfile { Controls = { new ControlLight("P1_BUTTON1", Rgb.Green, "Green", null) } });
        lib.AddGame(new GameInfo("Cave", "ddonpach", "DoDonPachi", null, 2, "", "", GameLighting.PlatformDefault));
        lib.AddGame(new GameInfo("Sega_Genesis", "twobtn", "Same name on a console", null, 1, "", "", GameLighting.PlatformDefault));
        lib.BannerButtons["twobtn"] = 2;
        lib.BannerButtons["stickonly"] = 0;
        lib.BannerButtons["lit"] = 6;
        lib.BannerButtons["ddonpach"] = 3;
        return lib;
    }

    private static ControlMap Map()
    {
        var map = new ControlMap();
        map.Mappings.Add(new ControlMapping { Control = "P1_JOYSTICK", Buttons = { "P1_STICK" }, Source = "test" });
        return map;
    }

    [Fact]
    public void AGameWithoutLightsLightsTheBannersButtonsForEachPlayer()
    {
        var r = GameLightResolver.Resolve(Library(), "MAME", "twobtn", Map(), Panel());
        Assert.Equal("MAME default + button banner twobtn (2 buttons)", r.From);
        Assert.Equal(new[] { "P1_B1", "P1_B2", "P1_STICK", "P2_B1", "P2_B2" }, r.ButtonColors.Keys.Order());
        Assert.Equal(Blue, r.ButtonColors["P1_B2"]);       // the default-active colour
        Assert.Equal(Rgb.White, r.ButtonColors["P1_STICK"]); // the rest of the default is kept
        Assert.False(r.GameSpecific);                        // still "needs setup"

        // A clone uses its parent's banner; a stick-only banner turns the default's button off.
        Assert.Contains("button banner twobtn", GameLightResolver.Resolve(Library(), "MAME", "twobtna", Map(), Panel()).From);
        var stick = GameLightResolver.Resolve(Library(), "MAME", "stickonly", Map(), Panel());
        Assert.Equal(new[] { "P1_STICK" }, stick.ButtonColors.Keys);
    }

    [Fact]
    public void ABannerNeverOverridesRealLightsOrNonMamePlatforms()
    {
        var lib = Library();
        Assert.Equal(new[] { "P1_B1" }, GameLightResolver.Resolve(lib, "MAME", "lit", Map(), Panel()).ButtonColors.Keys);
        // A MAME-data platform uses it (Cave runs MAME roms); a console with the same game name doesn't.
        Assert.Contains("button banner ddonpach (3 buttons)", GameLightResolver.Resolve(lib, "Cave", "ddonpach", Map(), Panel()).From);
        Assert.DoesNotContain("banner", GameLightResolver.Resolve(lib, "Sega_Genesis", "twobtn", Map(), Panel()).From);
        // A customized platform default is the user's choice: left alone.
        lib.SetLights("MAME", null, new LightProfile { Customized = true, Controls = { new ControlLight("P1_BUTTON1", Rgb.Green, "Green", null) } });
        Assert.Equal("MAME default (customized)", GameLightResolver.Resolve(lib, "MAME", "twobtn", Map(), Panel()).From);
    }

    [Fact]
    public void TheGameLibraryKeepsBannerCounts()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lednext-banners-{Guid.NewGuid():N}.db");
        try
        {
            var db = GameDatabase.Open(path);
            Assert.Null(db.GetBannerButtons("1941"));
            db.ReplaceBannerButtons(new Dictionary<string, IReadOnlyList<int>> { ["1941"] = new[] { 1, 2 }, ["jojoba"] = new[] { 1, 4, 5, 6 } },
                @"C:\banners", "2:1:2");
            Assert.Equal(2, db.GetBannerButtons("1941"));
            Assert.Equal(4, db.GetBannerButtons("JOJOBA"));
            Assert.Equal(2, db.BannerCount());
            Assert.Equal("2:1:2", GameDatabase.Open(path).Meta("banner_stamp"));
            db.ReplaceBannerButtons(new Dictionary<string, IReadOnlyList<int>>(), null, null);
            Assert.Null(db.GetBannerButtons("1941"));
            Assert.Null(db.Meta("banner_folder"));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (var f in Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(path) + "*")) File.Delete(f);
        }
    }
}
