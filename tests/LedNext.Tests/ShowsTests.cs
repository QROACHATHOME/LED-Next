using LedNext.Core;
using LedNext.Core.Lighting;
using LedNext.Core.Outputs;
using LedNext.Core.Shows;
using Xunit;

namespace LedNext.Tests;

public class ShowsTests
{
    private static readonly PanelSnapshot Panel = PanelSnapshot.From(Defaults.CreateFourPlayerCabinet());

    private static Dictionary<string, Rgb> Render(ILightSource source, double seconds)
    {
        var ctx = new RenderContext(Panel, TimeSpan.FromSeconds(seconds));
        source.Render(ctx);
        return Panel.Buttons.ToDictionary(b => b.Id, b => ctx.ColorOf(b.Id) ?? Rgb.Black);
    }

    private static bool AnyLit(Dictionary<string, Rgb> colors) => colors.Values.Any(c => c.R + c.G + c.B > 30);

    [Fact]
    public void AVersion10ConfigGetsIdleAndShowsOff()
    {
        var config = new CabinetConfig { Version = 10 };
        config.Idle = null!;
        config.Shows = null!;
        ConfigStore.Migrate(config);
        Assert.Equal(CabinetConfig.CurrentVersion, config.Version);
        Assert.Equal(1, config.Idle.Speed);
        Assert.False(config.Idle.Enabled);
        Assert.False(config.Idle.ReactToAudio);
        Assert.Equal(LightShows.None, config.Shows.Startup);
        Assert.Equal(LightShows.None, config.Shows.Shutdown);
        Assert.Null(LightShows.Startup(config.Shows.Startup, config.Shows.StartupDuration));
        Assert.Null(LightShows.Shutdown(config.Shows.Shutdown, config.Shows.ShutdownDuration, null));
    }

    [Fact]
    public void SettingsRoundTripThroughJson()
    {
        var config = Defaults.CreateFourPlayerCabinet();
        config.Idle.Enabled = true;
        config.Idle.Show = "Fire";
        config.Shows.Startup = "Sweep on";
        var copy = ConfigStore.Clone(config);
        Assert.True(copy.Idle.Enabled);
        Assert.Equal("Fire", copy.Idle.Show);
        Assert.Equal("Sweep on", copy.Shows.Startup);
    }

    [Fact]
    public void EveryShowRendersAndLightsSomething()
    {
        var audio = new AudioEnvelope(new FixedAudioLevel(0.6f, 0.6f), 1);
        foreach (var info in LightShows.Loops)
        {
            var show = LightShows.Loop(info.Name, audio)!;
            Assert.NotNull(show);
            bool lit = false;
            for (double t = 0; t < 6; t += 0.37) lit |= AnyLit(Render(show, t));
            Assert.True(lit, $"{info.Name} never lit a button");
        }
        foreach (var info in LightShows.Intros)
        {
            var show = LightShows.Startup(info.Name, TimeSpan.FromSeconds(4))!;
            Assert.NotNull(show);
            Assert.True(AnyLit(Render(show, 0)) | AnyLit(Render(show, 3.5)), $"start-up {info.Name}");
        }
        foreach (var info in LightShows.Outros) Assert.NotNull(LightShows.Shutdown(info.Name, TimeSpan.FromSeconds(3), new SolidColor(Rgb.Red)));
    }

    [Fact]
    public void SoundShowsAreNotOfferedForStartupOrShutdown()
    {
        Assert.Contains(LightShows.Loops, l => l.NeedsAudio);
        Assert.DoesNotContain(LightShows.Intros, l => l.NeedsAudio);
        Assert.DoesNotContain(LightShows.Outros, l => l.NeedsAudio);
        Assert.Null(LightShows.Startup("Sound meter", TimeSpan.FromSeconds(3)));
    }

    [Fact]
    public void FadeOutStartsFromWhatWasShownAndEndsBlack()
    {
        var show = LightShows.Shutdown("Fade out", TimeSpan.FromSeconds(2), new SolidColor(Rgb.Red))!;
        var first = Render(show, 10);           // the show's time 0
        Assert.All(first.Values, c => Assert.Equal(Rgb.Red, c));
        var last = Render(show, 12);
        Assert.All(last.Values, c => Assert.Equal(Rgb.Black, c));
    }

