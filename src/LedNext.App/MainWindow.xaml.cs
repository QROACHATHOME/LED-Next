using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using LedNext.Core;

namespace LedNext.App;

public partial class MainWindow : Window
{
    private readonly string? _placementPath;

    public MainWindow() => InitializeComponent();

    /// <param name="placementPath">Where the window position is kept (config/window.json, per machine).</param>
    public MainWindow(string placementPath) : this()
    {
        _placementPath = placementPath;
        if (WindowPlacement.Load(placementPath) is { } saved)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = saved.Left; Top = saved.Top; Width = saved.Width; Height = saved.Height;
            if (saved.Maximized) WindowState = WindowState.Maximized;
        }
        Loaded += (_, _) => KeepOnScreen();
        Closing += (_, _) => SavePlacement();
    }

    /// <summary>
    /// Moves (and if needed shrinks) the window so it lies inside the work area of the monitor nearest
    /// to it. Done in physical pixels, so display scaling and several monitors don't matter. Also covers
    /// the first start (no saved position) on a screen smaller than 1440×860.
    /// </summary>
    private void KeepOnScreen()
    {
        if (WindowState != WindowState.Normal) return;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var r)) return;
        var monitor = MonitorFromRect(ref r, MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info)) return;

        var dpi = VisualTreeHelper.GetDpi(this);
        var now = new WindowBounds(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
        var work = new WindowBounds(info.rcWork.Left, info.rcWork.Top, info.rcWork.Right - info.rcWork.Left, info.rcWork.Bottom - info.rcWork.Top);
        var b = WindowPlacement.Clamp(now, work, MinWidth * dpi.DpiScaleX, MinHeight * dpi.DpiScaleY);
        if (b == now) return;
        SetWindowPos(hwnd, IntPtr.Zero, (int)b.Left, (int)b.Top, (int)b.Width, (int)b.Height, SWP_NOZORDER | SWP_NOACTIVATE);
        AppLog.Info($"Window was partly off-screen ({Describe(now)} px); moved to {Describe(b)} px.");
    }

    private void SavePlacement()
    {
        if (_placementPath is null) return;
        var r = WindowState == WindowState.Normal ? new Rect(Left, Top, ActualWidth, ActualHeight) : RestoreBounds;
        if (r.IsEmpty || r.Width <= 0) return;
        WindowPlacement.Save(_placementPath, new WindowBounds(r.Left, r.Top, r.Width, r.Height, WindowState == WindowState.Maximized));
    }

    private static string Describe(WindowBounds b) => $"x {b.Left:0}–{b.Left + b.Width:0}, y {b.Top:0}–{b.Top + b.Height:0}";

    // ---- Win32 ----------------------------------------------------------------------------------

    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const uint SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }

    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromRect(ref RECT rect, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
}
