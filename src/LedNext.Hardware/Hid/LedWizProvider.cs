using LedNext.Core;
using Microsoft.Win32.SafeHandles;

namespace LedNext.Hardware.Hid;

/// <summary>
/// Finds LED-Wiz boards (and Pinscape units or clones in LED-Wiz mode) by their USB ids, VID FAFA, PID 00F0–00FF.
/// No SDK needed: the LED-Wiz is a plain HID device. Not yet tested on a real board.
/// </summary>
public sealed class LedWizProvider : ILedBoardProvider
{
    private readonly List<LedWizBoard> _open = new();

    public string Name => "LED-Wiz (USB HID)";

    public IReadOnlyList<ILedBoard> Discover()
    {
        Close();
        List<HidDeviceInfo> devices;
        try { devices = HidDevices.List(BoardModels.LedWizVendorId); }
        catch (Exception ex)
        {
            AppLog.Error("LED-Wiz: listing USB devices failed", ex);
            return Array.Empty<ILedBoard>();
        }

        foreach (var d in devices)
        {
            var found = BoardModels.IdentifyLedWiz(d.VendorId, d.ProductId);
            AppLog.Info($"  LED-Wiz USB device: VID {d.VendorId:X4} PID {d.ProductId:X4}, output report {d.OutputReportLength} bytes" +
                        (found is null ? " (not an LED-Wiz unit id)" : $" → {found.Model.NameFor(found.BoardId)}"));
            if (found is null) continue;
            // An LED-Wiz takes 8-byte reports (9 with the report id). A Pinscape also lists its keyboard and
            // joystick interfaces under the same ids; those have other report sizes and are skipped.
            if (d.OutputReportLength != 9) continue;
            if (_open.Any(b => b.Key == found.Key))
            {
                AppLog.Warn($"  two LED-Wiz units report number {found.BoardId}; give each its own unit number. Skipping the duplicate.");
                continue;
            }
            var handle = HidDevices.OpenForWrite(d.Path);
            if (handle.IsInvalid)
            {
                AppLog.Warn($"  {found.Key}: couldn't open it for writing (another program using it?).");
                handle.Dispose();
                continue;
            }
            AppLog.Info($"  {found.Key}: LED-Wiz support is new and untested on real boards; please report how it behaves.");
            _open.Add(new LedWizBoard(found, handle));
        }
        return _open.OrderBy(b => b.Key).ToArray();
    }

    public void Dispose() => Close();

    private void Close()
    {
        foreach (var b in _open) b.Dispose();
        _open.Clear();
    }
}

/// <summary>One LED-Wiz unit: each changed frame is sent as SBA plus four PBA reports (<see cref="LedWizProtocol"/>).</summary>
internal sealed class LedWizBoard(DetectedBoard found, SafeFileHandle handle) : ILedBoard
{
    private byte[]? _current;
    private long _writes;

    public string Key { get; } = found.Key;
    public string Model { get; } = found.Model.Model;
    public string DisplayName { get; } = found.Model.NameFor(found.BoardId);
    public int PinCount { get; } = found.Model.PinCount;
    public bool IsSimulated => false;

    public void Write(ReadOnlySpan<byte> intensities)
    {
        var levels = new byte[PinCount];
        for (int i = 0; i < PinCount && i < intensities.Length; i++) levels[i] = LedWizProtocol.Level(intensities[i]);
        if (_current is not null && _current.AsSpan().SequenceEqual(levels)) return;

        foreach (var report in LedWizProtocol.Frame(intensities))
        {
            var buf = new byte[9]; // report id 0, then the 8 bytes
            report.CopyTo(buf, 1);
            HidDevices.Write(handle, buf);
        }
        _current = levels;
        _writes++;
        if (_writes is 1 or 10 or 100) AppLog.Info($"{Key}: {_writes} write(s) so far.");
    }

    public void Dispose() => handle.Dispose();
}
