namespace LedNext.Core;

public enum HardwareMode { Simulator, Hardware }

public sealed class OutputSettings
{
    /// <summary>0..1, applied to button colours (not to raw pin tests).</summary>
    public double Brightness { get; set; } = 1.0;
    /// <summary>1.0 = linear. ~2.2 makes fades look more even on most LEDs.</summary>
    public double Gamma { get; set; } = 1.0;
    public int RefreshHz { get; set; } = 30;
}

public sealed class HardwareSettings
{
    public HardwareMode Mode { get; set; } = HardwareMode.Simulator;
    /// <summary>Full path to PacDrive64.dll / PacDrive32.dll. Empty = look next to the exe.</summary>
    public string? PacDriveDllPath { get; set; }
}

/// <summary>Everything about the physical cabinet: panel layout, wiring and output settings.</summary>
public sealed class CabinetConfig
{
    /// <summary>
    /// 1 = phase 1. 2 = adds <see cref="ControlMap"/> and <see cref="Games"/> (both default to empty,
    /// so a version 1 file loads unchanged). 3 = adds the LaunchBox folder and platform map to Games.
    /// </summary>
    public const int CurrentVersion = 3;

    public int Version { get; set; } = CurrentVersion;
    public PanelLayout Panel { get; set; } = new();
    public WiringMap Wiring { get; set; } = new();
    /// <summary>Which panel buttons light for each logical game control (P1_BUTTON1…).</summary>
    public LedNext.Core.Games.ControlMap ControlMap { get; set; } = new();
    public OutputSettings Output { get; set; } = new();
    public HardwareSettings Hardware { get; set; } = new();
    public GameSettings Games { get; set; } = new();
}

public sealed class GameSettings
{
    /// <summary>LEDBlinky install folder last imported from (read only, never written to).</summary>
    public string? LedBlinkyFolder { get; set; }
    /// <summary>LaunchBox install folder (read only), e.g. D:\LaunchBox. Added in config version 3.</summary>
    public string? LaunchBoxFolder { get; set; }
    /// <summary>Your corrections: LaunchBox platform name → LED-Next platform key. Added in config version 3.</summary>
    public Dictionary<string, string> LaunchBoxPlatformMap { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
