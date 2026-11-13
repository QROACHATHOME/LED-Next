using LedNext.Core;
using LedNext.Core.Lighting;
using LedNext.Core.Outputs;

namespace LedNext.Tests;

/// <summary>The rules of <see cref="LampLifecycle"/> (TASKS/DESIGN_LAMP_STATES.md), one test each.</summary>
public class LampLifecycleTests
{
    private const string Net = "network port 8000", Win = "Windows messages";
    private static readonly (string, bool)[] StartOn = { ("P1_START", true) }, StartOff = { ("P1_START", false) };

    private static LampLifecycle New() => new(b => new LampOverlay(b, _ => Array.Empty<string>(), Rgb.White, false));
    private static ILightSource Src(string name) => new SolidColor(Rgb.White, name);
    private static bool Yes(ILightSource _) => true;

    [Fact]
    public void LaunchedGameGetsLampsOnTheFrontEndsLightsAndGoesBackOnExit()
    {
        var life = New();
        var selected = Src("selected galaga");
        var playing = Src("playing galaga");
        Assert.Same(selected, life.FrontEndSelection(selected).Show);

        Assert.Same(playing, life.FrontEndLaunch(playing).Show);
        Assert.Equal(LampPhase.Launching, life.Phase);

        // MAME reports it: the front end's lights already show, nothing to set.
        Assert.Null(life.GameStarted("galaga", Net, playing, Src("rom galaga"), Yes).Show);
        Assert.Equal(LampPhase.Playing, life.Phase);

        var first = life.Lamps(StartOn);
        var overlay = Assert.IsType<LampOverlay>(first.Show);
        Assert.Same(playing, overlay.Base);
        Assert.Equal(LampPhase.PlayingWithLamps, life.Phase);
        Assert.Equal(new LampStep(null, Redraw: true), life.Lamps(StartOff)); // same overlay, just redraw

        // MAME stops: the launch lights stay until the front end's exit.
        Assert.Same(playing, life.GameEnded(Net).Show);
        Assert.Equal(LampPhase.Launching, life.Phase);
        Assert.Null(life.Game);

        var back = Src("selected galaga again");
        Assert.Same(back, life.FrontEndExit(back).Show);
        Assert.Equal(LampPhase.Browsing, life.Phase);
        Assert.False(life.Launched);
    }

    [Fact]
    public void FrontEndExitEndsTheGameEvenWithoutAStopFromTheEmulator()
    {
        var life = New();
        var playing = Src("playing marine fishing");
        life.FrontEndLaunch(playing);
        life.GameStarted("SEGA MARINE FISHING JAPAN", Net, playing, null, Yes);
        Assert.IsType<LampOverlay>(life.Lamps(StartOn).Show);

        var back = Src("selection");
        Assert.Same(back, life.FrontEndExit(back).Show);   // lamps cleared: the selection, no overlay
        Assert.Equal(LampPhase.Browsing, life.Phase);
        Assert.Null(life.Overlay);

        // Late outputs and a late stop of the ended game change nothing.
        Assert.Equal(LampStep.None, life.Lamps(StartOn));
        Assert.Equal(LampStep.None, life.GameEnded(Net));
    }

    [Fact]
    public void FrontEndClosingEndsTheGameAndTurnsAllOff()
    {
        var life = New();
        var playing = Src("playing");
        life.FrontEndLaunch(playing);
        life.GameStarted("galaga", Win, playing, null, Yes);
        life.Lamps(StartOn);
        var off = new AllOff();
        Assert.Same(off, life.FrontEndClosed(off).Show);
        Assert.Equal(LampPhase.Browsing, life.Phase);
        Assert.Null(life.Game);
    }

