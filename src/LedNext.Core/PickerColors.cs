namespace LedNext.Core;

/// <summary>
/// The colour picker's 16 "custom colours" slots, kept in cabinet.json (<see cref="GameSettings.PickerColors"/>) as
/// #RRGGBB. Windows' colour dialog holds them as 0x00BBGGRR ints, with white in an unused slot.
/// </summary>
public static class PickerColors
{
    public const int Slots = 16;
    private const int Empty = 0xFFFFFF;

    /// <summary>The saved colours as the dialog's slots (bad entries skipped, the rest white).</summary>
    public static int[] ToDialog(IEnumerable<string>? saved)
    {
        var slots = Enumerable.Repeat(Empty, Slots).ToArray();
        int i = 0;
        foreach (var hex in saved ?? [])
        {
            if (i == Slots) break;
            try { var c = Rgb.ParseHex(hex); slots[i++] = c.R | c.G << 8 | c.B << 16; }
            catch (FormatException) { }
        }
        return slots;
    }

    /// <summary>The dialog's slots as #RRGGBB, without the trailing unused (white) ones.</summary>
    public static List<string> FromDialog(int[]? slots)
    {
        var list = (slots ?? []).Take(Slots)
            .Select(v => new Rgb((byte)v, (byte)(v >> 8), (byte)(v >> 16)).ToHex()).ToList();
        while (list.Count > 0 && list[^1] == Rgb.White.ToHex()) list.RemoveAt(list.Count - 1);
        return list;
    }
}
