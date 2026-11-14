using LedNext.Core;
using LedNext.Core.Import.LedBlinky;
using LedNext.Core.Lighting;

namespace LedNext.Tests;

public class UltimateIOTests
{
    [Theory]
    [InlineData(0xD209, 0x1401, 5, "PacLED64#1", null)]
    [InlineData(0xD209, 0x1404, 5, "PacLED64#4", null)]
    [InlineData(0xD209, 0x0410, 6, "UltimateIO#1", "keyboard")]
    [InlineData(0xD209, 0x0411, 6, "UltimateIO#2", "keyboard")]     // "ID #2" firmware
    [InlineData(0xD209, 0x0412, 6, "UltimateIO#1", "DirectInput")]
    [InlineData(0xD209, 0x0413, 6, "UltimateIO#2", "DirectInput")]
    [InlineData(0xD209, 0x0420, 6, "UltimateIO#1", "unknown product 0420")] // the SDK says Ultimate I/O
    public void IdentifiesUltimarcLedBoardsFromUsbIds(int vid, int pid, int type, string key, string? mode)
    {
        var found = BoardModels.Identify(vid, pid, type);
        Assert.NotNull(found);
        Assert.Equal(key, found!.Key);
        Assert.Equal(mode, found.Mode);
        Assert.Equal(key.StartsWith("Ultimate") ? 96 : 64, found.Model.PinCount);
    }

    [Theory]
    [InlineData(0xD209, 0x1405, 5)]   // PacLED64 IDs stop at 4
    [InlineData(0xD209, 0x0001, 1)]   // a PacDrive
    [InlineData(0x045E, 0x028E, -1)]  // an Ultimate I/O in XInput mode: an Xbox 360 pad, no LEDs
    public void IgnoresOtherDevices(int vid, int pid, int type) => Assert.Null(BoardModels.Identify(vid, pid, type));

    [Fact]
    public void AddsAndRemovesBoards()
    {
        var config = Defaults.CreateFourPlayerCabinet();
        var wiring = config.Wiring;
        var def = BoardModels.Add(wiring, BoardModels.UltimateIO, 1);
        Assert.NotNull(def);
        Assert.Equal("UltimateIO#1", def!.Key);
        Assert.Equal("Ultimate I/O #1", def.Name);
        Assert.Equal(96, def.PinCount);
        Assert.True(def.GraphX > wiring.Boards.Where(b => b != def).Max(b => b.GraphX)); // placed to the right
        Assert.Null(BoardModels.Add(wiring, BoardModels.UltimateIO, 1));                // already there
        Assert.Throws<ArgumentOutOfRangeException>(() => BoardModels.Add(wiring, BoardModels.UltimateIO, 3));

        config.Panel.Buttons.Add(new PanelButton { Id = "X1", Player = 1 });
        wiring.Connect("X1", ColorChannel.R, "UltimateIO#1", 96);                        // the last pin exists
        Assert.Throws<ArgumentOutOfRangeException>(() => wiring.Connect("X1", ColorChannel.G, "UltimateIO#1", 97));
        Assert.Equal(1, BoardModels.Remove(wiring, "UltimateIO#1"));
        Assert.Null(wiring.FindBoard("UltimateIO#1"));
        Assert.Null(wiring.Find("X1", ColorChannel.R));
    }

    [Fact]
    public void AnUltimateIODrivesThirtyTwoRgbButtons()
    {
        var config = new CabinetConfig();
        BoardModels.Add(config.Wiring, BoardModels.UltimateIO, 1);
        for (int i = 1; i <= 33; i++) config.Panel.Buttons.Add(new PanelButton { Id = $"B{i}", Player = 1 });
        Assert.Equal(1, config.Wiring.AutoWire(config.Panel));                           // 32 fit, the 33rd doesn't
        Assert.Equal(96, config.Wiring.Find("B32", ColorChannel.B)!.Pin);

        var engine = new LedEngine(config);
        engine.Source = new SolidColor(new Rgb(10, 20, 30));
        var frame = engine.RenderFrame(TimeSpan.Zero);
        var pins = frame.BoardPins["UltimateIO#1"];
        Assert.Equal(96, pins.Length);
        Assert.True(pins[93] > 0 && pins[95] > 0);                                      // B32's R and B are lit
    }

    [Theory]
    [InlineData("PACLED64", 2, "PacLED64#2")]
    [InlineData("IPAC ULTIMATE IO", 1, "UltimateIO#1")]
    [InlineData("Ultimate I/O", 2, "UltimateIO#2")]
    [InlineData("NANOLED", 1, null)]
    public void MapsLedBlinkyControllersToBoards(string name, int id, string? key) =>
        Assert.Equal(key, ControlMapBuilder.BoardKeyFor(new InputMapPort(id, name, 1, "P1B1", "R", Array.Empty<string>())));
}
