namespace LedNext.Core;

/// <summary>How LED-Next talks to a board.</summary>
public enum BoardFamily
{
    /// <summary>Ultimarc's PacDrive SDK (PacDrive64.dll): PacLED64, Ultimate I/O, NanoLED, PacDrive.</summary>
    Ultimarc,
    /// <summary>The LED-Wiz USB protocol (HID): GroovyGameGear LED-Wiz, Pinscape in LED-Wiz mode, clones.</summary>
    LedWiz,
    /// <summary>Addressable RGB LEDs behind a serial port, Adalight protocol (Arduino, Teensy, WLED).</summary>
    Strip,
}

/// <summary>
/// The LED boards LED-Next knows: how many pins, which board IDs, and how each shows up on USB.
/// Pure data and logic, unit-tested; the SDK, HID and serial calls live in LedNext.Hardware.
/// </summary>
public sealed record BoardModel(string Model, string DisplayName, int PinCount, int MaxBoardId, string Notes,
    BoardFamily Family = BoardFamily.Ultimarc, bool OnOffOnly = false)
{
    public string KeyFor(int boardId) => $"{Model}#{boardId}";
    /// <summary>The name lists and screen readers show.</summary>
    public override string ToString() => DisplayName;
    public string NameFor(int boardId) => $"{DisplayName} #{boardId}";

    /// <summary>A strip's pins depend on its LED count; every other model has a fixed pin count.</summary>
    public bool IsStrip => Family == BoardFamily.Strip;

    public BoardDefinition Definition(int boardId, double graphX = 0, double graphY = 0) =>
        new() { Key = KeyFor(boardId), Model = Model, Name = NameFor(boardId), PinCount = PinCount, GraphX = graphX, GraphY = graphY };
}

/// <summary>What the PacDrive SDK (or a USB scan) says about one connected device, read as an LED board.</summary>
public sealed record DetectedBoard(BoardModel Model, int BoardId, string? Mode)
{
    public string Key => Model.KeyFor(BoardId);
}

public static class BoardModels
{
    public const int UltimarcVendorId = 0xD209;

    /// <summary>PacLED64: 64 pins (21 RGB LEDs). USB product 0x1401 + board ID − 1, IDs 1–4. Verified on the cabinet.</summary>
    public static readonly BoardModel PacLed64 = new("PacLED64", "PacLED64", 64, 4,
        "Verified on real boards.");

    /// <summary>
    /// I-PAC Ultimate I/O: keyboard/joystick encoder with 96 LED pins (32 RGB LEDs), 256 levels. Same PacDrive SDK
    /// calls as the PacLED64 (SDK device type 6; 96 bytes for Pac64SetLEDIntensities). USB product
    /// 0x0410 + (DirectInput mode ? 2 : 0) + (board ID − 1), IDs 1–2 (ID 2 needs Ultimarc's "ID #2" firmware).
    /// In XInput mode it shows up as an Xbox 360 pad with no LEDs at all. Sources: Ultimarc's PacDrive SDK readme
    /// (github.com/benbaker76/PacDriveSDK), LEDSpicer (src/devices/Ultimarc) and Ultimarc-linux. NOT TESTED on a
    /// real board.
    /// </summary>
    public static readonly BoardModel UltimateIO = new("UltimateIO", "Ultimate I/O", 96, 2,
        "Written from Ultimarc's documentation; not yet tested on a real board.");

    /// <summary>
    /// NanoLED: 60 pins (20 RGB LEDs), 256 levels, the PacLED64's smaller sibling. SDK device type 4, the same Pac64
    /// calls with 60 bytes. USB product 0x1481 + board ID − 1, IDs 1–4 (LEDSpicer). NOT TESTED on a real board.
    /// </summary>
    public static readonly BoardModel NanoLed = new("NanoLED", "NanoLED", 60, 4,
        "Written from Ultimarc's documentation; not yet tested on a real board.");

