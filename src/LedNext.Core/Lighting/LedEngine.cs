using System.Diagnostics;

namespace LedNext.Core.Lighting;

/// <summary>
/// Renders the current <see cref="ILightSource"/> on a background thread and pushes pin values
/// to the boards. All hardware access happens here, under <see cref="_outputLock"/>.
/// </summary>
public sealed class LedEngine : IDisposable
{
    private static readonly TimeSpan FaultRetryInterval = TimeSpan.FromSeconds(5);

    private readonly object _outputLock = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Dictionary<string, byte[]> _lastWritten = new();
    private readonly Dictionary<string, TimeSpan> _faultedUntil = new();

    private volatile PanelSnapshot _snapshot;
    private volatile ILightSource _source = new AllOff();
    private ILedBoardProvider? _provider;
    private List<ILedBoard> _boards = new();
    private Thread? _thread;
    private volatile bool _running;
    private string? _lastSourceError;

    public LedEngine(CabinetConfig config) => _snapshot = PanelSnapshot.From(config);

    /// <summary>Raised on the engine thread after every frame.</summary>
    public event Action<FrameSnapshot>? FrameRendered;

    public PanelSnapshot Snapshot => _snapshot;

    public ILightSource Source
    {
        get => _source;
        set
        {
            _source = value ?? new AllOff();
            _lastSourceError = null;
            AppLog.Info($"Light source: {_source.Name}");
        }
    }

    public IReadOnlyList<ILedBoard> Boards
    {
        get { lock (_outputLock) return _boards.ToArray(); }
    }

    public void ApplyConfig(CabinetConfig config) => _snapshot = PanelSnapshot.From(config);

    /// <summary>
    /// Blacks out and releases the current boards, then discovers boards from <paramref name="provider"/>.
    /// Runs under the output lock so it never overlaps a write.
    /// </summary>
    public IReadOnlyList<ILedBoard> ReplaceProvider(ILedBoardProvider? provider)
    {
        lock (_outputLock)
        {
            BlackoutLocked();
            foreach (var b in _boards) SafeDispose(b);
            SafeDispose(_provider);
            _boards = new();
            _lastWritten.Clear();
            _faultedUntil.Clear();
            _provider = provider;

            if (provider is not null)
            {
                try
                {
                    _boards = provider.Discover().ToList();
                }
                catch (Exception ex)
                {
                    AppLog.Error($"{provider.Name}: discovery failed", ex);
                }
                AppLog.Info($"{provider.Name}: {_boards.Count} board(s): {string.Join(", ", _boards.Select(b => b.Key))}");
            }
            return _boards.ToArray();
        }
    }

