namespace LedNext.Core;

/// <summary>
/// The LED boards LED-Next knows: how many pins, which board IDs, and how each shows up on USB.
/// Pure data and logic, unit-tested; the PacDrive SDK calls live in LedNext.Hardware.
/// </summary>
public sealed record BoardModel(string Model, string DisplayName, int PinCount, int MaxBoardId, string Notes)
{
    public string KeyFor(int boardId) => $"{Model}#{boardId}";
    /// <summary>The name lists and screen readers show.</summary>
    public override string ToString() => DisplayName;
    public string NameFor(int boardId) => $"{DisplayName} #{boardId}";

    public BoardDefinition Definition(int boardId, double graphX = 0, double graphY = 0) =>
        new() { Key = KeyFor(boardId), Model = Model, Name = NameFor(boardId), PinCount = PinCount, GraphX = graphX, GraphY = graphY };
}

/// <summary>What the PacDrive SDK says about one connected device, read as an LED board.</summary>
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

    public static IReadOnlyList<BoardModel> All { get; } = new[] { PacLed64, UltimateIO };

    public static BoardModel? Find(string? model) =>
        All.FirstOrDefault(m => string.Equals(m.Model, model, StringComparison.OrdinalIgnoreCase));

    private const int PacLed64FirstProduct = 0x1401;
    private const int UltimateFirstProduct = 0x0410, UltimateLastProduct = 0x0413;
    /// <summary>PacDrive SDK device types (PacDrive.cs DeviceType).</summary>
    public const int SdkTypePacLed64 = 5, SdkTypeUltimateIO = 6;

    /// <summary>Which LED board a PacDrive SDK device is, or null for anything else (a PacDrive, a U-HID…).</summary>
    public static DetectedBoard? Identify(int vendorId, int productId, int sdkType)
    {
        if (vendorId != UltimarcVendorId) return null;
        int pacId = productId - PacLed64FirstProduct + 1;
        if (pacId >= 1 && pacId <= PacLed64.MaxBoardId) return new DetectedBoard(PacLed64, pacId, null);
        if (productId >= UltimateFirstProduct && productId <= UltimateLastProduct)
        {
            int offset = productId - UltimateFirstProduct;
            return new DetectedBoard(UltimateIO, (offset & 1) + 1, offset >= 2 ? "DirectInput" : "keyboard");
        }
        // A product id not seen before, but the SDK calls it an Ultimate I/O (newer firmware?): take it as board 1.
        if (sdkType == SdkTypeUltimateIO) return new DetectedBoard(UltimateIO, 1, $"unknown product {productId:X4}");
        return null;
    }

    /// <summary>
    /// Adds a board to the wiring (to the right of the others in the wiring graph). Returns null when a board with
    /// that key is already there.
    /// </summary>
    public static BoardDefinition? Add(WiringMap wiring, BoardModel model, int boardId)
    {
        if (boardId < 1 || boardId > model.MaxBoardId) throw new ArgumentOutOfRangeException(nameof(boardId));
        if (wiring.FindBoard(model.KeyFor(boardId)) is not null) return null;
        double x = wiring.Boards.Count == 0 ? 1150 : wiring.Boards.Max(b => b.GraphX) + 350;
        var def = model.Definition(boardId, x, 0);
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
}
