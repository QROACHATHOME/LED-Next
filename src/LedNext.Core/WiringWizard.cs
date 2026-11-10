namespace LedNext.Core;

/// <summary>
/// Guided wiring: lights one pin at a time; the user says which button and colour lit up.
/// Works directly on a <see cref="WiringMap"/>. UI-independent so it can be unit-tested.
/// </summary>
public sealed class WiringWizard
{
    private static readonly ColorChannel[] DefaultOrder = { ColorChannel.R, ColorChannel.G, ColorChannel.B };

    private readonly WiringMap _wiring;
    private readonly List<BoardDefinition> _boards;
    private readonly Stack<Step> _history = new();
    private readonly Dictionary<string, List<ColorChannel>> _assignedThisSession = new();
    private int _boardIndex;

    private sealed record Step(string BoardKey, int Pin, string? ButtonId, ColorChannel Channel, PinBinding? PreviousOnPin, PinBinding? PreviousOnChannel);

    /// <param name="boardKeys">Boards to walk, in order. Empty = all boards in the wiring.</param>
    /// <param name="clearFirst">Remove existing bindings on those boards before starting.</param>
    public WiringWizard(WiringMap wiring, IEnumerable<string>? boardKeys = null, bool clearFirst = false)
    {
        _wiring = wiring;
        var keys = boardKeys?.ToList() ?? new List<string>();
        _boards = keys.Count == 0
            ? wiring.Boards.ToList()
            : keys.Select(k => wiring.FindBoard(k) ?? throw new ArgumentException($"Unknown board '{k}'")).ToList();
        if (_boards.Count == 0) throw new InvalidOperationException("There are no boards to wire.");

        if (clearFirst)
        {
            var set = _boards.Select(b => b.Key).ToHashSet();
            _wiring.Bindings.RemoveAll(b => set.Contains(b.BoardKey));
        }

        Pin = 1;
    }

    public bool IsDone { get; private set; }
    public string BoardKey => _boards[Math.Min(_boardIndex, _boards.Count - 1)].Key;
    public string BoardName => _boards[Math.Min(_boardIndex, _boards.Count - 1)].Name;
    public int Pin { get; private set; }
    public int PinCount => _boards[Math.Min(_boardIndex, _boards.Count - 1)].PinCount;
    public bool CanGoBack => _history.Count > 0;

    /// <summary>Number of pins handled so far, and in total, across all boards in this run.</summary>
    public int Completed => _history.Count;
    public int Total => _boards.Sum(b => b.PinCount);

    /// <summary>Channel order learned from the first button that got all three channels (e.g. some LEDs are wired G, R, B).</summary>
    public IReadOnlyList<ColorChannel> LearnedOrder { get; private set; } = DefaultOrder;

    public PinBinding? CurrentBinding => IsDone ? null : _wiring.FindByPin(BoardKey, Pin);

    public void Assign(string buttonId, ColorChannel channel)
    {
        if (IsDone) return;
        var prevPin = Clone(_wiring.FindByPin(BoardKey, Pin));
        var prevChannel = Clone(_wiring.Find(buttonId, channel));
        _wiring.Connect(buttonId, channel, BoardKey, Pin);
        _history.Push(new Step(BoardKey, Pin, buttonId, channel, prevPin, prevChannel));

        if (!_assignedThisSession.TryGetValue(buttonId, out var list)) _assignedThisSession[buttonId] = list = new();
        list.Remove(channel);
        list.Add(channel);
        if (ReferenceEquals(LearnedOrder, DefaultOrder) && list.Count == 3) LearnedOrder = list.ToArray();

        Advance();
    }

    /// <summary>Moves on without changing this pin (nothing lit, or the pin is unused).</summary>
    public void Skip()
    {
        if (IsDone) return;
        _history.Push(new Step(BoardKey, Pin, null, default, null, null));
        Advance();
    }

    /// <summary>Goes back one pin and undoes whatever was assigned there.</summary>
    public bool Back()
    {
        if (_history.Count == 0) return false;
        var step = _history.Pop();

        if (step.ButtonId is not null)
        {
            _wiring.DisconnectPin(step.BoardKey, step.Pin);
            Restore(step.PreviousOnPin);
            Restore(step.PreviousOnChannel);
            if (_assignedThisSession.TryGetValue(step.ButtonId, out var list)) list.Remove(step.Channel);
        }

        _boardIndex = _boards.FindIndex(b => b.Key == step.BoardKey);
        Pin = step.Pin;
        IsDone = false;
        return true;
    }

    /// <summary>
    /// Best guess for the current pin given the selected button: the next channel (in the learned order)
    /// that this button hasn't been given yet in this run. Null when there is no sensible guess.
    /// </summary>
    public ColorChannel? Suggest(string? selectedButtonId)
    {
        if (IsDone || selectedButtonId is null) return null;
        _assignedThisSession.TryGetValue(selectedButtonId, out var done);
        if (done is null || done.Count == 0) return LearnedOrder[0];
        foreach (var ch in LearnedOrder)
            if (!done.Contains(ch)) return ch;
        return null;
    }

    private void Advance()
    {
        if (Pin < PinCount)
        {
            Pin++;
            return;
        }
        if (_boardIndex < _boards.Count - 1)
        {
            _boardIndex++;
            Pin = 1;
            return;
        }
        IsDone = true;
    }

    private void Restore(PinBinding? b)
    {
        if (b is null) return;
        if (_wiring.FindByPin(b.BoardKey, b.Pin) is not null || _wiring.Find(b.ButtonId, b.Channel) is not null) return;
        _wiring.Bindings.Add(b);
    }

    private static PinBinding? Clone(PinBinding? b) =>
        b is null ? null : new PinBinding { ButtonId = b.ButtonId, Channel = b.Channel, BoardKey = b.BoardKey, Pin = b.Pin };
}
