using System.IO.Pipes;
using System.Text;

namespace LedNext.FrontEnd;

/// <summary>
/// Sends front-end events to LED-Next. Never blocks and never throws to the caller: messages go to a
/// background thread, a newer "select" replaces one not sent yet (scrolling fast sends only the last
/// game), and if LED-Next isn't running the connection is retried every <see cref="RetryAfter"/>. While it is
/// away only what still matters when it comes back is kept: the latest "started" and the latest "select".
/// </summary>
public sealed class FrontEndClient : IDisposable
{
    private const int MaxQueued = 32;
    private readonly string _pipeName;
    private readonly object _lock = new();
    private readonly LinkedList<FrontEndMessage> _queue = new();
    private readonly AutoResetEvent _wake = new(false);
    private readonly Thread _thread;
    private volatile bool _stopping;
    private NamedPipeClientStream? _pipe;
    private StreamWriter? _writer;
    private DateTime _nextConnectAttempt = DateTime.MinValue;

    /// <param name="connectTimeoutMs">How long one connection attempt may wait for LED-Next.</param>
    /// <param name="retryAfter">Wait this long after a failed connection before trying again.</param>
    public FrontEndClient(string? pipeName = null, int connectTimeoutMs = 300, TimeSpan? retryAfter = null)
    {
        _pipeName = pipeName ?? FrontEndProtocol.PipeName;
        ConnectTimeoutMs = connectTimeoutMs;
        RetryAfter = retryAfter ?? TimeSpan.FromSeconds(2);
        _thread = new Thread(Run) { IsBackground = true, Name = "LED-Next front-end client" };
        _thread.Start();
    }

    public int ConnectTimeoutMs { get; }
    public TimeSpan RetryAfter { get; }
    public bool IsConnected => _pipe?.IsConnected == true;

    /// <summary>Diagnostics (background thread). Never throw from a handler.</summary>
    public event Action<string>? Log;

    /// <summary>A connection attempt failed (LED-Next not running?). Background thread.</summary>
    public event Action? ConnectFailed;

    /// <summary>Raised once each time a connection is made. Background thread.</summary>
    public event Action? Connected;

    public void Send(FrontEndMessage message)
    {
        if (_stopping) return;
        message = message.SentAtMs is null ? message with { SentAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() } : message;
        lock (_lock)
        {
            // Only the newest selection matters; keep launches/exits in order.
            if (message.Event == FrontEndEvents.Select)
            {
                for (var n = _queue.First; n is not null; n = n.Next)
                    if (n.Value.Event == FrontEndEvents.Select) { _queue.Remove(n); break; }
            }
            _queue.AddLast(message);
            while (_queue.Count > MaxQueued) _queue.RemoveFirst();
        }
        _wake.Set();
    }

    /// <summary>For tests and shutdown: true when nothing is waiting to be sent.</summary>
    public bool IsIdle { get { lock (_lock) return _queue.Count == 0; } }

    private void Run()
    {
        while (!_stopping)
        {
            _wake.WaitOne(500);
            while (!_stopping && TryPeek(out var message))
            {
                if (!EnsureConnected())
                {
                    // LED-Next isn't there: keep only what still matters when it comes back. Anything kept
                    // makes the loop try again every RetryAfter, so "started" connects as soon as it listens.
                    KeepOnlyWhatMattersLater();
                    break;
                }
                try
                {
                    _writer!.WriteLine(FrontEndProtocol.Serialize(message));
                    _writer.Flush();
                    Dequeue(message);
                }
                catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
                {
                    Safe(() => Log?.Invoke($"Lost the connection to LED-Next: {ex.Message}"));
                    Disconnect();
                }
            }
        }
        Disconnect();
    }

    private bool EnsureConnected()
    {
        if (_pipe?.IsConnected == true) return true;
        Disconnect();
        if (DateTime.UtcNow < _nextConnectAttempt) return false;
        var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out, PipeOptions.None);
        try
        {
            pipe.Connect(ConnectTimeoutMs);
            _pipe = pipe;
            _writer = new StreamWriter(pipe, new UTF8Encoding(false)) { NewLine = "\n" };
            Safe(() => Log?.Invoke("Connected to LED-Next."));
            Safe(() => Connected?.Invoke());
            return true;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
        {
            pipe.Dispose();
            _nextConnectAttempt = DateTime.UtcNow + RetryAfter;
            Safe(() => ConnectFailed?.Invoke());
            return false;
        }
    }

    private void Disconnect()
    {
        try { _writer?.Dispose(); } catch { /* broken pipe */ }
        try { _pipe?.Dispose(); } catch { /* broken pipe */ }
        _writer = null;
        _pipe = null;
    }

    private bool TryPeek(out FrontEndMessage message)
    {
        lock (_lock)
        {
            message = _queue.First?.Value!;
            return _queue.Count > 0;
        }
    }

    private void Dequeue(FrontEndMessage message)
    {
        lock (_lock) if (ReferenceEquals(_queue.First?.Value, message)) _queue.RemoveFirst();
    }

    private void KeepOnlyWhatMattersLater()
    {
        lock (_lock)
        {
            var started = _queue.LastOrDefault(m => m.Event == FrontEndEvents.Started);
            var select = _queue.LastOrDefault(m => m.Event == FrontEndEvents.Select);
            _queue.Clear();
            if (started is not null) _queue.AddLast(started);
            if (select is not null) _queue.AddLast(select);
        }
    }

    private static void Safe(Action a)
    {
        try { a(); } catch { /* a logging handler must not stop the sender */ }
    }

    /// <summary>Sends what is queued (waiting up to <paramref name="flushTimeout"/>), then stops.</summary>
    public void Dispose() => Dispose(TimeSpan.FromMilliseconds(500));

    public void Dispose(TimeSpan flushTimeout)
    {
        if (_stopping) return;
        var until = DateTime.UtcNow + flushTimeout;
        while (!IsIdle && IsConnected && DateTime.UtcNow < until) { _wake.Set(); Thread.Sleep(10); }
        _stopping = true;
        _wake.Set();
        _thread.Join(1000);
        _wake.Dispose();
    }
}