    [Fact]
    public void SweepOffSwitchesTheRightSideOffFirst()
    {
        var show = LightShows.Shutdown("Sweep off", TimeSpan.FromSeconds(4), new SolidColor(Rgb.White))!;
        Render(show, 0);
        var mid = Render(show, 1.6);
        var right = Panel.Buttons.OrderByDescending(b => b.NormX).First();
        var left = Panel.Buttons.OrderBy(b => b.NormX).First();
        Assert.Equal(Rgb.Black, mid[right.Id]);
        Assert.Equal(Rgb.White, mid[left.Id]);
    }

    [Fact]
    public void AShutdownLoopFadesToBlackAtTheEnd()
    {
        var show = LightShows.Shutdown("Rainbow wave", TimeSpan.FromSeconds(3), null)!;
        Render(show, 0);
        Assert.True(AnyLit(Render(show, 1)));
        Assert.False(AnyLit(Render(show, 3)));
    }

    [Fact]
    public void RandomIdleChangesShowAfterTheIntervalWithACrossFade()
    {
        var settings = new IdleSettings { Show = LightShows.Random, ChangeEverySeconds = 10 };
        var idle = LightShows.Idle(settings, audio: null, seed: 1);
        Assert.Equal("Idle: random shows", idle.Name);
        Assert.DoesNotContain(idle.Names, n => LightShows.Loops.First(l => l.Name == n).NeedsAudio); // no sound: no sound shows
        Render(idle, 100);
        var first = idle.Current;
        Render(idle, 105);
        Assert.Equal(first, idle.Current);
        Render(idle, 110.1);
        Assert.NotEqual(first, idle.Current);
    }

    [Fact]
    public void HalfSpeedShowsAtTenSecondsWhatNormalSpeedShowsAtFive()
    {
        var slow = LightShows.Idle(new IdleSettings { Show = "Rainbow wave", Speed = 0.5 }, null, 1);
        var normal = LightShows.Idle(new IdleSettings { Show = "Rainbow wave" }, null, 1);
        Assert.Equal(Render(normal, 5), Render(slow, 10));
        Assert.NotEqual(Render(normal, 10), Render(slow, 10));
        Assert.Equal(0.25, new IdleSettings { Speed = 0 }.ClampedSpeed);
    }

    [Fact]
    public void TestPatternsAreMarkedSoIdleWaitsButOffIsNot()
    {
        // On the cabinet the idle show replaced the Boards tab's "All white" mid-test.
        ILightSource[] tests = [new SolidColor(Rgb.White), new ButtonChase(), new RgbCycle(), new Rainbow(), new PinWalk(),
            new SinglePin("PacLED64#1", "PacLED64 #1", 1), new IdentifyButton("P1_B1"), new ManualPins()];
        Assert.All(tests, t => Assert.IsAssignableFrom<ITestPattern>(t));
        Assert.False(new AllOff() is ITestPattern); // BigBox's "all off" must not block idle
    }

    [Fact]
    public void ANamedIdleShowStays()
    {
        var idle = LightShows.Idle(new IdleSettings { Show = "comet", ChangeEverySeconds = 5 }, null, 3);
        Render(idle, 0);
        Render(idle, 60);
        Assert.Equal("Comet", idle.Current);
        Assert.Equal("Idle: Comet", idle.Name);
    }

    [Fact]
    public void WithSoundTheSoundShowsJoinTheRandomPick()
    {
        var idle = LightShows.Idle(new IdleSettings(), new FixedAudioLevel(0, 0), 1);
        Assert.Contains("Sound meter", idle.Names);
        Assert.True(idle.ReactsToAudio);
    }

    [Fact]
    public void IdleShowFollowsTheSoundLevel()
    {
        var level = new FixedAudioLevel(0, 0);
        var idle = LightShows.Idle(new IdleSettings { Show = "Color fade" }, level, 1);
        var silent = Render(idle, 0);
        level.Left = level.Right = 0.1f; // quiet sound (×1.5 sensitivity)
        var quiet = Render(idle, 0.1);
        level.Left = level.Right = 1f;
        var loud = Render(idle, 0.2);
        int Sum(Dictionary<string, Rgb> c) => c.Values.Sum(x => x.R + x.G + x.B);
        Assert.True(Sum(quiet) < Sum(silent), "in silence the show is at full brightness; quiet sound dims it");
        Assert.True(Sum(loud) > Sum(quiet));
    }

