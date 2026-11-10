using LedNext.FrontEnd;

namespace LedNext.Core.FrontEnd;

/// <summary>What the panel should show after a front-end event.</summary>
public abstract record FrontEndAction
{
    /// <summary>Light this game (browsing or playing).</summary>
    public sealed record LightGame(FrontEndMessage Game, bool Playing) : FrontEndAction;
    /// <summary>The front end closed: all off.</summary>
    public sealed record AllOff(string Reason) : FrontEndAction;
    /// <summary>Bring the LED-Next window forward.</summary>
    public sealed record ShowWindow : FrontEndAction;
}

/// <summary>
/// Turns front-end events into what to light. Pure logic (no UI, no boards), so it is unit-tested:
/// <list type="bullet">
/// <item>select → that game (ignored while a game is running: the front end may still move its selection)</item>
/// <item>launch → that game, playing; exit → back to the game selected before</item>
/// <item>closing → all off; a select without a game (platform wheel) keeps the current lights</item>
/// </list>
/// </summary>
public sealed class FrontEndSession
{
    public FrontEndMessage? Selected { get; private set; }
    public FrontEndMessage? Playing { get; private set; }

    /// <summary>The action to take, or null when the lights stay as they are.</summary>
    public FrontEndAction? Handle(FrontEndMessage m)
    {
        switch (m.Event)
        {
            case FrontEndEvents.Select:
                if (!m.HasGame) return null;
                Selected = m;
                if (Playing is not null) return null;
                return new FrontEndAction.LightGame(m, Playing: false);

            case FrontEndEvents.Launch:
                if (!m.HasGame) m = Selected ?? m;
                if (!m.HasGame) return null;
                Playing = m;
                return new FrontEndAction.LightGame(m, Playing: true);

            case FrontEndEvents.Exit:
                Playing = null;
                return Selected is { } back ? new FrontEndAction.LightGame(back, Playing: false) : null;

            case FrontEndEvents.Closing:
                Selected = null;
                Playing = null;
                return new FrontEndAction.AllOff($"{m.Source ?? "Front end"} closed");

            case FrontEndEvents.Show:
                return new FrontEndAction.ShowWindow();

            default:
                return null; // started, and anything newer than this build
        }
    }
}
