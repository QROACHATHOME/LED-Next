using System.Collections.Concurrent;
using LedNext.Core.FrontEnd;
using LedNext.FrontEnd;

namespace LedNext.Tests;

public class FrontEndProtocolTests
{
    [Fact]
    public void RoundTripsAndRejectsJunk()
    {
        var m = new FrontEndMessage(FrontEndEvents.Select, "BigBox", "id-1", "Street Fighter II", "Arcade", @"..\roms\sf2.zip");
        var line = FrontEndProtocol.Serialize(m);
        Assert.DoesNotContain('\n', line);
        Assert.Contains("\"event\":\"select\"", line);
        Assert.Equal(m, FrontEndProtocol.Parse(line));

        Assert.Null(FrontEndProtocol.Parse(""));
        Assert.Null(FrontEndProtocol.Parse("not json"));
        Assert.Null(FrontEndProtocol.Parse("{\"title\":\"no event\"}"));
        Assert.Null(FrontEndProtocol.Parse(new string('x', FrontEndProtocol.MaxLineLength + 1)));
        // Unknown fields from a newer plugin are ignored.
        Assert.Equal(FrontEndEvents.Exit, FrontEndProtocol.Parse("{\"event\":\"exit\",\"future\":1}")!.Event);
    }
}

public class FrontEndSessionTests
{
    private static FrontEndMessage Sel(string title) => new(FrontEndEvents.Select, "LaunchBox", title, title, "Arcade");

    [Fact]
    public void BrowsePlayAndReturn()
    {
        var s = new FrontEndSession();
        var a = Assert.IsType<FrontEndAction.LightGame>(s.Handle(Sel("sf2")));
        Assert.Equal(("sf2", false), (a.Game.Title, a.Playing));

        var play = Assert.IsType<FrontEndAction.LightGame>(s.Handle(new FrontEndMessage(FrontEndEvents.Launch, "LaunchBox", "mk", "mk", "Arcade")));
        Assert.Equal(("mk", true), (play.Game.Title, play.Playing));

        // LaunchBox may still move its selection while the game runs: the panel stays on the game.
        Assert.Null(s.Handle(Sel("1942")));

        var back = Assert.IsType<FrontEndAction.LightGame>(s.Handle(new FrontEndMessage(FrontEndEvents.Exit, "LaunchBox")));
        Assert.Equal(("1942", false), (back.Game.Title, back.Playing));
    }

    [Fact]
    public void LaunchWithoutGameUsesSelectionAndClosingTurnsOff()
    {
        var s = new FrontEndSession();
        Assert.Null(s.Handle(new FrontEndMessage(FrontEndEvents.Select, "BigBox"))); // platform wheel, no game
        s.Handle(Sel("sf2"));
        var play = Assert.IsType<FrontEndAction.LightGame>(s.Handle(new FrontEndMessage(FrontEndEvents.Launch, "BigBox")));
        Assert.Equal("sf2", play.Game.Title);
        Assert.IsType<FrontEndAction.AllOff>(s.Handle(new FrontEndMessage(FrontEndEvents.Closing, "BigBox")));
        Assert.Null(s.Handle(new FrontEndMessage(FrontEndEvents.Exit, "BigBox")));
        Assert.IsType<FrontEndAction.ShowWindow>(s.Handle(new FrontEndMessage(FrontEndEvents.Show)));
        Assert.Null(s.Handle(new FrontEndMessage("something-new")));
    }
}

public class FrontEndPipeTests
{
    private static string UniquePipe() => $"LedNext.Test.{Guid.NewGuid():N}";

    private static void WaitFor(Func<bool> condition, int ms = 5000)
    {
        var until = DateTime.UtcNow.AddMilliseconds(ms);
        while (!condition() && DateTime.UtcNow < until) Thread.Sleep(20);
        Assert.True(condition(), "timed out");
    }

    [Fact]
    public void ClientMessagesReachTheServerInOrder()
    {
        var pipe = UniquePipe();
        var got = new ConcurrentQueue<FrontEndMessage>();
        using var server = new FrontEndServer(pipe);
        server.MessageReceived += got.Enqueue;
        server.Start();

        using var client = new FrontEndClient(pipe);
        client.Send(new FrontEndMessage(FrontEndEvents.Started, "LaunchBox"));
        for (int i = 0; i < 20; i++) client.Send(new FrontEndMessage(FrontEndEvents.Select, "LaunchBox", $"g{i}", $"Game {i}", "Arcade"));
        client.Send(new FrontEndMessage(FrontEndEvents.Launch, "LaunchBox", "g19", "Game 19", "Arcade"));
        client.Send(new FrontEndMessage(FrontEndEvents.Exit, "LaunchBox"));

        WaitFor(() => got.Any(m => m.Event == FrontEndEvents.Exit));
        var events = got.ToList();
        Assert.Equal(FrontEndEvents.Started, events[0].Event);
        // Fast scrolling may skip selections, but the last one always arrives, before the launch.
        var lastSelect = events.FindLastIndex(m => m.Event == FrontEndEvents.Select);
        Assert.Equal("Game 19", events[lastSelect].Title);
        Assert.True(lastSelect < events.FindIndex(m => m.Event == FrontEndEvents.Launch));
        Assert.True(events.Count(m => m.Event == FrontEndEvents.Select) <= 20);
    }

    [Fact]
    public void ClientWithoutServerDoesNotThrowAndReportsIt()
    {
        int failed = 0;
        using var client = new FrontEndClient(UniquePipe(), connectTimeoutMs: 50, retryAfter: TimeSpan.FromMilliseconds(50));
        client.ConnectFailed += () => Interlocked.Increment(ref failed);
        client.Send(new FrontEndMessage(FrontEndEvents.Select, "LaunchBox", "a", "A", "Arcade"));
        WaitFor(() => Volatile.Read(ref failed) > 0);
        Assert.False(client.IsConnected);
    }

    [Fact]
    public void ClientCatchesUpWhenTheServerStartsLater()
    {
        var pipe = UniquePipe();
        using var client = new FrontEndClient(pipe, connectTimeoutMs: 50, retryAfter: TimeSpan.FromMilliseconds(100));
        client.Send(new FrontEndMessage(FrontEndEvents.Select, "LaunchBox", "early", "Early", "Arcade"));
        client.Send(new FrontEndMessage(FrontEndEvents.Select, "LaunchBox", "later", "Later", "Arcade"));
        Thread.Sleep(300);

        var got = new ConcurrentQueue<FrontEndMessage>();
        using var server = new FrontEndServer(pipe);
        server.MessageReceived += got.Enqueue;
        server.Start();
        client.Send(new FrontEndMessage(FrontEndEvents.Launch, "LaunchBox", "later", "Later", "Arcade"));
        WaitFor(() => got.Any(m => m.Event == FrontEndEvents.Launch));
        // The selection made while LED-Next wasn't running is delivered (only the newest one).
        Assert.Equal(new[] { "Later" }, got.Where(m => m.Event == FrontEndEvents.Select).Select(m => m.Title));
    }

    [Fact]
    public void ASecondServerOnTheSamePipeFails()
    {
        var pipe = UniquePipe();
        using var first = new FrontEndServer(pipe);
        first.Start();
        using var second = new FrontEndServer(pipe);
        Assert.ThrowsAny<Exception>(() => second.Start());
    }
}