    [Fact]
    public void SoundMeterLightsTheMiddleFirst()
    {
        var level = new FixedAudioLevel(0.3f, 0.3f);
        var meter = LightShows.Loop("Sound meter", new AudioEnvelope(level, 1))!;
        var colors = Render(meter, 1);
        var middle = Panel.Buttons.OrderBy(b => Math.Abs(b.NormX - 0.5)).First();
        var edge = Panel.Buttons.OrderByDescending(b => Math.Abs(b.NormX - 0.5)).First();
        Assert.True(colors[middle.Id].G > 200);
        Assert.True(colors[edge.Id].R + colors[edge.Id].G < 30);
    }

    [Fact]
    public void AudioEnvelopeRisesAtOnceFallsSlowlyAndFindsBeats()
    {
        var level = new FixedAudioLevel(0, 0);
        var env = new AudioEnvelope(level, 1);
        env.Update(TimeSpan.Zero);
        Assert.False(env.Active);
        for (int i = 1; i <= 30; i++) { level.Left = level.Right = 0.2f; env.Update(TimeSpan.FromSeconds(i * 0.033)); }
        Assert.True(env.Active);
        level.Left = level.Right = 0.9f;
        env.Update(TimeSpan.FromSeconds(1.1));
        Assert.Equal(0.9, env.Level, 3);
        Assert.True(env.Beat);
        level.Left = level.Right = 0f;
        env.Update(TimeSpan.FromSeconds(1.2));
        Assert.InRange(env.Level, 0.5, 0.7); // 0.9 - 3/s × 0.1 s
        Assert.False(env.Beat);
        env.Update(TimeSpan.FromSeconds(4));
        Assert.False(env.Active);
    }

    [Fact]
    public void AppliesToBigBoxAndOptionallyLaunchBox()
    {
        var s = new FrontEndShowSettings();
        Assert.True(s.AppliesTo("BigBox"));
        Assert.False(s.AppliesTo("LaunchBox"));
        s.AlsoLaunchBox = true;
        Assert.True(s.AppliesTo("LaunchBox"));
        Assert.False(s.AppliesTo("LED-Next"));
    }
}

public class IdleMonitorTests
{
    private static readonly TimeSpan After = TimeSpan.FromMinutes(5);
    private static TimeSpan M(double minutes) => TimeSpan.FromMinutes(minutes);

    [Fact]
    public void StartsAfterTheWaitAndStopsOnNewInput()
    {
        var m = new IdleMonitor();
        Assert.Equal(IdleChange.None, m.Tick(M(4.9), M(0), After, null));
        Assert.Equal(IdleChange.Start, m.Tick(M(5), M(0), After, null));
        Assert.Equal(IdleChange.None, m.Tick(M(20), M(0), After, null));
        Assert.Equal(IdleChange.Stop, m.Tick(M(21), M(20.9), After, null));
        Assert.False(m.IsIdle);
        Assert.Equal(IdleChange.None, m.Tick(M(25), M(20.9), After, null)); // waits again from that input
        Assert.Equal(IdleChange.Start, m.Tick(M(26), M(20.9), After, null));
    }

    [Fact]
    public void NeverStartsWhileBlockedAndStopsWhenBlocked()
    {
        var m = new IdleMonitor();
        Assert.Equal(IdleChange.None, m.Tick(M(30), M(0), After, "a game is playing"));
        Assert.Equal(IdleChange.Start, m.Tick(M(31), M(0), After, null));
        Assert.Equal(IdleChange.Stop, m.Tick(M(32), M(0), After, "a game is playing"));
    }

    [Fact]
    public void ResetForgetsTheShow()
    {
        var m = new IdleMonitor();
        m.Tick(M(10), M(0), After, null);
        m.Reset();
        Assert.False(m.IsIdle);
        Assert.Equal(IdleChange.Start, m.Tick(M(11), M(0), After, null));
    }

