using LedNext.Core;

namespace LedNext.Hardware.Ultimarc;

/// <summary>Finds PacLED64 boards through the PacDrive SDK. Supports up to four boards (IDs 1–4).</summary>
public sealed class PacLed64Provider : ILedBoardProvider
{
    private bool _initialized;

    public PacLed64Provider(string? dllPath) => PacDriveNative.Configure(dllPath);

    public string Name => "PacLED64";

    public IReadOnlyList<ILedBoard> Discover()
    {
        Shutdown();
        int count;
        try
        {
            count = PacDriveNative.PacInitialize();
            _initialized = true;
        }
        catch (DllNotFoundException ex)
        {
            AppLog.Error("PacDrive SDK not available", ex);
            return Array.Empty<ILedBoard>();
        }
        catch (BadImageFormatException ex)
        {
            AppLog.Error($"PacDrive DLL is the wrong bitness for this {(Environment.Is64BitProcess ? "64" : "32")}-bit process", ex);
            return Array.Empty<ILedBoard>();
        }

        AppLog.Info($"PacDrive SDK reports {count} Ultimarc device(s).");
        var boards = new List<ILedBoard>();
        var seenIds = new HashSet<int>();

        for (int index = 0; index < count; index++)
        {
            int vid = PacDriveNative.PacGetVendorId(index);
            int pid = PacDriveNative.PacGetProductId(index);
            int type = SafeCall(() => PacDriveNative.PacGetDeviceType(index));
            int version = SafeCall(() => PacDriveNative.PacGetVersionNumber(index));
            AppLog.Info($"  device {index}: VID {vid:X4} PID {pid:X4} type {type} version {version}");

            int boardId = pid - PacDriveNative.PacLed64FirstProductId + 1;
            if (vid != PacDriveNative.UltimarcVendorId || boardId < 1 || boardId > 4) continue;

            if (!seenIds.Add(boardId))
            {
                AppLog.Warn($"  two PacLED64 boards report ID {boardId}. Give each board a unique ID with Ultimarc's configuration tool; skipping the duplicate.");
                continue;
            }

            var board = new PacLed64Board(index, boardId);
            board.TrySetInstantFade();
            boards.Add(board);
        }

        return boards.OrderBy(b => b.Key).ToArray();
    }

    public void Dispose() => Shutdown();

    private void Shutdown()
    {
        if (!_initialized) return;
        try { PacDriveNative.PacShutdown(); }
        catch (Exception ex) { AppLog.Error("PacShutdown failed", ex); }
        _initialized = false;
    }

    private static int SafeCall(Func<int> f)
    {
        try { return f(); }
        catch (EntryPointNotFoundException) { return -1; }
    }
}

internal sealed class PacLed64Board : ILedBoard
{
    private readonly int _sdkIndex;
    private readonly byte[] _current = new byte[64];
    private bool _batchAvailable = true;
    private bool _hasWritten;

    public PacLed64Board(int sdkIndex, int boardId)
    {
        _sdkIndex = sdkIndex;
        BoardId = boardId;
    }

    public int BoardId { get; }
    public string Key => $"PacLED64#{BoardId}";
    public string Model => "PacLED64";
    public string DisplayName => $"PacLED64 #{BoardId}";
    public int PinCount => 64;
    public bool IsSimulated => false;

    public void TrySetInstantFade()
    {
        try { PacDriveNative.Pac64SetLEDFadeTime(_sdkIndex, 0); }
        catch (EntryPointNotFoundException) { AppLog.Warn($"{Key}: Pac64SetLEDFadeTime not available."); }
    }

    public void Write(ReadOnlySpan<byte> intensities)
    {
        var data = new byte[64];
        intensities[..Math.Min(64, intensities.Length)].CopyTo(data);

        if (_batchAvailable)
        {
            try
            {
                if (!PacDriveNative.Pac64SetLEDIntensities(_sdkIndex, data))
                    throw new IOException($"{Key}: Pac64SetLEDIntensities returned false");
                data.CopyTo(_current, 0);
                _hasWritten = true;
                return;
            }
            catch (EntryPointNotFoundException)
            {
                _batchAvailable = false;
                AppLog.Warn($"{Key}: Pac64SetLEDIntensities not available in this SDK build; using per-pin writes.");
            }
        }

        // Per-pin fallback: only send pins that changed.
        for (int i = 0; i < 64; i++)
        {
            if (_hasWritten && _current[i] == data[i]) continue;
            if (!PacDriveNative.Pac64SetLEDIntensity(_sdkIndex, i, data[i]))
                throw new IOException($"{Key}: Pac64SetLEDIntensity(pin {i + 1}) returned false");
            _current[i] = data[i];
        }
        _hasWritten = true;
    }

    public void Dispose() { }
}