    [Fact]
    public void AGameStartedByHandShowsTheRomsLightsAndKeepsThePanelFromSelections()
    {
        var life = New();
        var before = Src("games tab preview");
        var rom = Src("rom dkong");
        Assert.Same(rom, life.GameStarted("dkong", Win, before, rom, Yes).Show);
        Assert.Equal(LampPhase.Playing, life.Phase);

        // BigBox selections while the game runs are remembered, not shown.
        var selection = Src("selection");
        Assert.Equal(LampStep.None, life.FrontEndSelection(selection));

        Assert.Same(rom, Assert.IsType<LampOverlay>(life.Lamps(StartOn).Show).Base);
        Assert.Same(selection, life.GameEnded(Win).Show); // back to the latest selection
        Assert.Equal(LampPhase.Browsing, life.Phase);
    }

    [Fact]
    public void AnUnknownRomStartedByHandKeepsTheCurrentLights()
    {
        var life = New();
        var current = Src("current");
        life.FrontEndSelection(current);
        Assert.Equal(LampStep.None, life.GameStarted("unknownrom", Net, current, null, Yes)); // already shown
        Assert.Same(current, Assert.IsType<LampOverlay>(life.Lamps(StartOn).Show).Base);
        Assert.Same(current, life.GameEnded(Net).Show);
    }

    [Fact]
    public void TheFirstAdapterToReportAGameOwnsIt()
    {
        var life = New();
        var playing = Src("playing");
        life.FrontEndLaunch(playing);
        life.GameStarted("galaga", Net, playing, null, Yes);
        Assert.Equal(LampStep.None, life.GameStarted("galaga", Win, playing, null, Yes));
        Assert.Equal(Net, life.Via);
        Assert.Equal(LampStep.None, life.GameEnded(Win)); // not the owner
        Assert.Equal(LampPhase.Playing, life.Phase);
        life.GameEnded(Net);
        Assert.Null(life.Via);
    }

    [Fact]
    public void ANameMismatchKeepsTheFrontEndsLights()
    {
        var life = New();
        var playing = Src("playing Outrun 2 SP (TeknoParrot)");
        life.FrontEndLaunch(playing);
        Assert.Equal(LampStep.None, life.GameStarted("or2sp", Net, playing, Src("rom or2sp"), _ => false));
        Assert.Same(playing, Assert.IsType<LampOverlay>(life.Lamps(StartOn).Show).Base);
    }

    [Fact]
    public void TheUsersOwnPickWinsUntilTheNextFrontEndEvent()
    {
        var life = New();
        var playing = Src("playing");
        life.FrontEndLaunch(playing);
        life.GameStarted("galaga", Net, playing, null, Yes);
        var overlay = life.Lamps(StartOn).Show!;
        life.SourceShown(overlay);           // the machine's own choice: no override
        Assert.False(life.UserOverride);

        life.SourceShown(Src("RGB cycle")); // a test pattern picked in LED-Next
        Assert.True(life.UserOverride);
        Assert.Equal(LampStep.None, life.Lamps(StartOff)); // lamps don't take the panel back
        Assert.Equal(LampStep.None, life.GameEnded(Net));  // nor does the end
        var back = Src("selection");
        Assert.Same(back, life.FrontEndExit(back).Show);   // the front end does
        Assert.False(life.UserOverride);
    }

    [Fact]
    public void PicksWhileBrowsingAreNotAnOverride()
    {
        var life = New();
        life.FrontEndSelection(Src("selection"));
        life.SourceShown(Src("RGB cycle"));
        Assert.False(life.UserOverride);
    }

    [Fact]
    public void ANewGameFromTheSameAdapterReplacesTheOldOne()
    {
        var life = New();
        var current = Src("current");
        life.GameStarted("galaga", Net, current, null, Yes);
        life.Lamps(StartOn);
        var digdug = Src("rom digdug");
        Assert.Same(digdug, life.GameStarted("digdug", Net, current, digdug, Yes).Show);
        Assert.Equal("digdug", life.Game);
        Assert.Null(life.Overlay); // galaga's lamps are gone
    }
}
