namespace LedNext.Core;

/// <summary>
/// Snapshot-based undo/redo. States are opaque strings (serialized config).
/// Checkpoints with the same coalesce key within <see cref="CoalesceWindow"/> are merged,
/// so typing a label or dragging a slider is one undo step.
/// </summary>
public sealed class UndoHistory(int capacity = 100)
{
    private readonly LinkedList<string> _undo = new();
    private readonly Stack<string> _redo = new();
    private string? _lastKey;
    private DateTime _lastTime;

    public TimeSpan CoalesceWindow { get; set; } = TimeSpan.FromSeconds(1.5);
    public Func<DateTime> Clock { get; set; } = () => DateTime.UtcNow;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    /// <summary>Records <paramref name="stateBeforeChange"/>. Call just before mutating.</summary>
    public void Checkpoint(string stateBeforeChange, string? coalesceKey = null)
    {
        var now = Clock();
        if (coalesceKey is not null && coalesceKey == _lastKey && now - _lastTime < CoalesceWindow)
        {
            _lastTime = now;
            return;
        }
        _lastKey = coalesceKey;
        _lastTime = now;

        if (_undo.Last?.Value == stateBeforeChange) return;
        _undo.AddLast(stateBeforeChange);
        while (_undo.Count > capacity) _undo.RemoveFirst();
        _redo.Clear();
    }

    /// <returns>The state to restore, or null if there is nothing to undo.</returns>
    public string? Undo(string currentState)
    {
        if (_undo.Count == 0) return null;
        var state = _undo.Last!.Value;
        _undo.RemoveLast();
        _redo.Push(currentState);
        _lastKey = null;
        return state;
    }

    public string? Redo(string currentState)
    {
        if (_redo.Count == 0) return null;
        var state = _redo.Pop();
        _undo.AddLast(currentState);
        _lastKey = null;
        return state;
    }
}
