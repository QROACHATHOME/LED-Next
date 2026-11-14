using LedNext.Core;

namespace LedNext.Hardware.Ultimarc;

/// <summary>
/// Finds Ultimarc LED boards through the PacDrive SDK: PacLED64 (IDs 1–4, verified on the cabinet) and
/// I-PAC Ultimate I/O (IDs 1–2, written from Ultimarc's documentation, not yet tested). Which board a device is
/// comes from <see cref="BoardModels.Identify"/>.
/// </summary>
public sealed class PacLed64Provider : ILedBoardProvider
{
    private bool _initialized;

    public PacLed64Provider(string? dllPath) => PacDriveNative.Configure(dllPath);

    public string Name => "Ultimarc (PacDrive SDK)";

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
        var seen = new HashSet<string>();

        for (int index = 0; index < count; index++)
        {
            int vid = PacDriveNative.PacGetVendorId(index);
            int pid = PacDriveNative.PacGetProductId(index);
            int type = SafeCall(() => PacDriveNative.PacGetDeviceType(index));
            int version = SafeCall(() => PacDriveNative.PacGetVersionNumber(index));
            var found = BoardModels.Identify(vid, pid, type);
            AppLog.Info($"  device {index}: VID {vid:X4} PID {pid:X4} type {type} version {version}" +
                        (found is null ? " (not an LED board LED-Next drives)" :
                         $" → {found.Model.NameFor(found.BoardId)}{(found.Mode is { } m ? $", {m} mode" : "")}"));
            if (found is null) continue;

            if (!seen.Add(found.Key))
            {
                AppLog.Warn($"  two {found.Model.DisplayName} boards report ID {found.BoardId}. Give each board a unique ID with Ultimarc's configuration tool; skipping the duplicate.");
                continue;
            }
            if (found.Model == BoardModels.UltimateIO)
                AppLog.Info($"  {found.Key}: Ultimate I/O support is new and untested on real boards; please report how it behaves.");

            var board = new UltimarcLedBoard(index, found);
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

/// <summary>
/// One PacLED64 or Ultimate I/O. Both take the SDK's Pac64 calls; only the pin count differs (64 / 96).
/// <list type="bullet">
/// <item>PacLED64: every changed frame goes out as one Pac64SetLEDIntensities (verified fast on the cabinet).</item>
/// <item>Ultimate I/O: Ultimarc's readme calls the all-LEDs command "long" (20 ms before the next command) and
/// says to use it only when more than half the LEDs change, so smaller changes go pin by pin.</item>
/// </list>
/// Without the batch call in the SDK build, every board falls back to per-pin writes of the changed pins.
/// </summary>
internal sealed class UltimarcLedBoard : ILedBoard
{
    private readonly int _sdkIndex;
    private readonly byte[] _current;
    private readonly bool _batchOnlyForBigChanges;
    private bool _batchAvailable = true;
    private bool _hasWritten;
    private long _writes, _batchWrites;

    public UltimarcLedBoard(int sdkIndex, DetectedBoard found)
    {
        _sdkIndex = sdkIndex;
        BoardId = found.BoardId;
        Key = found.Key;
        Model = found.Model.Model;
        DisplayName = found.Model.NameFor(found.BoardId);
        PinCount = found.Model.PinCount;
        _current = new byte[PinCount];
        _batchOnlyForBigChanges = found.Model == BoardModels.UltimateIO;
    }

    public int BoardId { get; }
    public string Key { get; }
    public string Model { get; }
    public string DisplayName { get; }
    public int PinCount { get; }
    public bool IsSimulated => false;

    public void TrySetInstantFade()
    {
        try
        {
            if (!PacDriveNative.Pac64SetLEDFadeTime(_sdkIndex, 0)) AppLog.Warn($"{Key}: Pac64SetLEDFadeTime returned false.");
        }
        catch (EntryPointNotFoundException) { AppLog.Warn($"{Key}: Pac64SetLEDFadeTime not available."); }
    }

    public void Write(ReadOnlySpan<byte> intensities)
    {
        var data = new byte[PinCount];
        intensities[..Math.Min(PinCount, intensities.Length)].CopyTo(data);

        int changed = 0;
        for (int i = 0; i < PinCount; i++) if (!_hasWritten || _current[i] != data[i]) changed++;
        if (changed == 0) return;

        bool batch = _batchAvailable && (!_batchOnlyForBigChanges || changed > PinCount / 2);
        if (batch)
        {
            try
            {
                if (!PacDriveNative.Pac64SetLEDIntensities(_sdkIndex, data))
                    throw new IOException($"{Key}: Pac64SetLEDIntensities returned false");
                data.CopyTo(_current, 0);
                _hasWritten = true;
                Count(batch: true);
                return;
            }
            catch (EntryPointNotFoundException)
            {
                _batchAvailable = false;
                AppLog.Warn($"{Key}: Pac64SetLEDIntensities not available in this SDK build; using per-pin writes.");
            }
        }

        // Per-pin: only the pins that changed.
        for (int i = 0; i < PinCount; i++)
        {
            if (_hasWritten && _current[i] == data[i]) continue;
            if (!PacDriveNative.Pac64SetLEDIntensity(_sdkIndex, i, data[i]))
                throw new IOException($"{Key}: Pac64SetLEDIntensity(pin {i + 1}) returned false");
            _current[i] = data[i];
        }
        _hasWritten = true;
        Count(batch: false);
    }

    /// <summary>The first writes of a board are logged, so a tester's log shows which path it took.</summary>
    private void Count(bool batch)
    {
        _writes++;
        if (batch) _batchWrites++;
        if (_writes is 1 or 10 or 100)
            AppLog.Info($"{Key}: {_writes} write(s) so far, {_batchWrites} as one all-pins command.");
    }

    public void Dispose() { }
}