    /// <summary>
    /// PacDrive: 16 outputs, on or off only (no dimming): an output is on when its value is at least
    /// <see cref="OnOffThreshold"/>. SDK device type 1, PacSetLEDStates. USB product 0x1500 + board ID − 1, IDs 1–4
    /// (LEDSpicer). NOT TESTED on a real board.
    /// </summary>
    public static readonly BoardModel PacDrive = new("PacDrive", "PacDrive", 16, 4,
        "On / off only (no dimming or fades). Not yet tested on a real board.", OnOffOnly: true);

    /// <summary>
    /// LED-Wiz: 32 outputs, 49 levels (0–48). USB vendor 0xFAFA, product 0x00F0 + unit − 1, units 1–16. Pinscape
    /// (KL25Z and Pico) in LED-Wiz mode and the LED-Wiz clones use the same IDs and protocol (a Pinscape's default
    /// unit is 8). Sources: LEDSpicer (src/devices/GroovyGameGear) and DOF. NOT TESTED on a real board.
    /// </summary>
    public static readonly BoardModel LedWiz = new("LEDWiz", "LED-Wiz", 32, 16,
        "Also Pinscape in LED-Wiz mode and LED-Wiz clones; the ID is the unit number. Not yet tested on a real board.",
        BoardFamily.LedWiz);

    /// <summary>
    /// Addressable RGB LEDs (WS2812 / SK6812…) on a microcontroller behind a serial port, Adalight protocol: an
    /// Arduino or Teensy with an Adalight sketch, or WLED (which reads Adalight on its USB port). Three pins per LED.
    /// The ID only tells strips apart. NOT TESTED on a real strip.
    /// </summary>
    public static readonly BoardModel LedStrip = new("LedStrip", "LED strip", DefaultStripLeds * 3, 8,
        "Adalight over a serial port (Arduino, Teensy, WLED). Three pins per LED. Not yet tested on a real strip.",
        BoardFamily.Strip);

    public static IReadOnlyList<BoardModel> All { get; } = new[] { PacLed64, UltimateIO, NanoLed, PacDrive, LedWiz, LedStrip };

    public static BoardModel? Find(string? model) =>
        All.FirstOrDefault(m => string.Equals(m.Model, model, StringComparison.OrdinalIgnoreCase));

    // ---- strips

    public const int DefaultStripLeds = 30, MaxStripLeds = 600, DefaultBaudRate = 115200;
    public static IReadOnlyList<int> BaudRates { get; } = [115200, 230400, 460800, 500000, 921600, 1000000, 2000000];

    /// <summary>Changes a strip's LED count; pins beyond the new end lose their wiring. Returns how many did.</summary>
    public static int SetStripLeds(WiringMap wiring, BoardDefinition strip, int leds)
    {
        leds = Math.Clamp(leds, 1, MaxStripLeds);
        strip.LedCount = leds;
        strip.PinCount = leds * 3;
        if (strip.Segments is { } segments)
        {
            segments.RemoveAll(s => s.FirstLed > leds);
            foreach (var s in segments) s.Count = Math.Min(s.Count, leds - s.FirstLed + 1);
        }
        return wiring.Bindings.RemoveAll(b => b.BoardKey == strip.Key && b.Pin > strip.PinCount);
    }

    // ---- USB identification

    private const int PacLed64FirstProduct = 0x1401, NanoLedFirstProduct = 0x1481, PacDriveFirstProduct = 0x1500;
    private const int UltimateFirstProduct = 0x0410, UltimateLastProduct = 0x0413;
    /// <summary>PacDrive SDK device types (PacDrive.cs DeviceType).</summary>
    public const int SdkTypePacDrive = 1, SdkTypeNanoLed = 4, SdkTypePacLed64 = 5, SdkTypeUltimateIO = 6;

    public const int LedWizVendorId = 0xFAFA, LedWizFirstProduct = 0x00F0;

    /// <summary>A PacDrive output is on at this value and above (25 %), so dimmed colours still light it.</summary>
    public const byte OnOffThreshold = 64;

