using LedNext.Core;

namespace LedNext.Tests;

public class WiringWizardTests
{
    private static WiringMap Boards(int pinsEach = 6)
    {
        var w = new WiringMap();
        w.Boards.Add(new BoardDefinition { Key = "A", Name = "Board A", PinCount = pinsEach });
        w.Boards.Add(new BoardDefinition { Key = "B", Name = "Board B", PinCount = pinsEach });
        return w;
    }

    [Fact]
    public void Assign_BindsCurrentPinAndAdvances()
    {
        var w = Boards();
        var wiz = new WiringWizard(w);

        wiz.Assign("P1_B1", ColorChannel.R);

        Assert.Equal(2, wiz.Pin);
        var b = Assert.Single(w.Bindings);
        Assert.Equal(("P1_B1", ColorChannel.R, "A", 1), (b.ButtonId, b.Channel, b.BoardKey, b.Pin));
    }

    [Fact]
    public void WalksAllBoardsThenFinishes()
    {
        var wiz = new WiringWizard(Boards(pinsEach: 2));
        wiz.Skip();
        wiz.Skip();
        Assert.Equal(("B", 1), (wiz.BoardKey, wiz.Pin));
        wiz.Skip();
        wiz.Skip();
        Assert.True(wiz.IsDone);
        Assert.Equal(4, wiz.Completed);
    }

    [Fact]
    public void ClearFirst_RemovesOnlySelectedBoards()
    {
        var w = Boards();
        w.Connect("X", ColorChannel.R, "A", 1);
        w.Connect("Y", ColorChannel.R, "B", 1);

        _ = new WiringWizard(w, new[] { "A" }, clearFirst: true);

        var left = Assert.Single(w.Bindings);
        Assert.Equal("B", left.BoardKey);
    }

    [Fact]
    public void Back_RestoresPreviousBindings()
    {
        var w = Boards();
        w.Connect("OLD", ColorChannel.G, "A", 1);    // pin 1 was wired to something else
        w.Connect("P1_B1", ColorChannel.R, "A", 5);  // the channel was on another pin
        var wiz = new WiringWizard(w);

        wiz.Assign("P1_B1", ColorChannel.R);
        Assert.True(wiz.Back());

        Assert.Equal(1, wiz.Pin);
        Assert.Equal("OLD", w.FindByPin("A", 1)!.ButtonId);
        Assert.Equal(5, w.Find("P1_B1", ColorChannel.R)!.Pin);
        Assert.Equal(2, w.Bindings.Count);
    }

    [Fact]
    public void Back_FromDoneReturnsToLastPin()
    {
        var wiz = new WiringWizard(Boards(pinsEach: 1));
        wiz.Skip();
        wiz.Skip();
        Assert.True(wiz.IsDone);
        wiz.Back();
        Assert.False(wiz.IsDone);
        Assert.Equal(("B", 1), (wiz.BoardKey, wiz.Pin));
    }

    [Fact]
    public void Suggest_FollowsLearnedChannelOrder()
    {
        var wiz = new WiringWizard(Boards(pinsEach: 12));
        // First button turns out to be wired G, R, B.
        Assert.Equal(ColorChannel.R, wiz.Suggest("P1_B1"));
        wiz.Assign("P1_B1", ColorChannel.G);
        wiz.Assign("P1_B1", ColorChannel.R);
        wiz.Assign("P1_B1", ColorChannel.B);
        Assert.Null(wiz.Suggest("P1_B1"));

        Assert.Equal(new[] { ColorChannel.G, ColorChannel.R, ColorChannel.B }, wiz.LearnedOrder);
        Assert.Equal(ColorChannel.G, wiz.Suggest("P1_B2"));
        wiz.Assign("P1_B2", ColorChannel.G);
        Assert.Equal(ColorChannel.R, wiz.Suggest("P1_B2"));
        Assert.Null(wiz.Suggest(null));
    }
}

public class UndoHistoryTests
{
    [Fact]
    public void UndoRedo_RoundTrips()
    {
        var h = new UndoHistory();
        h.Checkpoint("s0");          // state before change 1
        h.Checkpoint("s1");          // state before change 2; current is now s2

        Assert.Equal("s1", h.Undo("s2"));
        Assert.Equal("s0", h.Undo("s1"));
        Assert.Null(h.Undo("s0"));
        Assert.Equal("s1", h.Redo("s0"));
        Assert.Equal("s2", h.Redo("s1"));
        Assert.False(h.CanRedo);
    }

    [Fact]
    public void NewCheckpoint_ClearsRedo()
    {
        var h = new UndoHistory();
        h.Checkpoint("s0");
        h.Undo("s1");
        Assert.True(h.CanRedo);
        h.Checkpoint("s0");
        Assert.False(h.CanRedo);
    }

    [Fact]
    public void SameKeyWithinWindow_IsOneStep()
    {
        var now = new DateTime(2026, 1, 1);
        var h = new UndoHistory { Clock = () => now };
        h.Checkpoint("a", "label:P1");
        now = now.AddMilliseconds(300);
        h.Checkpoint("ab", "label:P1");
        now = now.AddSeconds(5);
        h.Checkpoint("abc", "label:P1");

        Assert.Equal("abc", h.Undo("abcd"));
        Assert.Equal("a", h.Undo("abc"));
        Assert.False(h.CanUndo);
    }

    [Fact]
    public void Capacity_DropsOldest()
    {
        var h = new UndoHistory(capacity: 2);
        h.Checkpoint("1");
        h.Checkpoint("2");
        h.Checkpoint("3");
        Assert.Equal("3", h.Undo("4"));
        Assert.Equal("2", h.Undo("3"));
        Assert.Null(h.Undo("2"));
    }
}

public class PanelIdTests
{
    [Theory]
    [InlineData("P1_B8", "P1_B9")]
    [InlineData("P1_B1", "P1_B3")] // P1_B2 is taken
    [InlineData("START", "START_2")]
    public void NextIdAfter_BumpsTrailingNumber(string source, string expected)
    {
        var panel = new PanelLayout();
        foreach (var id in new[] { "P1_B1", "P1_B2", "P1_B8", "START" }) panel.Buttons.Add(new PanelButton { Id = id });
        Assert.Equal(expected, panel.NextIdAfter(source));
    }
}
