using LedNext.FrontEnd;

namespace LedNext.Core.FrontEnd;

/// <summary>What the panel should show after a front-end event.</summary>
public abstract record FrontEndAction
{
    /// <summary>Light this game (browsing or playing).</summary>
    public sealed record LightGame(FrontEndMessage Game, bool Playing) : FrontEndAction;
    /// <summary>A platform is highlighted (no game): light that platform's default.</summary>
    public sealed record LightPlatform(FrontEndMessage Selection) : FrontEndAction;
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
/// <item>a select with only a platform (platform wheel) → that platform's default lights</item>
/// <item>closing → all off; a select with neither a game nor a platform (a playlist) keeps the current lights</item>
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
                if (!m.HasGame && !m.HasPlatform) return null;
                Selected = m;
                if (Playing is not null) return null;
                return ForSelection(m);

            case FrontEndEvents.Launch:
                if (!m.HasGame && Selected is { HasGame: true } selectedGame) m = selectedGame;
                if (!m.HasGame) return null;
                Playing = m;
                return new FrontEndAction.LightGame(m, Playing: true);

            case FrontEndEvents.Exit:
                Playing = null;
                return Selected is { } back ? ForSelection(back) : null;

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

    private static FrontEndAction ForSelection(FrontEndMessage m) =>
        m.HasGame ? new FrontEndAction.LightGame(m, Playing: false) : new FrontEndAction.LightPlatform(m);
}
