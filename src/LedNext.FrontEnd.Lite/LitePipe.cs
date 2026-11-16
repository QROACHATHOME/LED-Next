using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace LedNext.FrontEnd.Lite
{
    /// <summary>Writes messages to LED-Next's front-end pipe ("LedNext.FrontEnd", as FrontEndProtocol.PipeName).</summary>
    public static class LitePipe
    {
        public const string PipeName = "LedNext.FrontEnd";

        /// <summary>
        /// Connects, writes one message and disconnects (for a command-line tool that runs once per event).
        /// False when LED-Next isn't running (nothing listens) or didn't take the message in time; never throws.
        /// </summary>
        public static bool SendOnce(LiteMessage message, int timeoutMs, out string error, string pipeName = PipeName)
        {
            error = null;
            if (!Exists(pipeName))
            {
                error = "LED-Next is not running (its pipe doesn't exist).";
                return false;
            }
            try
            {
                using (var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.Out, PipeOptions.None))
                {
                    pipe.Connect(timeoutMs);
                    if (message.SentAtMs == null) message.SentAtMs = LiteMessage.NowMs();
                    var bytes = new UTF8Encoding(false).GetBytes(message.ToJson() + "\n");
                    pipe.Write(bytes, 0, bytes.Length);
                    pipe.Flush();
                    return true;
                }
            }
            catch (Exception ex)
            {
                error = ex is TimeoutException ? "LED-Next didn't take the message in time." : ex.Message;
                return false;
            }
        }

        /// <summary>
        /// True when LED-Next's pipe exists (it is running), at once: connecting to a pipe that doesn't exist would
        /// wait the whole timeout, and ES-DE waits for its scripts while browsing.
        /// </summary>
        public static bool Exists(string pipeName = PipeName)
        {
            try
            {
                // 1 ms: returns at once either way; ERROR_FILE_NOT_FOUND (2) means no such pipe.
                if (WaitNamedPipe(@"\\.\pipe\" + pipeName, 1)) return true;
                return Marshal.GetLastWin32Error() != 2;
            }
            catch
            {
                return true; // not Windows, or no kernel32: let Connect find out
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "WaitNamedPipeW")]
        private static extern bool WaitNamedPipe(string name, int timeoutMs);
    }

    /// <summary>
    /// For a plugin inside a front end: <see cref="Send"/> never blocks; a background thread keeps one connection
    /// open and writes the queue. While LED-Next isn't running, messages are dropped (after one try) and a new
    /// connection is tried at most every few seconds, so a front end never waits on LED-Next. When the queue backs
    /// up (fast scrolling), older selections are dropped: only the newest one matters.
    /// </summary>
    public sealed class LiteClient : IDisposable
    {
        private readonly string _pipeName;
        private readonly object _lock = new object();
        private readonly LinkedList<LiteMessage> _queue = new LinkedList<LiteMessage>();
        private readonly AutoResetEvent _wake = new AutoResetEvent(false);
        private readonly Thread _thread;
        private volatile bool _stop;
        private NamedPipeClientStream _pipe;
        private StreamWriter _writer;
        private DateTime _nextConnect = DateTime.MinValue;

        public LiteClient(string pipeName = LitePipe.PipeName)
        {
            _pipeName = pipeName;
            _thread = new Thread(Run) { IsBackground = true, Name = "LED-Next front-end client" };
            _thread.Start();
        }

        /// <summary>Plain text, for the plugin's own log. Raised on the background thread.</summary>
        public event Action<string> Log;

        public int ConnectTimeoutMs { get; set; } = 300;
        public TimeSpan RetryAfter { get; set; } = TimeSpan.FromSeconds(5);
        public bool IsConnected { get { var p = _pipe; return p != null && p.IsConnected; } }

        public void Send(LiteMessage message)
        {
            if (message == null || _stop) return;
            if (message.SentAtMs == null) message.SentAtMs = LiteMessage.NowMs();
            lock (_lock)
            {
                if (message.Event == LiteMessage.Select)
                {
                    // Only the newest selection waiting is worth sending.
                    for (var n = _queue.First; n != null;)
                    {
                        var next = n.Next;
                        if (n.Value.Event == LiteMessage.Select) _queue.Remove(n);
                        n = next;
                    }
                }
                _queue.AddLast(message);
                while (_queue.Count > 50) _queue.RemoveFirst();
            }
            _wake.Set();
        }

        private void Run()
        {
            while (!_stop)
            {
                _wake.WaitOne(1000);
                while (!_stop)
                {
                    LiteMessage next;
                    lock (_lock)
                    {
                        if (_queue.Count == 0) break;
                        next = _queue.First.Value;
                    }
                    if (!EnsureConnected())
                    {
                        lock (_lock) _queue.Clear(); // LED-Next isn't running: old events would only confuse it later
                        break;
                    }
                    try
                    {
                        _writer.WriteLine(next.ToJson());
                        _writer.Flush();
                        lock (_lock) { if (_queue.Count > 0 && ReferenceEquals(_queue.First.Value, next)) _queue.RemoveFirst(); }
                    }
                    catch (Exception ex)
                    {
                        Say("Lost the connection to LED-Next: " + ex.Message);
                        Close();
                    }
                }
            }
            Close();
        }

        private bool EnsureConnected()
        {
            if (IsConnected) return true;
            Close();
            if (DateTime.UtcNow < _nextConnect) return false;
            var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out, PipeOptions.None);
            try
            {
                pipe.Connect(ConnectTimeoutMs);
                _pipe = pipe;
                _writer = new StreamWriter(pipe, new UTF8Encoding(false)) { NewLine = "\n" };
                Say("Connected to LED-Next.");
                return true;
            }
            catch (Exception ex)
            {
                pipe.Dispose();
                _nextConnect = DateTime.UtcNow + RetryAfter;
                Say(ex is TimeoutException ? "LED-Next is not running." : "Can't reach LED-Next: " + ex.Message);
                return false;
            }
        }

        private void Close()
        {
            try { _writer?.Dispose(); } catch { }
            try { _pipe?.Dispose(); } catch { }
            _writer = null;
            _pipe = null;
        }

        private void Say(string text)
        {
            try { Log?.Invoke(text); } catch { }
        }

        /// <summary>Waits up to half a second for the queue to be written (a front end's last "closing"), then stops.</summary>
        public void Dispose()
        {
            var until = DateTime.UtcNow.AddMilliseconds(500);
            while (DateTime.UtcNow < until)
            {
                lock (_lock) { if (_queue.Count == 0) break; }
                if (!IsConnected && DateTime.UtcNow < _nextConnect) break;
                _wake.Set();
                Thread.Sleep(10);
            }
            _stop = true;
            _wake.Set();
            _thread.Join(500);
        }
    }
}