    [Fact]
    public void WindowsLastInputDoesNotWobbleBetweenTheTwoClocks()
    {
        // The cabinet's bug: the tick clock moves in 16 ms steps, the steady clock smoothly, so "now - age" moved
        // a few ms each second and stopped the idle show as if something was pressed.
        var clock = new LastInputClock();
        var m = new IdleMonitor();
        const uint input = 1_000_000;
        var first = clock.LastInput(M(1), input, input + 60_000);
        Assert.Equal(IdleChange.Start, m.Tick(M(1), first, TimeSpan.FromMinutes(1), null));
        for (int s = 1; s <= 30; s++)
        {
            var now = M(1) + TimeSpan.FromSeconds(s) + TimeSpan.FromMilliseconds(s % 3 * 7); // off the tick grid
            uint tick = input + 60_000 + (uint)(s * 1000) - (uint)(s % 2 * 15);
            Assert.Equal(IdleChange.None, m.Tick(now, clock.LastInput(now, input, tick), TimeSpan.FromMinutes(1), null));
        }
        // A real input (a new tick) stops it.
        var press = clock.LastInput(M(1.6), input + 95_000, input + 96_000);
        Assert.Equal(M(1.6) - TimeSpan.FromSeconds(1), press);
        Assert.Equal(IdleChange.Stop, m.Tick(M(1.6), press, TimeSpan.FromMinutes(1), null));
    }
}

public class KnownLampsTests
{
    [Fact]
    public void LogKeepsEachOutputsRangeAndChanges()
    {
        var log = new LampOutputLog("MAME", "galaga", "MameOutputs.ini");
        log.Add("led0", 0, new[] { "P1_START" });
        log.Add("led0", 1, new[] { "P1_START" });
        log.Add("led0", 1, new[] { "P1_START" });
        log.Add("led0", 0, new[] { "P1_START" });
        log.Add("pause", 0, Array.Empty<string>());
        var outputs = log.Outputs();
        Assert.Equal(2, outputs.Count);
        var led = outputs.Single(o => o.Output == "led0");
        Assert.Equal((0, 1, 2, "P1_START"), (led.Min, led.Max, led.Changes, led.MappedTo));
        Assert.Equal("", outputs.Single(o => o.Output == "pause").MappedTo);
    }

    [Fact]
    public void DatabaseAddsAndUpdatesKnownLamps()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lednext-lamps-{Guid.NewGuid():N}.db");
        try
        {
            var db = LedNext.Data.GameDatabase.Open(path);
            Assert.Equal(0, db.LampOutputCount());
            db.RecordLampOutputs("Flycast", "CRAZY TAXI", "cabinet.json",
                new[] { new LampOutputSeen("lamp7", "", 0, 1, 4) }, new DateTime(2026, 11, 14, 10, 0, 0, DateTimeKind.Utc));
            db.RecordLampOutputs("Flycast", "CRAZY TAXI", "cabinet.json",
                new[] { new LampOutputSeen("lamp7", "P1_START", 0, 3, 2), new LampOutputSeen("lamp0", "", 0, 0, 0) },
                new DateTime(2026, 11, 15, 10, 0, 0, DateTimeKind.Utc));
            db.RecordLampOutputs("MAME", "galaga", "MameOutputs.ini", new[] { new LampOutputSeen("led0", "P1_START", 0, 1, 9) });

            Assert.Equal(3, db.LampOutputCount());
            Assert.Equal(new[] { "Flycast", "MAME" }, db.LampEmulators());
            var lamp7 = db.ListLampOutputs("flycast", "lamp7").Single();
            Assert.Equal(("P1_START", 2, 0, 3, 6), (lamp7.MappedTo, lamp7.Sessions, lamp7.Min, lamp7.Max, lamp7.Changes));
            Assert.Equal(new DateTime(2026, 11, 14, 10, 0, 0, DateTimeKind.Utc), lamp7.FirstSeen.ToUniversalTime());
            Assert.Equal(new DateTime(2026, 11, 15, 10, 0, 0, DateTimeKind.Utc), lamp7.LastSeen.ToUniversalTime());
            Assert.Equal("lamp0", db.ListLampOutputs(unmappedOnly: true).Single().Output);
            Assert.Equal(2, db.ListLampOutputs(text: "taxi").Count);
            Assert.Empty(db.ListLampOutputs(text: "100%_")); // % and _ are matched literally
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }
}
