namespace LedNext.Core.Shows;

public enum IdleChange { None, Start, Stop }

/// <summary>
/// Decides when the idle show starts and stops. Pure logic (the App feeds it the time of the last input once a
/// second), unit-tested:
/// <list type="bullet">
/// <item>It starts when nothing was pressed for <see cref="IdleSettings.After"/> and nothing blocks it (a game is
/// playing, a start-up or shut-down show runs, no front end is open when that is required, idle is off).</item>
/// <item>It stops on any newer input, or as soon as something blocks it.</item>
/// <item>After it stopped, the wait starts again from that input.</item>
/// </list>
/// </summary>
public sealed class IdleMonitor
{
    private TimeSpan _inputWhenStarted;

    public bool IsIdle { get; private set; }

    /// <param name="now">Any steady clock.</param>
    /// <param name="lastInput">When the last key, button, mouse move (or other activity) happened, on the same clock.</param>
    /// <param name="blockedBy">Why idle can't run now, or null.</param>
    public IdleChange Tick(TimeSpan now, TimeSpan lastInput, TimeSpan after, string? blockedBy)
    {
        if (IsIdle)
        {
            if (blockedBy is null && lastInput <= _inputWhenStarted) return IdleChange.None;
            IsIdle = false;
            return IdleChange.Stop;
        }
        if (blockedBy is not null || now - lastInput < after) return IdleChange.None;
        IsIdle = true;
        _inputWhenStarted = lastInput;
        return IdleChange.Start;
    }

    /// <summary>Forget the idle state (the show was replaced by something else).</summary>
    public void Reset() => IsIdle = false;
}
