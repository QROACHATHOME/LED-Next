using System.IO.Pipes;
using System.Text;
using LedNext.FrontEnd;

namespace LedNext.Core.FrontEnd;

/// <summary>
/// Listens on the LED-Next pipe for front-end plugins (LaunchBox and BigBox can both be connected).
/// Messages are raised on a background thread; malformed lines are logged and skipped.
/// </summary>
public sealed class FrontEndServer : IDisposable
{
    private readonly string _pipeName;
    private readonly CancellationTokenSource _cts = new();
    private Task? _acceptLoop;
    private int _clients;

    public FrontEndServer(string? pipeName = null) => _pipeName = pipeName ?? FrontEndProtocol.PipeName;

    public event Action<FrontEndMessage>? MessageReceived;

    public int ConnectedClients => Volatile.Read(ref _clients);

    /// <summary>
    /// Starts listening. Throws IOException or UnauthorizedAccessException if another process (another
    /// LED-Next) already owns the pipe.
    /// </summary>
    public void Start()
    {
        // Created here, as the first instance, so a second LED-Next finds out at once.
        var first = new NamedPipeServerStream(_pipeName, PipeDirection.In, NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.FirstPipeInstance);
        _acceptLoop = Task.Run(() => AcceptLoop(first, _cts.Token));
        AppLog.Info($"Listening for front ends on pipe '{_pipeName}'.");
    }

    private NamedPipeServerStream CreatePipe() => new(_pipeName, PipeDirection.In,
        NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

    private async Task AcceptLoop(NamedPipeServerStream pipe, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await pipe.WaitForConnectionAsync(ct).ConfigureAwait(false);
                var connected = pipe;
                _ = Task.Run(() => ReadClient(connected, ct), ct);
                pipe = CreatePipe();
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                AppLog.Warn($"Front-end pipe error, listening again: {ex.Message}");
                try { pipe.Dispose(); } catch { /* already broken */ }
                try { await Task.Delay(500, ct).ConfigureAwait(false); } catch (OperationCanceledException) { break; }
                try { pipe = CreatePipe(); }
                catch (IOException ex2) { AppLog.Error("Could not listen for front ends any more", ex2); return; }
            }
        }
        pipe.Dispose();
    }

    private async Task ReadClient(NamedPipeServerStream pipe, CancellationToken ct)
    {
        int n = Interlocked.Increment(ref _clients);
        AppLog.Info($"Front end connected ({n} connected).");
        try
        {
            using var reader = new StreamReader(pipe, new UTF8Encoding(false));
            while (!ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                if (line is null) break;
                var message = FrontEndProtocol.Parse(line);
                if (message is null)
                {
                    AppLog.Warn($"Ignored a front-end message that is not LED-Next JSON ({Math.Min(line.Length, 80)} chars).");
                    continue;
                }
                try { MessageReceived?.Invoke(message); }
                catch (Exception ex) { AppLog.Error($"Handling front-end message '{message.Event}' failed", ex); }
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { /* client went away */ }
        finally
        {
            pipe.Dispose();
            n = Interlocked.Decrement(ref _clients);
            if (!ct.IsCancellationRequested) AppLog.Info($"Front end disconnected ({n} connected).");
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _acceptLoop?.Wait(1000); } catch (AggregateException) { }
        _cts.Dispose();
    }
}
