using System.Windows.Media;
using LedNext.Core;

namespace LedNext.App;

internal static class ColorUtil
{
    public static Color ToMedia(this Rgb c) => Color.FromRgb(c.R, c.G, c.B);

    public static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    public static readonly SolidColorBrush RedBrush = Frozen(Color.FromRgb(0xF0, 0x50, 0x50));
    public static readonly SolidColorBrush GreenBrush = Frozen(Color.FromRgb(0x50, 0xD0, 0x60));
    public static readonly SolidColorBrush BlueBrush = Frozen(Color.FromRgb(0x50, 0x90, 0xFF));

    public static SolidColorBrush ChannelBrush(ColorChannel ch) => ch switch
    {
        ColorChannel.R => RedBrush,
        ColorChannel.G => GreenBrush,
        _ => BlueBrush,
    };
}
