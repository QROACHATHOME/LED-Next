using LedNext.Core;
using LedNext.Core.Games;
using LedNext.Core.Import;
using LedNext.Core.Lighting;
using LedNext.Core.Speech;

namespace LedNext.Tests;

public class SpeechTests
{
    private static ResolvedControl C(string control, string? label, params string[] buttons) =>
        new(control, label, Rgb.White, null, false, buttons, "name");

    private static ResolvedLights Lights(params ResolvedControl[] controls) =>
        new("MAME", "1942", "game 1942", controls, new Dictionary<string, Rgb>());

    [Fact]
    public void SaysTheNameThenEachLabelOnceInPlayerOrder()
    {
        var lights = Lights(
            C("P2_BUTTON1", "Fire", "P2_B1"),
            C("P1_BUTTON2", "Loop", "P1_B2"),
            C("P1_BUTTON1", "fire", "P1_B1"),
            C("P1_BUTTON3", null, "P1_B3"),             // no label: skipped
            C("P1_BUTTON4", "Bomb"),                     // not on this panel: skipped
            C("P1_JOYSTICK", "Move", "P1_STICK"),        // a joystick: only with SpeakJoysticks
            C("P1_START", "1 Player", "P1_START"),
            C("P2_START", "2 Players", "P2_START"));
        var items = SpeechPlan.For(lights, new SpeechSettings(), "1942");
        Assert.Equal(new[] { "1942", "Loop", "fire" }, items.Select(i => i.Text));
        Assert.Empty(items[0].Buttons);
        Assert.Equal(new[] { "P1_B1", "P2_B1" }, items[2].Buttons.OrderBy(b => b));   // "Fire" on both players, said once

        var all = SpeechPlan.For(lights, new SpeechSettings { SpeakGameName = false, SpeakJoysticks = true, SpeakStartCoin = true, Prefix = "Controls" }, "1942");
        Assert.Equal(new[] { "Controls", "Loop", "fire", "Move", "Start Game" }, all.Select(i => i.Text));
        Assert.Equal(new[] { "P1_START", "P2_START" }, all[^1].Buttons);
    }

    [Fact]
    public void PausesOnlyAfterTheGamesName()
    {
        var lights = Lights(C("P1_BUTTON1", "Fire", "P1_B1"), C("P1_BUTTON2", "Loop", "P1_B2"));
        var items = SpeechPlan.For(lights, new SpeechSettings { PauseAfterNameSeconds = 1.5, Prefix = "Controls" }, "1942");
        Assert.Equal(new[] { "1942", "Controls", "Fire", "Loop" }, items.Select(i => i.Text));
        Assert.Equal(TimeSpan.FromSeconds(1.5), items[0].PauseAfter);
        Assert.All(items.Skip(1), i => Assert.Equal(TimeSpan.Zero, i.PauseAfter));

        Assert.All(SpeechPlan.For(lights, new SpeechSettings(), "1942"), i => Assert.Equal(TimeSpan.Zero, i.PauseAfter)); // default: as before
        Assert.Equal(TimeSpan.FromSeconds(10), new SpeechSettings { PauseAfterNameSeconds = 99 }.PauseAfterName);      // clamped
        Assert.Equal(2.0, SpeechSettings.FromLedBlinky(IniFile.Parse(Array.Empty<string>()),new SpeechSettings { PauseAfterNameSeconds = 2 }).PauseAfterNameSeconds); // kept
    }

    [Fact]
    public void AGameWithoutLabelsSaysNothing()
    {
        var items = SpeechPlan.For(Lights(C("P1_BUTTON1", null, "P1_B1")), new SpeechSettings(), "Galaga");
        Assert.Empty(items); // not even the name
    }

    [Fact]
    public void CopiesLedBlinkysSpeechSettings()
    {
        var ini = IniFile.Parse(new[]
        {
            "[GameOptions]", "GameStartSpeakControls=1", "GameStartSpeakName=0", "GameStartSpeakStartCoin=1",
            "GameStartStrobeSpeech=1", "GamePauseSpeakControls=0", "AbortSpeechAnyKey=0", "SpeakControlsPrefix=",
            "SpeakStartButton=Press Start", "SpeakCoinButton=Insert Coin",
            "[Speech]", "SpeechRate=2", "SpeechVolume=80", "Voice=Microsoft David Desktop - English (United States)",
            "[OtherSettings]", "DoNotSpeakJoystickActions=1",
        });
        var s = SpeechSettings.FromLedBlinky(ini, new SpeechSettings { StopOnInput = true, DelaySeconds = 2 });
        Assert.True(s.Enabled);
        Assert.False(s.SpeakGameName);
        Assert.True(s.SpeakStartCoin);
        Assert.True(s.BlinkButtons);
        Assert.False(s.SpeakOnPause);
        Assert.False(s.SpeakJoysticks);
        Assert.True(s.StopOnInput);           // LED-Next's own choice is kept
        Assert.Equal(2, s.DelaySeconds);
        Assert.Equal(2, s.Rate);
        Assert.Equal(80, s.Volume);
        Assert.Equal("Microsoft David Desktop - English (United States)", s.Voice);
        Assert.Null(s.Prefix);
        Assert.Equal("Press Start", s.StartText);
    }

    [LedBlinkySamplesFact]
    public void ReadsTheCabinetsLedBlinkySettings()
    {
        var path = Path.Combine(LedBlinkyImportTests.RepoRoot(), "import-samples", "ledblinky", "settings.ini");
        var s = SpeechSettings.FromLedBlinky(IniFile.Load(path));
        Assert.True(s.Enabled);
        Assert.True(s.BlinkButtons);
        Assert.Equal("Microsoft David Desktop - English (United States)", s.Voice);
    }

    [Fact]
    public void TheHighlightBlinksOnTopOfTheSourceWithoutReplacingIt()
    {
        var config = new CabinetConfig();
        config.Wiring.Boards.Add(new BoardDefinition { Key = "B", Name = "B" });
        int pin = 1;
        foreach (var id in new[] { "P1_B1", "P1_B2" })
        {
            config.Panel.Buttons.Add(new PanelButton { Id = id, Player = 1 });
            foreach (var ch in new[] { ColorChannel.R, ColorChannel.G, ColorChannel.B }) config.Wiring.Connect(id, ch, "B", pin++);
        }
        var engine = new LedEngine(config);
        var red = new Rgb(255, 0, 0);
        var source = new SolidColor(red);
        engine.Source = source;
        var hl = new SpeechHighlight();
        engine.Highlight = hl;
        hl.Blink(new[] { "P1_B1" });

        var t0 = TimeSpan.FromSeconds(10);
        var on = engine.RenderFrame(t0);
        var off = engine.RenderFrame(t0 + SpeechHighlight.HalfPeriod);
        var onAgain = engine.RenderFrame(t0 + SpeechHighlight.HalfPeriod * 2);
        Assert.Equal(red, on.ButtonColors["P1_B1"]);      // its own colour while lit
        Assert.Equal(Rgb.Black, off.ButtonColors["P1_B1"]);
        Assert.Equal(red, onAgain.ButtonColors["P1_B1"]);
        Assert.Equal(red, off.ButtonColors["P1_B2"]);     // other buttons unchanged
        Assert.Same(source, engine.Source);

        hl.Clear();
        Assert.Equal(red, engine.RenderFrame(t0 + SpeechHighlight.HalfPeriod * 3).ButtonColors["P1_B1"]);
    }
}
