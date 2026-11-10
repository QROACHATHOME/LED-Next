using System.Text.Json;

namespace LedNext.Core;

/// <summary>Where the main window was, in device-independent pixels.</summary>
public sealed record WindowBounds(double Left, double Top, double Width, double Height, bool Maximized = false);

/// <summary>
/// Saves and restores the main window's position (<c>config/window.json</c>, per machine, so not part
/// of cabinet.json) and keeps it on screen: a window found at x 919–2359 on a 1920-wide display hid
/// half the Panel tab.
/// </summary>
public static class WindowPlacement
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>Moves (and if needed shrinks) the window so all of it is inside <paramref name="area"/>.</summary>
    public static WindowBounds Clamp(WindowBounds w, WindowBounds area, double minWidth = 0, double minHeight = 0)
    {
        double width = Math.Max(Math.Min(w.Width, area.Width), Math.Min(minWidth, area.Width));
        double height = Math.Max(Math.Min(w.Height, area.Height), Math.Min(minHeight, area.Height));
        double left = Math.Clamp(w.Left, area.Left, area.Left + area.Width - width);
        double top = Math.Clamp(w.Top, area.Top, area.Top + area.Height - height);
        return w with { Left = left, Top = top, Width = width, Height = height };
    }

    public static WindowBounds? Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var b = JsonSerializer.Deserialize<WindowBounds>(File.ReadAllText(path), Json);
            return b is { Width: > 0, Height: > 0 } && double.IsFinite(b.Left) && double.IsFinite(b.Top) ? b : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            AppLog.Warn($"Could not read the window position from {path}: {ex.Message}");
            return null;
        }
    }

    public static void Save(string path, WindowBounds bounds)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(path, JsonSerializer.Serialize(bounds, Json));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn($"Could not save the window position to {path}: {ex.Message}");
        }
    }
}
