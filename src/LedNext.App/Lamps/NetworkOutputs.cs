using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Windows.Threading;
using LedNext.Core;

namespace LedNext.App.Lamps;

/// <summary>
/// MAME's network output ("output network" in mame.ini; src/osd/modules/output/network.cpp), also Flycast's
/// network output: the emulator listens on TCP port 8000 and sends lines "name = value" ending in '\r'.
/// "mame_start = rom" comes first (also to a client that connects later), "mame_stop = 1" at the end.
/// A background thread tries to connect every 2 s while nothing listens; nothing is ever sent.
/// </summary>
public sealed class NetworkOutputs(int port, Dispatcher dispatcher) : IEmulatorOutputs
{
    private static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(2);
    private readonly CancellationTokenSource _cts = new();
    private Thread? _thread;
    private TcpClient? _client;

    public string Via => $"network port {port}";
    public event Action<string, string>? Started;
    public event Action<string>? Stopped;
    public event Action<string, int>? Output;

    public void Start()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "LED-Next lamp network listener" };
        _thread.Start();
        AppLog.Info($"Lamps: watching for emulator network output on port {port} (MAME \"output network\", Flycast).");
    }

    private void Run()
    {
        var ct = _cts.Token;
        while (!ct.IsCancellationRequested)
        {
            var client = TryConnect();
            if (client is null)
            {
                ct.WaitHandle.WaitOne(RetryAfter);
                continue;
            }
            _client = client;
            string? rom = null;
            try
            {
                using var stream = client.GetStream();
                var buffer = new byte[4096];
                var line = new StringBuilder();
                while (!ct.IsCancellationRequested)
                {
                    int n = stream.Read(buffer, 0, buffer.Length);
                    if (n == 0) break;
                    for (int i = 0; i < n; i++)
                    {
                        char ch = (char)buffer[i];
                        if (ch is '\r' or '\n')
                        {
                            if (line.Length > 0) HandleLine(line.ToString(), ref rom);
                            line.Clear();
                        }
                        else if (line.Length < 512) line.Append(ch);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
            {
                // the emulator closed; listen again
            }
            finally
            {
                client.Dispose();
                _client = null;
            }
            if (rom is not null) Raise(() => Stopped?.Invoke(Via));
            if (!ct.IsCancellationRequested) ct.WaitHandle.WaitOne(TimeSpan.FromMilliseconds(500));
        }
    }

    /// <summary>MAME listens on IPv6 "any"; IPv4 loopback is tried too (Flycast, MAME with dual-stack off).</summary>
    private TcpClient? TryConnect()
    {
        foreach (var address in new[] { IPAddress.IPv6Loopback, IPAddress.Loopback })
        {
            var client = new TcpClient(address.AddressFamily);
            try
            {
                var connect = client.ConnectAsync(address, port);
                if (connect.Wait(300) && client.Connected) return client;
            }
            catch (Exception ex) when (ex is SocketException or AggregateException or ObjectDisposedException)
            {
                // nothing listening
            }
            client.Dispose();
        }
        return null;
    }

    private void HandleLine(string line, ref string? rom)
    {
        int eq = line.IndexOf('=');
        if (eq <= 0) return;
        var name = line[..eq].Trim();
        var value = line[(eq + 1)..].Trim();
        switch (name)
        {
            case "mame_start":
                if (rom is not null) { Raise(() => Stopped?.Invoke(Via)); }
                rom = value;
                var started = value;
                Raise(() => Started?.Invoke(started, Via));
                break;
            case "mame_stop":
                if (rom is not null) Raise(() => Stopped?.Invoke(Via));
                rom = null;
                break;
            case "pause":
                break;
            default:
                if (rom is not null && int.TryParse(value, out var v)) Raise(() => Output?.Invoke(name, v));
                break;
        }
    }

    private void Raise(Action a) => dispatcher.BeginInvoke(() =>
    {
        try { a(); }
        catch (Exception ex) { AppLog.Error("Lamps: network output handler failed", ex); }
    });

    public void Dispose()
    {
        _cts.Cancel();
        try { _client?.Dispose(); } catch (ObjectDisposedException) { }
        _thread?.Join(1000);
        _cts.Dispose();
    }
}
