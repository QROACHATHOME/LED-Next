using System.Text.Json;
using LedNext.Core;
using LedNext.Core.Lighting;

namespace LedNext.Tests;

/// <summary>NanoLED, PacDrive, LED-Wiz and LED strips (config v14).</summary>
public class MoreBoardsTests
{
    [Theory]
    [InlineData(0x1481, 4, "NanoLED#1")]
    [InlineData(0x1484, 4, "NanoLED#4")]
    [InlineData(0x1500, 1, "PacDrive#1")]
    [InlineData(0x1503, 1, "PacDrive#4")]
    [InlineData(0x1401, 5, "PacLED64#1")]    // unchanged
    [InlineData(0x1490, 4, "NanoLED#1")]     // unknown product, the SDK says NanoLED
    [InlineData(0x1510, 1, "PacDrive#1")]    // unknown product, the SDK says PacDrive
    public void IdentifiesUltimarcBoards(int pid, int type, string key) =>
        Assert.Equal(key, BoardModels.Identify(BoardModels.UltimarcVendorId, pid, type)!.Key);

    [Fact]
    public void UhidAndServoStikAreNotLedBoards()
    {
        Assert.Null(BoardModels.Identify(BoardModels.UltimarcVendorId, 0x1700, 2));
        Assert.Null(BoardModels.Identify(BoardModels.UltimarcVendorId, 0x1700, 7));
    }

    [Theory]
    [InlineData(0xFAFA, 0x00F0, "LEDWiz#1")]
    [InlineData(0xFAFA, 0x00F7, "LEDWiz#8")]   // a Pinscape's default unit
    [InlineData(0xFAFA, 0x00FF, "LEDWiz#16")]
    public void IdentifiesLedWizUnits(int vid, int pid, string key) =>
        Assert.Equal(key, BoardModels.IdentifyLedWiz(vid, pid)!.Key);

    [Theory]
    [InlineData(0xFAFA, 0x0100)]
    [InlineData(0xD209, 0x00F0)]
    public void IgnoresOtherHidDevices(int vid, int pid) => Assert.Null(BoardModels.IdentifyLedWiz(vid, pid));

    [Fact]
    public void LedWizFrame_IsSbaThenFourPbaChunks()
    {
        var pins = new byte[32];
        pins[0] = 255; pins[1] = 128; pins[8] = 1; pins[31] = 255;
        var reports = LedWizProtocol.Frame(pins);

        Assert.Equal(5, reports.Length);
        Assert.All(reports, r => Assert.Equal(8, r.Length));
        Assert.Equal(new byte[] { 64, 255, 255, 255, 255, LedWizProtocol.PulseSpeed, 0, 0 }, reports[0]);
        Assert.Equal(48, reports[1][0]);
        Assert.Equal(24, reports[1][1]);
        Assert.Equal(0, reports[2][0]);          // 1/255 rounds to dark
        Assert.Equal(48, reports[4][7]);
        // No PBA chunk can look like an SBA command, and none uses the board's own flash modes (49+).
        Assert.All(reports.Skip(1).SelectMany(r => r), v => Assert.InRange(v, 0, 48));
    }

    [Fact]
    public void AdalightFrame_HasHeaderCountChecksumAndRgb()
    {
        var rgb = new byte[] { 1, 2, 3, 4, 5, 6 };
        var frame = AdalightProtocol.Frame(rgb, 2);
        Assert.Equal(new byte[] { (byte)'A', (byte)'d', (byte)'a', 0, 1, 0 ^ 1 ^ 0x55, 1, 2, 3, 4, 5, 6 }, frame);

        var big = AdalightProtocol.Frame(new byte[300 * 3], 300); // 299 = 0x012B
        Assert.Equal(0x01, big[3]);
        Assert.Equal(0x2B, big[4]);
        Assert.Equal(0x01 ^ 0x2B ^ 0x55, big[5]);
        Assert.Equal(6 + 900, big.Length);
    }

    [Fact]
    public void PacDriveStates_AreOnAtAQuarterAndAbove()
    {
        var pins = new byte[16];
        pins[0] = 255; pins[1] = 64; pins[2] = 63; pins[15] = 200;
        Assert.Equal(0b1000_0000_0000_0011, PacDriveProtocol.States(pins));
    }

    [Fact]
    public void AddStrip_SetsLedsPortAndPins()
    {
        var wiring = new WiringMap();
        var def = BoardModels.Add(wiring, BoardModels.LedStrip, 1, leds: 10, serialPort: " COM5 ", baudRate: 230400)!;
        Assert.Equal("LedStrip#1", def.Key);
        Assert.Equal(10, def.LedCount);
        Assert.Equal(30, def.PinCount);
        Assert.Equal("COM5", def.SerialPort);
        Assert.Equal(230400, def.BaudRate);
        Assert.NotNull(def.Segments);
        Assert.Equal("LED 1 R", BoardModels.PinName(def, 1));
        Assert.Equal("LED 10 B", BoardModels.PinName(def, 30));

        var pac = BoardModels.Add(wiring, BoardModels.PacLed64, 1)!;
        Assert.Null(pac.LedCount);
        Assert.Equal("Pin 7", BoardModels.PinName(pac, 7));
    }

