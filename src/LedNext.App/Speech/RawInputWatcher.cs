using System.Runtime.InteropServices;
using System.Windows.Interop;
using LedNext.Core;

namespace LedNext.App.Speech;

/// <summary>
/// Notices any key press, joystick or gamepad change while another program (MAME) has the focus, through Windows
/// Raw Input with <c>RIDEV_INPUTSINK</c> on a hidden message-only window. Only listens between
/// <see cref="Begin"/> and <see cref="End"/> (gamepads send many reports, so it isn't left on). Keyboard: key
/// downs only. Joysticks and gamepads: a report that differs from that device's previous one (the first report of
/// each device is only remembered). Mouse and trackball movement is not watched. UI thread only. Never throws:
/// a failure is logged and the watcher stays off.
/// </summary>
public sealed class RawInputWatcher : IDisposable
{
    private const int WM_INPUT = 0x00FF;
    private const uint RID_INPUT = 0x10000003, RIDI_DEVICENAME = 0x20000007;
    private const uint RIM_TYPEKEYBOARD = 1, RIM_TYPEHID = 2;
    private const uint RIDEV_INPUTSINK = 0x100, RIDEV_REMOVE = 0x1;
    private const ushort RI_KEY_BREAK = 1;

    private HwndSource? _window;
    private bool _listening;
    private readonly Dictionary<IntPtr, byte[]> _last = new();

    /// <summary>Raised on the UI thread with what was pressed or moved ("key", "HID\VID_D209&PID_0410…").</summary>
    public event Action<string>? Input;

    public void Begin()
    {
        if (_listening) return;
        try
        {
            _window ??= CreateWindow();
            _last.Clear();
            if (!Register(RIDEV_INPUTSINK, _window.Handle))
            {
                AppLog.Warn($"Speech: could not watch the controls (RegisterRawInputDevices error {Marshal.GetLastWin32Error()}); input won't stop the speech.");
                return;
            }
            _listening = true;
        }
        catch (Exception ex) { AppLog.Error("Speech: could not watch the controls", ex); }
    }

    public void End()
    {
        if (!_listening) return;
        _listening = false;
        try { Register(RIDEV_REMOVE, IntPtr.Zero); }
        catch (Exception ex) { AppLog.Error("Speech: could not stop watching the controls", ex); }
    }

    private HwndSource CreateWindow()
    {
        var p = new HwndSourceParameters("LED-Next raw input") { ParentWindow = new IntPtr(-3), WindowStyle = 0 }; // HWND_MESSAGE
        var w = new HwndSource(p);
        w.AddHook(Hook);
        return w;
    }

    private bool Register(uint flags, IntPtr hwnd)
    {
        // Generic desktop: 6 = keyboard, 4 = joystick, 5 = gamepad.
        var devices = new[] { 6, 4, 5 }.Select(u => new RAWINPUTDEVICE { usUsagePage = 1, usUsage = (ushort)u, dwFlags = flags, hwndTarget = hwnd }).ToArray();
        return RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());
    }

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_INPUT || !_listening) return IntPtr.Zero;
        try
        {
            if (Read(lParam) is { } what) Input?.Invoke(what);
        }
        catch (Exception ex) { AppLog.Error("Speech: reading input failed", ex); _listening = false; }
        return IntPtr.Zero;
    }

    private string? Read(IntPtr lParam)
    {
        uint headerSize = (uint)Marshal.SizeOf<RAWINPUTHEADER>();
        uint size = 0;
        GetRawInputData(lParam, RID_INPUT, IntPtr.Zero, ref size, headerSize);
        if (size == 0) return null;
        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (GetRawInputData(lParam, RID_INPUT, buffer, ref size, headerSize) != size) return null;
            var header = Marshal.PtrToStructure<RAWINPUTHEADER>(buffer);
            var data = buffer + (int)headerSize;
            if (header.dwType == RIM_TYPEKEYBOARD)
            {
                var flags = (ushort)Marshal.ReadInt16(data, 2); // RAWKEYBOARD: MakeCode, Flags, Reserved, VKey…
                return (flags & RI_KEY_BREAK) == 0 ? "a key" : null;
            }
            if (header.dwType != RIM_TYPEHID) return null;
            int sizeHid = Marshal.ReadInt32(data), count = Marshal.ReadInt32(data, 4);
            int length = sizeHid * count;
            if (length <= 0 || length > 4096) return null;
            var report = new byte[length];
            Marshal.Copy(data + 8, report, 0, length);
            bool changed = _last.TryGetValue(header.hDevice, out var before) && !before.AsSpan().SequenceEqual(report);
            _last[header.hDevice] = report;
            return changed ? DeviceName(header.hDevice) : null;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static string DeviceName(IntPtr device)
    {
        uint chars = 0;
        GetRawInputDeviceInfo(device, RIDI_DEVICENAME, null, ref chars);
        if (chars == 0) return "a joystick";
        var sb = new System.Text.StringBuilder((int)chars);
        GetRawInputDeviceInfo(device, RIDI_DEVICENAME, sb, ref chars);
        var name = sb.ToString();
        int vid = name.IndexOf("VID_", StringComparison.OrdinalIgnoreCase);
        return vid >= 0 && name.Length >= vid + 17 ? $"joystick {name.Substring(vid, 17)}" : "a joystick";
    }

    public void Dispose()
    {
        End();
        _window?.Dispose();
        _window = null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTDEVICE { public ushort usUsagePage; public ushort usUsage; public uint dwFlags; public IntPtr hwndTarget; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTHEADER { public uint dwType; public uint dwSize; public IntPtr hDevice; public IntPtr wParam; }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] devices, uint count, uint size);

    [DllImport("user32.dll")]
    private static extern uint GetRawInputData(IntPtr hRawInput, uint command, IntPtr data, ref uint size, uint headerSize);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint GetRawInputDeviceInfo(IntPtr device, uint command, System.Text.StringBuilder? data, ref uint size);
}