    public void Start()
    {
        if (_running) return;
        _running = true;
        _thread = new Thread(Loop) { IsBackground = true, Name = "LED-Next output", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }

    public void Stop()
    {
        if (!_running) return;
        _running = false;
        _thread?.Join(TimeSpan.FromSeconds(2));
        _thread = null;
        lock (_outputLock) BlackoutLocked();
    }

    public void Dispose()
    {
        Stop();
        ReplaceProvider(null);
    }

    /// <summary>Renders one frame without touching hardware. Public for tests.</summary>
    public FrameSnapshot RenderFrame(TimeSpan time)
    {
        var snapshot = _snapshot;
        var source = _source;
        var ctx = new RenderContext(snapshot, time);

        try
        {
            source.Render(ctx);
        }
        catch (Exception ex)
        {
            // Log once per source, then show nothing rather than crash the output thread.
            if (_lastSourceError != source.Name)
            {
                _lastSourceError = source.Name;
                AppLog.Error($"Light source '{source.Name}' failed", ex);
            }
            ctx = new RenderContext(snapshot, time);
        }

        var pins = new Dictionary<string, byte[]>();
        foreach (var board in snapshot.Boards) pins[board.Key] = new byte[board.PinCount];

        foreach (var (buttonId, color) in ctx.Colors)
        {
            if (!snapshot.Targets.TryGetValue(buttonId, out var targets)) continue;
            foreach (var t in targets)
            {
                if (pins.TryGetValue(t.BoardKey, out var buf) && t.Pin >= 1 && t.Pin <= buf.Length)
                    buf[t.Pin - 1] = Correct(color[t.Channel], snapshot.Brightness, snapshot.Gamma);
            }
        }

        foreach (var ((boardKey, pin), value) in ctx.RawPins)
        {
            if (pins.TryGetValue(boardKey, out var buf) && pin >= 1 && pin <= buf.Length)
                buf[pin - 1] = value;
        }

        // Button colours are read back from the pins, so the preview shows exactly what the
        // hardware gets — including raw pin tests landing on a wired button.
        var buttonColors = new Dictionary<string, Rgb>(snapshot.Buttons.Count);
        foreach (var b in snapshot.Buttons)
        {
            byte r = 0, g = 0, bl = 0;
            if (snapshot.Targets.TryGetValue(b.Id, out var targets))
            {
                foreach (var t in targets)
                {
                    if (!pins.TryGetValue(t.BoardKey, out var buf) || t.Pin < 1 || t.Pin > buf.Length) continue;
                    var v = buf[t.Pin - 1];
                    switch (t.Channel)
                    {
                        case ColorChannel.R: r = v; break;
                        case ColorChannel.G: g = v; break;
                        default: bl = v; break;
                    }
                }
            }
            buttonColors[b.Id] = new Rgb(r, g, bl);
        }

        return new FrameSnapshot(time, source.Name, ctx.Status, buttonColors, pins);
    }

    public static byte Correct(byte value, double brightness, double gamma)
    {
        if (value == 0) return 0;
        double v = value / 255.0;
        if (Math.Abs(gamma - 1) > 0.001) v = Math.Pow(v, gamma);
        return (byte)Math.Round(Math.Clamp(v * brightness, 0, 1) * 255);
    }

    private void Loop()
    {
        AppLog.Info("Output thread started.");
        while (_running)
        {
            var started = _clock.Elapsed;
            try
            {
                var frame = RenderFrame(started);
                lock (_outputLock) WriteLocked(frame, started);
                FrameRendered?.Invoke(frame);
            }
            catch (Exception ex)
            {
                AppLog.Error("Output loop error", ex);
            }

            var period = TimeSpan.FromSeconds(1.0 / _snapshot.RefreshHz);
            var remaining = period - (_clock.Elapsed - started);
            if (remaining > TimeSpan.Zero) Thread.Sleep(remaining);
        }
        AppLog.Info("Output thread stopped.");
    }

    private void WriteLocked(FrameSnapshot frame, TimeSpan now)
    {
        foreach (var board in _boards)
        {
            if (_faultedUntil.TryGetValue(board.Key, out var until) && now < until) continue;

            var data = new byte[board.PinCount];
            if (frame.BoardPins.TryGetValue(board.Key, out var src))
                Array.Copy(src, data, Math.Min(src.Length, data.Length));

            if (_lastWritten.TryGetValue(board.Key, out var last) && last.AsSpan().SequenceEqual(data)) continue;

            try
            {
                board.Write(data);
                _lastWritten[board.Key] = data;
                if (_faultedUntil.Remove(board.Key)) AppLog.Info($"{board.Key}: writes working again.");
            }
            catch (Exception ex)
            {
                _faultedUntil[board.Key] = now + FaultRetryInterval;
                _lastWritten.Remove(board.Key);
                AppLog.Error($"{board.Key}: write failed; retrying in {FaultRetryInterval.TotalSeconds:0}s", ex);
            }
        }
    }

    private void BlackoutLocked()
    {
        foreach (var board in _boards)
        {
            try { board.Write(new byte[board.PinCount]); }
            catch (Exception ex) { AppLog.Error($"{board.Key}: blackout failed", ex); }
        }
        _lastWritten.Clear();
    }

    private static void SafeDispose(IDisposable? d)
    {
        try { d?.Dispose(); }
        catch (Exception ex) { AppLog.Error("Dispose failed", ex); }
    }
}