    [Fact]
    public void ShrinkingAStrip_UnwiresPinsAndTrimsRuns()
    {
        var wiring = new WiringMap();
        var def = BoardModels.Add(wiring, BoardModels.LedStrip, 1, leds: 10)!;
        wiring.Connect("A", ColorChannel.R, def.Key, 1);
        wiring.Connect("B", ColorChannel.R, def.Key, 28);
        def.Segments!.Add(new StripSegment { ButtonId = "C", FirstLed = 4, Count = 6 });
        def.Segments.Add(new StripSegment { ButtonId = "D", FirstLed = 8, Count = 2 });

        Assert.Equal(1, BoardModels.SetStripLeds(wiring, def, 5));
        Assert.Equal(15, def.PinCount);
        Assert.Single(wiring.Bindings);
        var run = Assert.Single(def.Segments);
        Assert.Equal((4, 2), (run.FirstLed, run.Count));
    }

    private static CabinetConfig StripCabinet()
    {
        var c = new CabinetConfig();
        c.Panel.Buttons.Add(new PanelButton { Id = "MARQUEE", Kind = ControlKind.Other });
        c.Panel.Buttons.Add(new PanelButton { Id = "P1_B1" });
        var strip = BoardModels.Add(c.Wiring, BoardModels.LedStrip, 1, leds: 10, serialPort: "COM3")!;
        strip.Segments!.Add(new StripSegment { ButtonId = "MARQUEE", FirstLed = 3, Count = 4 });
        c.Wiring.Connect("P1_B1", ColorChannel.R, strip.Key, 1);
        c.Wiring.Connect("P1_B1", ColorChannel.G, strip.Key, 2);
        c.Wiring.Connect("P1_B1", ColorChannel.B, strip.Key, 3);
        return c;
    }

    [Fact]
    public void StripRun_LightsEveryLedInItWithTheControlsColour()
    {
        using var engine = new LedEngine(StripCabinet()) { Source = new SolidColor(new Rgb(10, 20, 30)) };
        var pins = engine.RenderFrame(TimeSpan.Zero).BoardPins["LedStrip#1"];

        Assert.Equal(30, pins.Length);
        Assert.Equal(new byte[] { 10, 20, 30 }, pins[..3]);                  // LED 1: P1_B1, wired pin by pin
        Assert.Equal(new byte[] { 0, 0, 0 }, pins[3..6]);                    // LED 2: nothing
        for (int led = 3; led <= 6; led++) Assert.Equal(new byte[] { 10, 20, 30 }, pins[(led * 3 - 3)..(led * 3)]);
        Assert.All(pins[18..], v => Assert.Equal(0, v));                     // LEDs 7-10: nothing
    }

    [Fact]
    public void RenamingAndRemovingAControl_FollowsIntoStripRuns()
    {
        var c = StripCabinet();
        c.Wiring.RenameButton("MARQUEE", "TOP");
        Assert.Equal("TOP", c.Wiring.Boards[0].Segments![0].ButtonId);
        c.Wiring.RemoveButton("TOP");
        Assert.Empty(c.Wiring.Boards[0].Segments!);
    }

    [Fact]
    public void Config_KeepsStripSettings_AndLeavesThemOutForOtherBoards()
    {
        var c = StripCabinet();
        BoardModels.Add(c.Wiring, BoardModels.PacLed64, 1);
        var json = JsonSerializer.Serialize(c, ConfigStore.JsonOptions);
        var back = JsonSerializer.Deserialize<CabinetConfig>(json, ConfigStore.JsonOptions)!;

        var strip = back.Wiring.FindBoard("LedStrip#1")!;
        Assert.Equal((10, "COM3", 115200), (strip.LedCount!.Value, strip.SerialPort, strip.BaudRate!.Value));
        Assert.Equal("MARQUEE", Assert.Single(strip.Segments!).ButtonId);
        var pacJson = JsonSerializer.Serialize(back.Wiring.FindBoard("PacLED64#1"), ConfigStore.JsonOptions);
        Assert.DoesNotContain("ledCount", pacJson);
        Assert.DoesNotContain("segments", pacJson);
    }

    [Fact]
    public void Migrate_From13_FixesAHandEditedStrip()
    {
        var c = new CabinetConfig { Version = 13 };
        c.Wiring.Boards.Add(new BoardDefinition { Key = "LedStrip#1", Model = "LedStrip", PinCount = 31 });
        c.Wiring.Boards.Add(new BoardDefinition { Key = "PacLED64#1" });
        ConfigStore.Migrate(c);

        Assert.Equal(CabinetConfig.CurrentVersion, c.Version);
        var strip = c.Wiring.Boards[0];
        Assert.Equal((10, 30, 115200), (strip.LedCount!.Value, strip.PinCount, strip.BaudRate!.Value));
        Assert.NotNull(strip.Segments);
        Assert.Null(c.Wiring.Boards[1].LedCount);
        Assert.Equal(64, c.Wiring.Boards[1].PinCount);
    }
}
