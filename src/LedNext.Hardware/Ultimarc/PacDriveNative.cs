using System.Reflection;
using System.Runtime.InteropServices;
using LedNext.Core;

namespace LedNext.Hardware.Ultimarc;

/// <summary>
/// P/Invoke into Ultimarc's PacDrive SDK (PacDrive64.dll / PacDrive32.dll).
///
/// UNVERIFIED: these signatures follow the PacDrive SDK as documented by Ultimarc but have not yet
/// been run against real boards. See TASKS/00_ACTIVE_PRIORITIES.md, "Verify on the arcade machine".
/// "id" is the SDK's device index (0..PacInitialize()-1), not the PacLED64 board ID.
/// </summary>
internal static class PacDriveNative
{
    private const string Lib = "PacDrive"; // resolved to PacDrive64.dll / PacDrive32.dll below

    private static readonly object Gate = new();
    private static bool _resolverInstalled;
    private static string? _configuredPath;

    public const int UltimarcVendorId = 0xD209;
    public const int PacLed64FirstProductId = 0x1401; // board ID 1; 0x1402..0x1404 = IDs 2..4

    public static string DefaultFileName => Environment.Is64BitProcess ? "PacDrive64.dll" : "PacDrive32.dll";

    /// <summary>Points the loader at a specific DLL (or null for the exe folder). Call before the first native call.</summary>
    public static void Configure(string? dllPath)
    {
        lock (Gate)
        {
            _configuredPath = string.IsNullOrWhiteSpace(dllPath) ? null : dllPath;
            if (_resolverInstalled) return;
            NativeLibrary.SetDllImportResolver(typeof(PacDriveNative).Assembly, Resolve);
            _resolverInstalled = true;
        }
    }

    /// <summary>Where the loader will look, for error messages.</summary>
    public static string ExpectedPath =>
        _configuredPath ?? Path.Combine(AppContext.BaseDirectory, DefaultFileName);

    private static IntPtr Resolve(string name, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (name != Lib) return IntPtr.Zero;
        var path = ExpectedPath;
        if (NativeLibrary.TryLoad(path, out var handle))
        {
            AppLog.Info($"Loaded {path}");
            return handle;
        }
        // Fall back to the normal search order (PATH etc.).
        if (NativeLibrary.TryLoad(DefaultFileName, assembly, searchPath, out handle))
        {
            AppLog.Info($"Loaded {DefaultFileName} from the system search path");
            return handle;
        }
        throw new DllNotFoundException(
            $"{DefaultFileName} not found. Download Ultimarc's PacDrive SDK and copy {DefaultFileName} to {AppContext.BaseDirectory}, " +
            "or set hardware.pacDriveDllPath in config/cabinet.json.");
    }

    /// <summary>Enumerates devices. Returns the number of Ultimarc devices found.</summary>
    [DllImport(Lib, CallingConvention = CallingConvention.StdCall)]
    public static extern int PacInitialize();

    [DllImport(Lib, CallingConvention = CallingConvention.StdCall)]
    public static extern void PacShutdown();

    [DllImport(Lib, CallingConvention = CallingConvention.StdCall)]
    public static extern int PacGetDeviceType(int id);

    [DllImport(Lib, CallingConvention = CallingConvention.StdCall)]
    public static extern int PacGetVendorId(int id);

    [DllImport(Lib, CallingConvention = CallingConvention.StdCall)]
    public static extern int PacGetProductId(int id);

    [DllImport(Lib, CallingConvention = CallingConvention.StdCall)]
    public static extern int PacGetVersionNumber(int id);

    /// <summary>Sets one LED. Port is 0-based (0..63).</summary>
    [DllImport(Lib, CallingConvention = CallingConvention.StdCall)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool Pac64SetLEDIntensity(int id, int port, byte intensity);

    /// <summary>Sets all 64 LEDs in one call. Not present in every SDK build — callers fall back to per-port writes.</summary>
    [DllImport(Lib, CallingConvention = CallingConvention.StdCall)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool Pac64SetLEDIntensities(int id, byte[] data);

    /// <summary>Hardware fade time between intensity changes. 0 = instant.</summary>
    [DllImport(Lib, CallingConvention = CallingConvention.StdCall)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool Pac64SetLEDFadeTime(int id, byte fadeTime);
}
