using LedNext.Core.Outputs;

namespace LedNext.Tests;

public class EmulatorOutputsTests
{
    [Theory]
    [InlineData("mame.exe", false, "MAME", "MAME")]
    [InlineData("mame64", false, "MAME", "MAME")]
    [InlineData("groovymame", false, "MAME", "MAME")]
    [InlineData("arcade64.exe", false, "MAME", "MAME")]
    [InlineData("flycast", false, "Flycast", "Flycast")]
    [InlineData("odin.exe", false, "MAME", "Odin")]
    [InlineData("Keisho", false, "MAME", "Keisho")]
    [InlineData("Supermodel.exe", false, "MAME", "Supermodel")]
    [InlineData("emulator_multicpu.exe", true, "MAME", "Model 2")]            // M2Emulator with Output Blaster: still Model 2
    [InlineData("InitialD8_GLW_RE_SBZZ_dumped.exe", true, "MAME", "TeknoParrot")] // Output Blaster inside the game
    [InlineData("DemulShooter.exe", false, "MAME", "DemulShooter")]
    [InlineData("SomeOtherMameBuild", false, "MAME", "MAME")]
    [InlineData("unknown.exe", false, "MAME", "MAME")]                        // not known: the protocol's name, as before
    [InlineData(null, false, "Flycast", "Flycast")]                           // process not found
    public void NamesTheEmulatorFromItsProcess(string? exe, bool blaster, string protocol, string expected) =>
        Assert.Equal(expected, EmulatorNames.Identify(exe, blaster, protocol));

    [Fact]
    public void OnlyMameAndKeishoUseMameOutputsIni()
    {
        Assert.True(EmulatorNames.UsesMameOutputsIni("MAME"));
        Assert.True(EmulatorNames.UsesMameOutputsIni("keisho"));
        Assert.False(EmulatorNames.UsesMameOutputsIni("TeknoParrot"));
        Assert.False(EmulatorNames.UsesMameOutputsIni("Flycast"));
    }

    [Fact]
    public void BuiltInStartLampsComeAfterTheCabinetsOwnRows()
    {
        var chain = new[] { "braveff" };
        var odin = BuiltInLampMaps.For("Odin").For(chain);
        Assert.Equal(new[] { "P1_START" }, odin.ControlsFor("1p_start_lamp"));
        Assert.Equal(new[] { "P2_START" }, odin.ControlsFor("2p_start_lamp"));
        Assert.Empty(odin.ControlsFor("left_revolving_light"));

        Assert.Equal(new[] { "P1_START" }, BuiltInLampMaps.For("TeknoParrot").For(new[] { "Initial D8" }).ControlsFor("LampStart"));
        Assert.True(BuiltInLampMaps.For("Flycast").IsEmpty); // lamp bits differ per game
        Assert.True(BuiltInLampMaps.For("MAME").IsEmpty);    // MameOutputs.ini covers MAME

        var own = new LampMapTable("Odin", new[]
        {
            new LampMapEntry { Emulator = "Odin", Game = "braveff", Output = "1p_start_lamp", Controls = new() { "P1_BUTTON1" } },
        });
        var layered = OutputMaps.Layer(own, BuiltInLampMaps.For("Odin")).For(chain);
        Assert.Equal(new[] { "P1_BUTTON1" }, layered.ControlsFor("1p_start_lamp")); // the cabinet's row wins
        Assert.Equal(new[] { "P2_START" }, layered.ControlsFor("2p_start_lamp"));
    }

    [Fact]
    public void ReadsOdinKeishoAndOutputBlasterNetworkLines()
    {
        // Odin (outputs_network.cpp): MAME's dialect, ids are odin-games.json's.
        var odin = new OutputLineParser();
        Assert.Equal(new OutputLine.Start("braveff", "MAME"), odin.Feed("mame_start = braveff"));
        Assert.Equal(new OutputLine.Value("1p_start_lamp", 1), odin.Feed("1p_start_lamp = 1"));
        Assert.IsType<OutputLine.Stop>(odin.Feed("mame_stop = 1"));

        // Keisho (osd/outputs.cpp): MAME's names, pause, RawLamps as a byte.
        var keisho = new OutputLineParser();
        keisho.Feed("mame_start = daytona");
        Assert.Equal(new OutputLine.Value("RawLamps", 132), keisho.Feed("RawLamps = 132"));
        Assert.Equal(new OutputLine.Pause(true), keisho.Feed("pause = 1"));

        // Output Blaster (NetOutputs.cpp): decimal values, as MAME.
        var blaster = new OutputLineParser();
        blaster.Feed("mame_start = Initial D8");
        Assert.Equal("Initial D8", blaster.Game);
        Assert.Equal(new OutputLine.Value("LampStart", 1), blaster.Feed("LampStart = 1"));
    }

    [Fact]
    public void ReadsFlycastAsItsSourceSendsIt()
    {
        // core/network/output.h: "game = <gameId>\n" before the first output, values "%x", no stop or pause.
        var p = new OutputLineParser();
        Assert.Null(p.Feed("lamp0 = 1"));                                   // before the game line: dropped
        Assert.Equal(new OutputLine.Start("SEGA MARINE FISHING JAPAN", "Flycast"), p.Feed("game = SEGA MARINE FISHING JAPAN"));
        Assert.Equal(new OutputLine.Value("lamp7", 1), p.Feed("lamp7 = 1"));
        Assert.Equal(new OutputLine.Value("tachometer", 0x1a2), p.Feed("tachometer = 1a2"));
        Assert.Null(p.Feed("game = SEGA MARINE FISHING JAPAN"));            // sent again after a reset: same game
        Assert.Equal(new OutputLine.Value("m3ffb", -1), p.Feed("m3ffb = ffffffff")); // a 32-bit pattern: kept, not a crash
    }

    private static byte[] Reply(int id, string text, params byte[] after) =>
        BitConverter.GetBytes(id).Concat(System.Text.Encoding.Latin1.GetBytes(text)).Concat(new byte[] { 0 }).Concat(after).ToArray();

    [Fact]
    public void ReadsWindowsMessageNamesUpToTheirNul()
    {
        // MAME and Keisho: id, name, NUL and nothing more.
        Assert.Equal((0, "daytona93"), MameCopyData.ParseIdString(Reply(0, "daytona93")));
        // Supermodel and Output Blaster (visit 13): leftover bytes after the NUL ("scudplus do", "LampStart ÍÍ").
        Assert.Equal((0, "scudplus"), MameCopyData.ParseIdString(Reply(0, "scudplus", (byte)' ', (byte)'d', (byte)'o')));
        Assert.Equal((7, "LampStart"), MameCopyData.ParseIdString(Reply(7, "LampStart", 0xCD, 0xCD, 0xCD)));
        // No NUL at all: the whole block.
        Assert.Equal((3, "lamp0"), MameCopyData.ParseIdString(BitConverter.GetBytes(3).Concat("lamp0"u8.ToArray()).ToArray()));
        Assert.Null(MameCopyData.ParseIdString(new byte[] { 1, 0 }));
    }
}
