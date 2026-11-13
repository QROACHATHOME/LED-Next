using System.Runtime.InteropServices;
using System.Windows.Interop;
using LedNext.Core;

namespace LedNext.App.Lamps;

/// <summary>
/// MAME's Windows output protocol (src/osd/modules/output/win32_output.h; "output windows" in mame.ini), also
/// spoken by Supermodel and TeknoParrot's Output Blaster. MAME broadcasts "MAMEOutputStart" with its output
/// window; a client registers with "MAMEOutputRegister" and then gets "MAMEOutputUpdateState" (id, value) for
/// every output. Names come from "MAMEOutputGetIDString" as a WM_COPYDATA reply; id 0 is the rom name.
/// Runs on the UI thread: a hidden top-level window (broadcasts don't reach message-only windows).
/// </summary>
public sealed class MameWindowOutputs : IEmulatorOutputs
{
    private const int WM_COPYDATA = 0x004A;
    private const int CopyDataIdString = 1;
    private const int PauseId = 1;
    private static readonly IntPtr ClientId = new(0x4C4E_0000 + Environment.ProcessId % 0xFFFF);

    private readonly uint _start, _stop, _update, _register, _unregister, _getIdString;
    private HwndSource? _window;
    private IntPtr _mame;
    private string? _rom;
    private readonly Dictionary<int, string> _names = new();
    private readonly List<(int Id, int Value)> _waiting = new();

    public MameWindowOutputs()
    {
        _start = RegisterWindowMessage("MAMEOutputStart");
        _stop = RegisterWindowMessage("MAMEOutputStop");
        _update = RegisterWindowMessage("MAMEOutputUpdateState");
        _register = RegisterWindowMessage("MAMEOutputRegister");
        _unregister = RegisterWindowMessage("MAMEOutputUnregister");
        _getIdString = RegisterWindowMessage("MAMEOutputGetIDString");
    }

    public string Via => "Windows messages";
    public event Action<string, string, string>? Started;
    public event Action<string>? Stopped;
    public event Action<bool>? Paused;
    public event Action<string, int>? Output;

    public void Start()
    {
        _window = new HwndSource(new HwndSourceParameters("LED-Next lamp listener")
        {
            WindowStyle = 0, // WS_OVERLAPPED, not visible: top level, so it gets MAME's broadcasts
            Width = 0,
            Height = 0,
        });
        _window.AddHook(WndProc);
        AppLog.Info("Lamps: listening for MAME's Windows output messages (mame.ini \"output windows\").");
        // MAME may already be running (LED-Next started after it).
        var running = FindWindow("MAMEOutput", "MAMEOutput");
        if (running != IntPtr.Zero) Connect(running);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        try
        {
            uint m = unchecked((uint)msg);
            if (m == _start) { Connect(wParam); handled = true; }
            else if (m == _stop) { if (wParam == _mame || _mame == IntPtr.Zero) Disconnect(); handled = true; }
            else if (m == _update) { OnUpdate(wParam.ToInt32(), lParam.ToInt32()); handled = true; }
            else if (msg == WM_COPYDATA) { handled = OnCopyData(lParam); return handled ? new IntPtr(1) : IntPtr.Zero; }
        }
        catch (Exception ex)
        {
            AppLog.Error("Lamps: MAME output message failed", ex);
        }
        return IntPtr.Zero;
    }

    private void Connect(IntPtr mame)
    {
        if (mame == IntPtr.Zero || mame == _mame) return;
        if (_mame != IntPtr.Zero) Disconnect();
        _mame = mame;
        _names.Clear();
        _waiting.Clear();
        _rom = null;
        PostMessage(mame, _register, _window!.Handle, ClientId);
        RequestRom();
    }

    /// <summary>Asks for id 0 (the rom). MAME may be too busy while it starts; then it is asked again on the next update.</summary>
    private void RequestRom()
    {
        if (_rom is not null || _mame == IntPtr.Zero) return;
        Ask(0);
        if (_names.TryGetValue(0, out var rom) && rom.Length > 0)
        {
            _rom = rom;
            Started?.Invoke(rom, "MAME", Via); // Supermodel / Output Blaster use the same messages; not told apart yet
            foreach (var (id, value) in _waiting.ToArray()) OnUpdate(id, value);
            _waiting.Clear();
        }
    }

    private void OnUpdate(int id, int value)
    {
        if (_mame == IntPtr.Zero) return;
        if (id == PauseId) { if (_rom is not null) Paused?.Invoke(value != 0); return; }
        if (_rom is null)
        {
            _waiting.RemoveAll(w => w.Id == id);
            _waiting.Add((id, value));
            RequestRom();
            return;
        }
        if (!_names.TryGetValue(id, out var name)) { Ask(id); _names.TryGetValue(id, out name); }
        if (!string.IsNullOrEmpty(name)) Output?.Invoke(name, value);
    }

    /// <summary>MAME answers with WM_COPYDATA to our window while this call waits (at most 500 ms).</summary>
    private void Ask(int id) =>
        SendMessageTimeout(_mame, _getIdString, _window!.Handle, new IntPtr(id), SMTO_ABORTIFHUNG | SMTO_NORMAL, 500, out _);

    private bool OnCopyData(IntPtr lParam)
    {
        var data = Marshal.PtrToStructure<COPYDATASTRUCT>(lParam);
        if (data.dwData.ToInt64() != CopyDataIdString || data.lpData == IntPtr.Zero || data.cbData < 5) return false;
        int id = Marshal.ReadInt32(data.lpData);
        var name = Marshal.PtrToStringAnsi(data.lpData + 4, Math.Max(0, data.cbData - 5)).TrimEnd('\0');
        _names[id] = name;
        return true;
    }

    private void Disconnect()
    {
        if (_mame == IntPtr.Zero) return;
        bool started = _rom is not null;
        _mame = IntPtr.Zero;
        _rom = null;
        _names.Clear();
        _waiting.Clear();
        if (started) Stopped?.Invoke(Via);
    }

    public void Dispose()
    {
        try
        {
            if (_mame != IntPtr.Zero && _window is not null) PostMessage(_mame, _unregister, _window.Handle, ClientId);
            _window?.Dispose();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // shutting down
        }
        _window = null;
    }

    private const uint SMTO_NORMAL = 0x0000, SMTO_ABORTIFHUNG = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    private struct COPYDATASTRUCT
    {
        public IntPtr dwData;
        public int cbData;
        public IntPtr lpData;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint RegisterWindowMessage(string name);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindWindow(string? className, string? windowName);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);
}
