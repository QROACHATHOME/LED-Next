using System.IO.Ports;
using LedNext.Core;

namespace LedNext.Hardware.Serial;

/// <summary>
/// The LED strips set up in the Boards tab (a serial port each): a strip can't be found by scanning, so each one
/// configured is opened on its port. A strip whose port can't be opened shows as "Not detected". Not yet tested on a
/// real strip.
/// </summary>
public sealed class LedStripProvider(IEnumerable<BoardDefinition> boards) : ILedBoardProvider
{
    private readonly BoardDefinition[] _strips = boards.Where(b => BoardModels.Find(b.Model)?.IsStrip == true).ToArray();
    private readonly List<LedStripBoard> _open = new();

    public string Name => "LED strips (serial)";

    public IReadOnlyList<ILedBoard> Discover()
    {
        Close();
        foreach (var def in _strips)
        {
            if (string.IsNullOrWhiteSpace(def.SerialPort))
            {
                AppLog.Warn($"  {def.Key}: no serial port set (Boards tab).");
                continue;
            }
            int leds = def.LedCount ?? def.PinCount / 3;
            int baud = def.BaudRate ?? BoardModels.DefaultBaudRate;
            try
            {
                var port = new SerialPort(def.SerialPort, baud) { WriteTimeout = 500, DtrEnable = false, RtsEnable = false };
                port.Open();
                AppLog.Info($"  {def.Key}: {def.SerialPort} at {baud} baud, {leds} LEDs (Adalight). LED strip support is new; please report how it behaves.");
                _open.Add(new LedStripBoard(def.Key, def.Name, leds, port));
            }
            catch (Exception ex)
            {
                AppLog.Warn($"  {def.Key}: couldn't open {def.SerialPort}: {ex.Message}");
            }
        }
        return _open.ToArray();
    }

    public void Dispose() => Close();

    private void Close()
    {
        foreach (var b in _open) b.Dispose();
        _open.Clear();
    }
}

/// <summary>
/// One strip on a serial port, Adalight frames (<see cref="AdalightProtocol"/>). The last frame is sent again every
/// second while nothing changes: WLED goes back to its own effect after a short silence (2.5 s by default), and some
/// Adalight sketches blank the strip after a timeout.
/// </summary>
internal sealed class LedStripBoard : ILedBoard
{
    private static readonly TimeSpan KeepAlive = TimeSpan.FromSeconds(1);
    private readonly SerialPort _port;
    private readonly int _leds;
    private readonly object _gate = new();
    private readonly Timer _timer;
    private byte[]? _lastFrame;
    private DateTime _lastSent;
    private long _writes;
    private bool _disposed;

    public LedStripBoard(string key, string name, int leds, SerialPort port)
    {
        Key = key;
        DisplayName = name;
        _leds = leds;
        PinCount = leds * 3;
        _port = port;
        _timer = new Timer(_ => Resend(), null, KeepAlive, KeepAlive);
    }

    public string Key { get; }
    public string Model => BoardModels.LedStrip.Model;
    public string DisplayName { get; }
    public int PinCount { get; }
    public bool IsSimulated => false;

    public void Write(ReadOnlySpan<byte> intensities)
    {
        var frame = AdalightProtocol.Frame(intensities, _leds);
        lock (_gate)
        {
            if (_disposed) return;
            _port.Write(frame, 0, frame.Length); // throws on a timeout or an unplugged port: the engine retries
            _lastFrame = frame;
            _lastSent = DateTime.UtcNow;
        }
        _writes++;
        if (_writes is 1 or 10 or 100) AppLog.Info($"{Key}: {_writes} write(s) so far ({frame.Length} bytes each).");
    }

    private void Resend()
    {
        try
        {
            lock (_gate)
            {
                if (_disposed || _lastFrame is null || DateTime.UtcNow - _lastSent < KeepAlive) return;
                _port.Write(_lastFrame, 0, _lastFrame.Length);
                _lastSent = DateTime.UtcNow;
            }
        }
        catch { /* the next real write reports the problem */ }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }
        _timer.Dispose();
        try { _port.Dispose(); } catch { }
    }
}
