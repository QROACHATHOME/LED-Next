using LedNext.Core;
using LedNext.Core.Lighting;

namespace LedNext.Tests;

public class WiringTests
{
    private static WiringMap TwoBoards()
    {
        var w = new WiringMap();
        w.Boards.Add(new BoardDefinition { Key = "PacLED64#1", Name = "B1" });
        w.Boards.Add(new BoardDefinition { Key = "PacLED64#2", Name = "B2" });
        return w;
    }

    [Fact]
    public void Connect_ReplacesExistingBindingOnSamePin()
    {
        var w = TwoBoards();
        w.Connect("A", ColorChannel.R, "PacLED64#1", 5);
        w.Connect("B", ColorChannel.G, "PacLED64#1", 5);

        var bind = Assert.Single(w.Bindings);
        Assert.Equal("B", bind.ButtonId);
    }

    [Fact]
    public void Connect_ReplacesExistingBindingOnSameChannel()
    {
        var w = TwoBoards();
        w.Connect("A", ColorChannel.R, "PacLED64#1", 5);
        w.Connect("A", ColorChannel.R, "PacLED64#2", 9);

        var bind = Assert.Single(w.Bindings);
        Assert.Equal(("PacLED64#2", 9), (bind.BoardKey, bind.Pin));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65)]
    public void Connect_RejectsPinsOutsideTheBoard(int pin)
    {
        var w = TwoBoards();
        Assert.Throws<ArgumentOutOfRangeException>(() => w.Connect("A", ColorChannel.R, "PacLED64#1", pin));
    }

    [Fact]
    public void RenameButton_MovesBindings()
    {
        var w = TwoBoards();
        w.Connect("A", ColorChannel.B, "PacLED64#1", 3);
        w.RenameButton("A", "Z");
        Assert.Equal("Z", w.Bindings[0].ButtonId);
    }

    [Fact]
    public void DefaultCabinet_IsFullyWiredWithoutCollisions()
    {
        var config = Defaults.CreateFourPlayerCabinet();

        // 28 buttons + 4 start + 4 coin + 4 joysticks + 1 trackball
        Assert.Equal(41, config.Panel.Buttons.Count);
        Assert.Equal(123, config.Wiring.Bindings.Count);
        Assert.All(config.Panel.Buttons, b =>
            Assert.All(Enum.GetValues<ColorChannel>(), ch => Assert.NotNull(config.Wiring.Find(b.Id, ch))));
        Assert.Equal(123, config.Wiring.Bindings.Select(b => (b.BoardKey, b.Pin)).Distinct().Count());
        foreach (var p in new[] { 1, 2, 3, 4 })
        {
            Assert.Equal(ControlKind.Coin, config.Panel.Find($"P{p}_COIN")!.Kind);
            Assert.Equal(ControlKind.Joystick, config.Panel.Find($"P{p}_STICK")!.Kind);
        }
        Assert.Equal(ControlKind.Trackball, config.Panel.Find("TRACKBALL")!.Kind);
        // P1 fills board 1 first; P4 and the trackball are on board 2.
        Assert.All(config.Wiring.Bindings.Where(b => b.ButtonId.StartsWith("P1_")), b => Assert.Equal("PacLED64#1", b.BoardKey));
        Assert.All(config.Wiring.Bindings.Where(b => b.ButtonId.StartsWith("P4_") || b.ButtonId == "TRACKBALL"), b => Assert.Equal("PacLED64#2", b.BoardKey));
        // Every control sits inside the panel and no two controls overlap.
        var buttons = config.Panel.Buttons;
        Assert.All(buttons, b => Assert.InRange(b.X, b.Size / 2, config.Panel.Width - b.Size / 2));
        Assert.All(buttons, b => Assert.InRange(b.Y, b.Size / 2, config.Panel.Height - b.Size / 2));
        for (int i = 0; i < buttons.Count; i++)
            for (int j = i + 1; j < buttons.Count; j++)
            {
                double d = Math.Sqrt(Math.Pow(buttons[i].X - buttons[j].X, 2) + Math.Pow(buttons[i].Y - buttons[j].Y, 2));
                Assert.True(d >= (buttons[i].Size + buttons[j].Size) / 2, $"{buttons[i].Id} overlaps {buttons[j].Id}");
            }
        // No RGB triplet is split across two boards.
        Assert.All(config.Panel.Buttons, b => Assert.Single(config.Wiring.Bindings.Where(x => x.ButtonId == b.Id).Select(x => x.BoardKey).Distinct()));
    }

    [Fact]
    public void AutoWire_ReportsButtonsThatDoNotFit()
    {
        var panel = new PanelLayout();
        for (int i = 0; i < 25; i++) panel.Buttons.Add(new PanelButton { Id = $"B{i}" });
        var w = new WiringMap();
        w.Boards.Add(new BoardDefinition { Key = "K", PinCount = 64 }); // 21 RGB buttons fit

        Assert.Equal(4, w.AutoWire(panel));
        Assert.Equal(63, w.Bindings.Count);
    }
}

public class EngineTests
{
    private static CabinetConfig OneButton()
    {
        var c = new CabinetConfig();
        c.Panel.Buttons.Add(new PanelButton { Id = "P1_B1", X = 100, Y = 100 });
        c.Wiring.Boards.Add(new BoardDefinition { Key = "PacLED64#1", Name = "B1" });
        c.Wiring.Connect("P1_B1", ColorChannel.R, "PacLED64#1", 10);
        c.Wiring.Connect("P1_B1", ColorChannel.G, "PacLED64#1", 11);
        c.Wiring.Connect("P1_B1", ColorChannel.B, "PacLED64#1", 12);
        return c;
    }

