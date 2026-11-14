using System.Diagnostics;
using System.Runtime.InteropServices;
using LedNext.Core;
using LedNext.Core.Outputs;

namespace LedNext.App.Lamps;

/// <summary>
/// Finds the process behind an emulator's outputs (the owner of the MAMEOutput window, or of the listening TCP
/// port) and names it with <see cref="EmulatorNames.Identify"/>. Every call is best effort: anything that fails
/// (access denied, the process already gone) falls back to the protocol's own name, as before.
/// </summary>
public static class EmulatorProcess
{
    /// <summary>The emulator that owns this window.</summary>
    public static string ForWindow(IntPtr hwnd, string protocolDefault)
    {
        try
        {
            GetWindowThreadProcessId(hwnd, out uint pid);
            return Name(pid == 0 ? null : (int)pid, protocolDefault);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            AppLog.Warn($"Lamps: could not tell which emulator owns the output window ({ex.Message}).");
            return protocolDefault;
        }
    }

    /// <summary>The emulator listening on this TCP port of this computer (IPv4 or IPv6).</summary>
    public static string ForListener(int port, string protocolDefault)
    {
        try { return Name(ListenerPid(port), protocolDefault); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            AppLog.Warn($"Lamps: could not tell which emulator listens on port {port} ({ex.Message}).");
            return protocolDefault;
        }
    }

    private static string Name(int? pid, string protocolDefault)
    {
        if (pid is not { } id || id == Environment.ProcessId) return protocolDefault;
        string? exe = null;
        try { using var p = Process.GetProcessById(id); exe = p.ProcessName; }
        catch (ArgumentException) { return protocolDefault; } // gone
        catch (InvalidOperationException) { return protocolDefault; }
        bool blaster = HasModule(id, "OutputBlaster.dll");
        var name = EmulatorNames.Identify(exe, blaster, protocolDefault);
        AppLog.Info($"Lamps: outputs come from {exe}.exe (pid {id}{(blaster ? ", OutputBlaster.dll loaded" : "")}) → {name}.");
        return name;
    }

    // ---- loaded modules (32-bit games from this 64-bit process too)

    private static bool HasModule(int pid, string module)
    {
        var snap = CreateToolhelp32Snapshot(TH32CS_SNAPMODULE | TH32CS_SNAPMODULE32, (uint)pid);
        if (snap == InvalidHandle) return false; // access denied (an elevated game), or gone
        try
        {
            var entry = new MODULEENTRY32W { dwSize = (uint)Marshal.SizeOf<MODULEENTRY32W>() };
            for (bool ok = Module32FirstW(snap, ref entry); ok; ok = Module32NextW(snap, ref entry))
                if (string.Equals(entry.szModule, module, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
        finally { CloseHandle(snap); }
    }

    // ---- TCP listeners

    private static int? ListenerPid(int port)
    {
        foreach (var family in new[] { AF_INET, AF_INET6 })
            if (ListenerPid(port, family) is { } pid) return pid;
        return null;
    }

    private static int? ListenerPid(int port, int family)
    {
        int size = 0;
        GetExtendedTcpTable(IntPtr.Zero, ref size, false, family, TCP_TABLE_OWNER_PID_LISTENER, 0);
        if (size <= 0) return null;
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (GetExtendedTcpTable(buffer, ref size, false, family, TCP_TABLE_OWNER_PID_LISTENER, 0) != 0) return null;
            int count = Marshal.ReadInt32(buffer);
            // MIB_TCPROW_OWNER_PID: state, localAddr, localPort, remoteAddr, remotePort, pid (6 DWORDs).
            // MIB_TCP6ROW_OWNER_PID: localAddr[16], localScopeId, localPort, remoteAddr[16], remoteScopeId,
            // remotePort, state, pid.
            int rowSize = family == AF_INET ? 24 : 56;
            int portAt = family == AF_INET ? 8 : 20;
            int pidAt = family == AF_INET ? 20 : 52;
            for (int i = 0; i < count; i++)
            {
                var row = buffer + 4 + i * rowSize;
                int raw = Marshal.ReadInt32(row + portAt);
                int localPort = ((raw & 0xFF) << 8) | ((raw >> 8) & 0xFF); // network byte order, low 16 bits
                if (localPort == port) return Marshal.ReadInt32(row + pidAt);
            }
            return null;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private const int AF_INET = 2, AF_INET6 = 23, TCP_TABLE_OWNER_PID_LISTENER = 3;
    private const uint TH32CS_SNAPMODULE = 0x8, TH32CS_SNAPMODULE32 = 0x10;
    private static readonly IntPtr InvalidHandle = new(-1);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MODULEENTRY32W
    {
        public uint dwSize, th32ModuleID, th32ProcessID, GlblcntUsage, ProccntUsage;
        public IntPtr modBaseAddr;
        public uint modBaseSize;
        public IntPtr hModule;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szModule;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExePath;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool order, int family, int tableClass, uint reserved);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Module32FirstW(IntPtr snapshot, ref MODULEENTRY32W entry);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Module32NextW(IntPtr snapshot, ref MODULEENTRY32W entry);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
