using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LedNext.Core;
using LedNext.Core.Games;

namespace LedNext.App;

/// <summary>Reads a folder of button banners (<c>&lt;rom&gt;.png</c>) with WPF's PNG decoder. Only reads files.</summary>
public static class ButtonBannerReader
{
    /// <summary>Changes when files are added, removed or replaced, so an unchanged folder isn't read again.</summary>
    public static string Stamp(string folder)
    {
        var files = Directory.EnumerateFiles(folder, "*.png").Select(f => new FileInfo(f)).ToList();
        long newest = files.Count == 0 ? 0 : files.Max(f => f.LastWriteTimeUtc.Ticks);
        return $"{files.Count}:{newest}:{files.Sum(f => f.Length)}";
    }

    /// <summary>rom → used button positions, for every banner that could be read. Unreadable files are counted.</summary>
    public static Dictionary<string, IReadOnlyList<int>> Read(string folder, IProgress<string>? progress, out int skipped)
    {
        var result = new Dictionary<string, IReadOnlyList<int>>(StringComparer.OrdinalIgnoreCase);
        skipped = 0;
        var files = Directory.EnumerateFiles(folder, "*.png").ToList();
        for (int i = 0; i < files.Count; i++)
        {
            if (i % 50 == 0) progress?.Report($"Reading button banners… {i:N0} of {files.Count:N0}");
            try
            {
                var used = UsedPositions(files[i]);
                if (used is null) { skipped++; continue; }
                result[Path.GetFileNameWithoutExtension(files[i])] = used;
            }
            catch (Exception ex) when (ex is IOException or NotSupportedException or FileFormatException or UnauthorizedAccessException or ArgumentException)
            {
                skipped++;
                AppLog.Warn($"Button banner {Path.GetFileName(files[i])} could not be read: {ex.Message}");
            }
        }
        return result;
    }

    private static IReadOnlyList<int>? UsedPositions(string file)
    {
        using var stream = File.OpenRead(file);
        var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
        var bgra = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        int w = bgra.PixelWidth, h = bgra.PixelHeight, stride = w * 4;
        var px = new byte[stride * h];
        bgra.CopyPixels(px, stride, 0);
        return ButtonBanners.UsedPositions(w, h, (x, y) =>
        {
            int o = y * stride + x * 4;
            return (px[o + 2], px[o + 1], px[o], px[o + 3]);
        });
    }

    /// <summary>A picture for display, decoded small and not locked on disk (any thread), or null.</summary>
    public static ImageSource? Load(string file, int decodeWidth = 400)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(file);
            image.DecodePixelWidth = decodeWidth;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or FileFormatException or UnauthorizedAccessException or UriFormatException)
        {
            return null;
        }
    }
}
