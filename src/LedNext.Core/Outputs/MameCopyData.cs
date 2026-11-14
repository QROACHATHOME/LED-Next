using System.Text;

namespace LedNext.Core.Outputs;

/// <summary>
/// The reply to MAME's "MAMEOutputGetIDString" (WM_COPYDATA): a 4-byte id, then the name, ended by a NUL.
/// MAME sends exactly that; Supermodel and Output Blaster send a bigger block with leftover bytes after the NUL
/// ("scudplus\0 do", "LampStart\0ÍÍ"), so the name ends at the first NUL, not at the block's end.
/// </summary>
public static class MameCopyData
{
    public static (int Id, string Name)? ParseIdString(ReadOnlySpan<byte> data)
    {
        if (data.Length < 4) return null;
        int id = BitConverter.ToInt32(data[..4]);
        var text = data[4..];
        int end = text.IndexOf((byte)0);
        if (end >= 0) text = text[..end];
        return (id, Encoding.Latin1.GetString(text).Trim());
    }
}
