using System.Globalization;

namespace LedNext.Core;

/// <summary>An 8-bit-per-channel colour.</summary>
public readonly record struct Rgb(byte R, byte G, byte B)
{
    public static readonly Rgb Black = new(0, 0, 0);
    public static readonly Rgb White = new(255, 255, 255);
    public static readonly Rgb Red = new(255, 0, 0);
    public static readonly Rgb Green = new(0, 255, 0);
    public static readonly Rgb Blue = new(0, 0, 255);

    public byte this[ColorChannel channel] => channel switch
    {
        ColorChannel.R => R,
        ColorChannel.G => G,
        _ => B,
    };

    public Rgb Scale(double factor)
    {
        factor = Math.Clamp(factor, 0, 1);
        return new((byte)Math.Round(R * factor), (byte)Math.Round(G * factor), (byte)Math.Round(B * factor));
    }

    /// <summary>Hue in degrees (any value, wraps), saturation and value 0..1.</summary>
    public static Rgb FromHsv(double hue, double saturation, double value)
    {
        hue = ((hue % 360) + 360) % 360;
        double c = value * saturation;
        double x = c * (1 - Math.Abs(hue / 60 % 2 - 1));
        double m = value - c;
        (double r, double g, double b) = (int)(hue / 60) switch
        {
            0 => (c, x, 0d),
            1 => (x, c, 0d),
            2 => (0d, c, x),
            3 => (0d, x, c),
            4 => (x, 0d, c),
            _ => (c, 0d, x),
        };
        return new(ToByte(r + m), ToByte(g + m), ToByte(b + m));
    }

    public string ToHex() => $"#{R:X2}{G:X2}{B:X2}";

    public static Rgb ParseHex(string hex)
    {
        var s = hex.Trim().TrimStart('#');
        if (s.Length != 6 || !uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v))
            throw new FormatException($"Not a #RRGGBB colour: '{hex}'");
        return new((byte)(v >> 16), (byte)(v >> 8), (byte)v);
    }

    public override string ToString() => ToHex();

    private static byte ToByte(double v) => (byte)Math.Round(Math.Clamp(v, 0, 1) * 255);
}