    /// <summary>Which LED board a PacDrive SDK device is, or null for anything else (a U-HID, a ServoStik…).</summary>
    public static DetectedBoard? Identify(int vendorId, int productId, int sdkType)
    {
        if (vendorId != UltimarcVendorId) return null;
        if (InRange(productId, PacLed64FirstProduct, PacLed64.MaxBoardId) is { } pacId) return new DetectedBoard(PacLed64, pacId, null);
        if (productId >= UltimateFirstProduct && productId <= UltimateLastProduct)
        {
            int offset = productId - UltimateFirstProduct;
            return new DetectedBoard(UltimateIO, (offset & 1) + 1, offset >= 2 ? "DirectInput" : "keyboard");
        }
        if (InRange(productId, NanoLedFirstProduct, NanoLed.MaxBoardId) is { } nanoId) return new DetectedBoard(NanoLed, nanoId, null);
        if (InRange(productId, PacDriveFirstProduct, PacDrive.MaxBoardId) is { } driveId) return new DetectedBoard(PacDrive, driveId, null);
        // A product id not seen before, but the SDK knows the type (newer firmware?): take it as board 1.
        return sdkType switch
        {
            SdkTypeUltimateIO => new DetectedBoard(UltimateIO, 1, $"unknown product {productId:X4}"),
            SdkTypeNanoLed => new DetectedBoard(NanoLed, 1, $"unknown product {productId:X4}"),
            SdkTypePacDrive => new DetectedBoard(PacDrive, 1, $"unknown product {productId:X4}"),
            _ => null,
        };
    }

    /// <summary>An LED-Wiz (or Pinscape / clone in LED-Wiz mode) by its USB ids, or null.</summary>
    public static DetectedBoard? IdentifyLedWiz(int vendorId, int productId) =>
        vendorId == LedWizVendorId && InRange(productId, LedWizFirstProduct, LedWiz.MaxBoardId) is { } unit
            ? new DetectedBoard(LedWiz, unit, null)
            : null;

    private static int? InRange(int productId, int first, int count)
    {
        int id = productId - first + 1;
        return id >= 1 && id <= count ? id : null;
    }

    // ---- adding and removing

    /// <summary>
    /// Adds a board to the wiring (to the right of the others in the wiring graph). Returns null when a board with
    /// that key is already there. A strip gets <paramref name="leds"/> LEDs (default 30) on <paramref name="serialPort"/>.
    /// </summary>
    public static BoardDefinition? Add(WiringMap wiring, BoardModel model, int boardId, int? leds = null,
        string? serialPort = null, int? baudRate = null)
    {
        if (boardId < 1 || boardId > model.MaxBoardId) throw new ArgumentOutOfRangeException(nameof(boardId));
        if (wiring.FindBoard(model.KeyFor(boardId)) is not null) return null;
        double x = wiring.Boards.Count == 0 ? 1150 : wiring.Boards.Max(b => b.GraphX) + 350;
        var def = model.Definition(boardId, x, 0);
        if (model.IsStrip)
        {
            int count = Math.Clamp(leds ?? DefaultStripLeds, 1, MaxStripLeds);
            def.LedCount = count;
            def.PinCount = count * 3;
            def.SerialPort = string.IsNullOrWhiteSpace(serialPort) ? null : serialPort.Trim();
            def.BaudRate = baudRate ?? DefaultBaudRate;
            def.Segments = new();
        }
        wiring.Boards.Add(def);
        return def;
    }

    /// <summary>Removes a board and every pin wired to it. Returns how many pin bindings went with it.</summary>
    public static int Remove(WiringMap wiring, string key)
    {
        int removed = wiring.Bindings.RemoveAll(b => b.BoardKey == key);
        wiring.Boards.RemoveAll(b => b.Key == key);
        return removed;
    }

    /// <summary>The pin's name for people: "Pin 7", or "LED 3 G" on a strip.</summary>
    public static string PinName(BoardDefinition board, int pin) =>
        Find(board.Model)?.IsStrip == true ? $"LED {(pin + 2) / 3} {"RGB"[(pin - 1) % 3]}" : $"Pin {pin}";
}
