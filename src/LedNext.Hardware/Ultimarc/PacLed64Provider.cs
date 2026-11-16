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
            if (found.Model != BoardModels.PacLed64)
                AppLog.Info($"  {found.Key}: {found.Model.DisplayName} support is new and untested on real boards; please report how it behaves.");

            if (found.Model == BoardModels.PacDrive)
            {
                boards.Add(new PacDriveBoard(index, found));
                continue;
            }
            var board = new UltimarcLedBoard(index, found);
            board.ResetEffects();
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
    private bool _flashAvailable = true;
    private bool _resetPending;
    private long _writes, _batchWrites, _resets;

    public UltimarcLedBoard(int sdkIndex, DetectedBoard found)
    {
        _sdkIndex = sdkIndex;
        BoardId = found.BoardId;
        Key = found.Key;
        Model = found.Model.Model;
        DisplayName = found.Model.NameFor(found.BoardId);
        PinCount = found.Model.PinCount;
        _current = new byte[PinCount];
        // Ultimarc's readme: the all-LEDs command is "long" on the Ultimate I/O and NanoLED; use it only for big changes.
        // (The PacLED64 is fast with it on every frame, verified on the cabinet.)
        _batchOnlyForBigChanges = found.Model != BoardModels.PacLed64;
    }

    public int BoardId { get; }
    public string Key { get; }
    public string Model { get; }
    public string DisplayName { get; }
    public int PinCount { get; }
    public bool IsSimulated => false;

    /// <summary>
    /// Instant fades and no flashing: the board's own effects, which LED-Next never uses. A PacLED64 keeps them until
    /// it loses power, so a garbled command can leave LEDs blinking on their own while LED-Next sends steady colours
    /// (seen on the cabinet after a refused write: LEDs going on and off, P2 button 1 blinking; a PC restart cleared it).
    /// Called when the board is opened and before the write after a refused one. Never throws.
    /// </summary>
    public void ResetEffects()
    {
        try
        {
            if (!PacDriveNative.Pac64SetLEDFadeTime(_sdkIndex, 0)) AppLog.Warn($"{Key}: Pac64SetLEDFadeTime returned false.");
        }
        catch (EntryPointNotFoundException) { AppLog.Warn($"{Key}: Pac64SetLEDFadeTime not available."); }
        catch (Exception ex) { AppLog.Warn($"{Key}: Pac64SetLEDFadeTime failed: {ex.Message}"); }
        if (!_flashAvailable) return;
        try
        {
            if (!PacDriveNative.Pac64SetLEDFlashSpeeds(_sdkIndex, 0)) AppLog.Warn($"{Key}: Pac64SetLEDFlashSpeeds returned false.");
        }
        catch (EntryPointNotFoundException) { _flashAvailable = false; AppLog.Warn($"{Key}: Pac64SetLEDFlashSpeeds not available."); }
        catch (Exception ex) { AppLog.Warn($"{Key}: Pac64SetLEDFlashSpeeds failed: {ex.Message}"); }
    }

    public void Write(ReadOnlySpan<byte> intensities)
    {
        if (_resetPending)
        {
            // The last write was refused and may have reached the board half done: clear any effect it set.
            _resetPending = false;
            ResetEffects();
            _resets++;
            if (_resets is 1 || _resets % 20 == 0)
                AppLog.Info($"{Key}: cleared the board's own flashing and fades after a refused write ({_resets} so far).");
        }
        try { WriteIntensities(intensities); }
        catch { _resetPending = true; throw; }
    }

    private void WriteIntensities(ReadOnlySpan<byte> intensities)
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

/// <summary>
/// A PacDrive: 16 outputs, on or off only, all set in one PacSetLEDStates call. An output is on when its value is at
/// least <see cref="BoardModels.OnOffThreshold"/> (25 %), so fades and dimmed colours snap on or off. Not yet tested
/// on a real board.
/// </summary>
internal sealed class PacDriveBoard(int sdkIndex, DetectedBoard found) : ILedBoard
{
    private ushort? _current;
    private long _writes;

    public string Key { get; } = found.Key;
    public string Model { get; } = found.Model.Model;
    public string DisplayName { get; } = found.Model.NameFor(found.BoardId);
    public int PinCount { get; } = found.Model.PinCount;
    public bool IsSimulated => false;

    public void Write(ReadOnlySpan<byte> intensities)
    {
        var states = PacDriveProtocol.States(intensities);
        if (_current == states) return;
        if (!PacDriveNative.PacSetLEDStates(sdkIndex, states))
            throw new IOException($"{Key}: PacSetLEDStates returned false");
        _current = states;
        _writes++;
        if (_writes is 1 or 10 or 100) AppLog.Info($"{Key}: {_writes} write(s) so far (outputs {states:X4}).");
    }

    public void Dispose() { }
}
