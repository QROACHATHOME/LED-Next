using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace LedNext.Hardware.Hid;

/// <summary>One HID interface Windows lists: its path and USB ids, plus how long its output reports are.</summary>
internal sealed record HidDeviceInfo(string Path, int VendorId, int ProductId, int OutputReportLength);

/// <summary>
/// Lists HID devices and writes output reports, with Windows' own hid.dll / SetupAPI (no driver or library to
/// install). Used for the LED-Wiz family.
/// </summary>
internal static class HidDevices
{
    /// <summary>Every HID interface with the given vendor id (devices that can't be opened are skipped).</summary>
    public static List<HidDeviceInfo> List(int vendorId)
    {
        var found = new List<HidDeviceInfo>();
        HidD_GetHidGuid(out var guid);
        var set = SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
        if (set == new IntPtr(-1)) return found;
        try
        {
            var iface = new SP_DEVICE_INTERFACE_DATA { cbSize = Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };
            for (uint i = 0; SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, i, ref iface); i++)
            {
                var path = InterfacePath(set, ref iface);
                if (path is null) continue;
                // Opened with no access rights just to read the ids: works even for keyboards and mice Windows holds.
                using var h = Open(path, 0);
                if (h.IsInvalid) continue;
                var attrs = new HIDD_ATTRIBUTES { Size = Marshal.SizeOf<HIDD_ATTRIBUTES>() };
                if (!HidD_GetAttributes(h, ref attrs) || attrs.VendorID != vendorId) continue;
                int outLength = 0;
                if (HidD_GetPreparsedData(h, out var pre))
                {
                    try { if (HidP_GetCaps(pre, out var caps) == HIDP_STATUS_SUCCESS) outLength = caps.OutputReportByteLength; }
                    finally { HidD_FreePreparsedData(pre); }
                }
                found.Add(new HidDeviceInfo(path, attrs.VendorID, attrs.ProductID, outLength));
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        return found;
    }

    public static SafeFileHandle OpenForWrite(string path) => Open(path, GENERIC_WRITE);

    /// <summary>Writes one output report (its first byte is the report id, 0 for devices without ids).</summary>
    public static void Write(SafeFileHandle handle, byte[] report)
    {
        if (!WriteFile(handle, report, report.Length, out int written, IntPtr.Zero) || written != report.Length)
            throw new IOException($"HID write failed (error {Marshal.GetLastWin32Error()})");
    }

    private static SafeFileHandle Open(string path, uint access) =>
        CreateFile(path, access, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);

    private static string? InterfacePath(IntPtr set, ref SP_DEVICE_INTERFACE_DATA iface)
    {
        SetupDiGetDeviceInterfaceDetail(set, ref iface, IntPtr.Zero, 0, out int size, IntPtr.Zero);
        if (size <= 0) return null;
        var buf = Marshal.AllocHGlobal(size);
        try
        {
            // cbSize of SP_DEVICE_INTERFACE_DETAIL_DATA_W: 8 on 64-bit, 6 on 32-bit (a DWORD and one WCHAR, packed).
            Marshal.WriteInt32(buf, IntPtr.Size == 8 ? 8 : 6);
            if (!SetupDiGetDeviceInterfaceDetail(set, ref iface, buf, size, out _, IntPtr.Zero)) return null;
            return Marshal.PtrToStringUni(buf + 4);
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    // ---- Win32

    private const int DIGCF_PRESENT = 0x2, DIGCF_DEVICEINTERFACE = 0x10;
    private const uint GENERIC_WRITE = 0x40000000, FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2, OPEN_EXISTING = 3;
    private const int HIDP_STATUS_SUCCESS = 0x00110000;

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVICE_INTERFACE_DATA
    {
        public int cbSize;
        public Guid InterfaceClassGuid;
        public int Flags;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HIDD_ATTRIBUTES
    {
        public int Size;
        public ushort VendorID;
        public ushort ProductID;
        public ushort VersionNumber;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HIDP_CAPS
    {
        public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes, NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices,
            NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices,
            NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
    }

    [DllImport("hid.dll")] private static extern void HidD_GetHidGuid(out Guid guid);
    [DllImport("hid.dll")] private static extern bool HidD_GetAttributes(SafeFileHandle h, ref HIDD_ATTRIBUTES attrs);
    [DllImport("hid.dll")] private static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr data);
    [DllImport("hid.dll")] private static extern bool HidD_FreePreparsedData(IntPtr data);
    [DllImport("hid.dll")] private static extern int HidP_GetCaps(IntPtr data, out HIDP_CAPS caps);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SetupDiGetClassDevs(ref Guid guid, IntPtr enumerator, IntPtr parent, int flags);
    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr devInfo, ref Guid guid, uint index, ref SP_DEVICE_INTERFACE_DATA data);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SP_DEVICE_INTERFACE_DATA data, IntPtr detail, int size, out int required, IntPtr devInfo);
    [DllImport("setupapi.dll")] private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteFile(SafeFileHandle h, byte[] buffer, int count, out int written, IntPtr overlapped);
}
