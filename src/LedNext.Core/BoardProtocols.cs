namespace LedNext.Core;

/// <summary>
/// The bytes LED-Next sends to the boards that don't go through an SDK. Pure, unit-tested; the HID and serial
/// writes live in LedNext.Hardware.
/// </summary>
public static class LedWizProtocol
{
    /// <summary>LED-Wiz brightness runs 0–48 (49–132 are its own flashing modes, never used).</summary>
    public const int MaxLevel = 48;
    /// <summary>The SBA command's first byte; a PBA chunk never starts with it, since levels stop at 48.</summary>
    public const byte SbaCommand = 64;
    /// <summary>Pulse speed for the board's own flashing (1–7). Unused, since no output is set to flash.</summary>
    public const byte PulseSpeed = 2;
    public const int Outputs = 32;

    public static byte Level(byte value) => (byte)Math.Round(value * MaxLevel / 255.0);

    /// <summary>
    /// One frame as five 8-byte reports. First SBA, which switches all 32 outputs on and also points the board back at
    /// output 1. Then PBA in four chunks of 8 levels, outputs 1–8 … 25–32. Brightness 0 is dark, so "on" never shows.
    /// Sending SBA every frame keeps the board aligned if a chunk was ever lost.
    /// </summary>
    public static byte[][] Frame(ReadOnlySpan<byte> intensities)
    {
        var reports = new byte[5][];
        reports[0] = [SbaCommand, 0xFF, 0xFF, 0xFF, 0xFF, PulseSpeed, 0, 0];
        for (int chunk = 0; chunk < 4; chunk++)
        {
            var r = new byte[8];
            for (int i = 0; i < 8; i++)
            {
                int pin = chunk * 8 + i;
                r[i] = pin < intensities.Length ? Level(intensities[pin]) : (byte)0;
            }
            reports[chunk + 1] = r;
        }
        return reports;
    }
}

/// <summary>
/// Adalight: "Ada", LED count − 1 (high, low), checksum high ^ low ^ 0x55, then R, G, B per LED. Read by the Adalight
/// sketches for Arduino / Teensy (FastLED) and by WLED on its USB serial port.
/// </summary>
public static class AdalightProtocol
{
    public static byte[] Frame(ReadOnlySpan<byte> rgb, int leds)
    {
        leds = Math.Max(1, leds);
        var frame = new byte[6 + leds * 3];
        frame[0] = (byte)'A'; frame[1] = (byte)'d'; frame[2] = (byte)'a';
        int n = leds - 1;
        frame[3] = (byte)(n >> 8);
        frame[4] = (byte)(n & 0xFF);
        frame[5] = (byte)(frame[3] ^ frame[4] ^ 0x55);
        rgb[..Math.Min(rgb.Length, leds * 3)].CopyTo(frame.AsSpan(6));
        return frame;
    }
}

public static class PacDriveProtocol
{
    /// <summary>The PacDrive's 16 outputs as PacSetLEDStates' bits (bit 0 = output 1): on at the threshold and above.</summary>
    public static ushort States(ReadOnlySpan<byte> intensities, byte threshold = BoardModels.OnOffThreshold)
    {
        int bits = 0;
        for (int i = 0; i < 16 && i < intensities.Length; i++)
            if (intensities[i] >= threshold) bits |= 1 << i;
        return (ushort)bits;
    }
}