    [Fact]
    public void ButtonColour_LandsOnWiredPins()
    {
        using var engine = new LedEngine(OneButton()) { Source = new SolidColor(new Rgb(200, 100, 50)) };
        var frame = engine.RenderFrame(TimeSpan.Zero);

        var pins = frame.BoardPins["PacLED64#1"];
        Assert.Equal(64, pins.Length);
        Assert.Equal(200, pins[9]);
        Assert.Equal(100, pins[10]);
        Assert.Equal(50, pins[11]);
        Assert.Equal(3, pins.Count(p => p != 0));
        Assert.Equal(new Rgb(200, 100, 50), frame.ButtonColors["P1_B1"]);
    }

    [Fact]
    public void Brightness_ScalesButtonColours_ButNotRawPins()
    {
        var config = OneButton();
        config.Output.Brightness = 0.5;
        using var engine = new LedEngine(config) { Source = new SolidColor(Rgb.White) };
        Assert.Equal(128, engine.RenderFrame(TimeSpan.Zero).BoardPins["PacLED64#1"][9]);

        var manual = new ManualPins();
        manual.Toggle("PacLED64#1", 40);
        engine.Source = manual;
        Assert.Equal(255, engine.RenderFrame(TimeSpan.Zero).BoardPins["PacLED64#1"][39]);
    }

    [Fact]
    public void RawPinOnWiredButton_ShowsInPreview()
    {
        using var engine = new LedEngine(OneButton());
        var manual = new ManualPins();
        manual.Toggle("PacLED64#1", 11); // the G pin
        engine.Source = manual;

        Assert.Equal(new Rgb(0, 255, 0), engine.RenderFrame(TimeSpan.Zero).ButtonColors["P1_B1"]);
    }

    [Fact]
    public void NewSource_IsShownAtOnce_NotAtTheNextFrameTick()
    {
        var config = OneButton();
        config.Output.RefreshHz = 1; // one frame a second
        using var engine = new LedEngine(config);
        engine.Start();
        Thread.Sleep(100); // the first frame is out; the loop now waits ~1 s
        var shown = new ManualResetEventSlim();
        TimeSpan sinceSet = default;
        var white = new SolidColor(Rgb.White);
        engine.SourceShown += (source, t, _) => { if (ReferenceEquals(source, white)) { sinceSet = t; shown.Set(); } };
        engine.Source = white;
        Assert.True(shown.Wait(500), "the new source should not wait for the next tick");
        Assert.True(sinceSet < TimeSpan.FromMilliseconds(500));
    }

    [Fact]
    public void FailingSource_RendersBlackInsteadOfThrowing()
    {
        using var engine = new LedEngine(OneButton()) { Source = new Throwing() };
        var frame = engine.RenderFrame(TimeSpan.Zero);
        Assert.All(frame.BoardPins["PacLED64#1"], p => Assert.Equal(0, p));
    }

    [Fact]
    public void PinWalk_CoversEveryPinOfEveryBoard()
    {
        var config = Defaults.CreateFourPlayerCabinet();
        using var engine = new LedEngine(config) { Source = new PinWalk(stepSeconds: 1) };

        var lit = new HashSet<(string, int)>();
        for (int s = 0; s < 128; s++)
        {
            var frame = engine.RenderFrame(TimeSpan.FromSeconds(s + 0.5));
            foreach (var (key, pins) in frame.BoardPins)
                for (int i = 0; i < pins.Length; i++)
                    if (pins[i] != 0) lit.Add((key, i + 1));
        }
        Assert.Equal(128, lit.Count);
    }

    [Theory]
    [InlineData(255, 1.0, 1.0, 255)]
    [InlineData(128, 1.0, 2.2, 56)]
    [InlineData(0, 1.0, 2.2, 0)]
    [InlineData(255, 0.0, 1.0, 0)]
    public void Correct_AppliesGammaThenBrightness(byte input, double brightness, double gamma, byte expected) =>
        Assert.Equal(expected, LedEngine.Correct(input, brightness, gamma));

    private sealed class Throwing : ILightSource
    {
        public string Name => "Throwing";
        public void Render(RenderContext ctx) => throw new InvalidOperationException("boom");
    }
}

public class ConfigStoreTests
{
    [Fact]
    public void SaveAndLoad_RoundTrips()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lednext-{Guid.NewGuid():N}", "cabinet.json");
        try
        {
            var config = Defaults.CreateFourPlayerCabinet();
            config.Hardware.Mode = HardwareMode.Hardware;
            ConfigStore.Save(path, config);

            var json = File.ReadAllText(path);
            Assert.Contains("\"mode\": \"Hardware\"", json);

            var loaded = ConfigStore.LoadOrCreate(path);
            Assert.Equal(HardwareMode.Hardware, loaded.Hardware.Mode);
            Assert.Equal(config.Panel.Buttons.Count, loaded.Panel.Buttons.Count);
            Assert.Equal(config.Wiring.Bindings.Count, loaded.Wiring.Bindings.Count);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public void Rgb_HexRoundTrips()
    {
        var c = new Rgb(0x12, 0xAB, 0xFF);
        Assert.Equal("#12ABFF", c.ToHex());
        Assert.Equal(c, Rgb.ParseHex("#12abff"));
    }
}
