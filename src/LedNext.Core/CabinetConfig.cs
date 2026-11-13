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
    /// 4 = adds Games.ExtraColorsIni. 5 = adds Games.ButtonBannersFolder. 6 = adds <see cref="Lamps"/>.
    /// 7 = adds Lamps.Maps (LED-Next's own lamp table, empty by default). 8 = adds <see cref="Speech"/> (off by default).
    /// 9 = adds Speech.PauseAfterNameSeconds (0, so speech sounds as before).
    /// </summary>
    public const int CurrentVersion = 9;

    public int Version { get; set; } = CurrentVersion;
    public PanelLayout Panel { get; set; } = new();
    public WiringMap Wiring { get; set; } = new();
    /// <summary>Which panel buttons light for each logical game control (P1_BUTTON1…).</summary>
    public LedNext.Core.Games.ControlMap ControlMap { get; set; } = new();
    public OutputSettings Output { get; set; } = new();
    public HardwareSettings Hardware { get; set; } = new();
    public GameSettings Games { get; set; } = new();
    /// <summary>Emulators' live lamp outputs (MAME, Supermodel, TeknoParrot via Output Blaster, Flycast). Version 6.</summary>
    public LampSettings Lamps { get; set; } = new();
    /// <summary>Speaking a game's button labels at launch, with the buttons blinking. Version 8.</summary>
    public LedNext.Core.Speech.SpeechSettings Speech { get; set; } = new();
}

/// <summary>Live lamps from emulators (MAME's output system). Added in config version 6.</summary>
public sealed class LampSettings
{
    public bool Enabled { get; set; } = true;
    /// <summary>MAME's Windows messages ("output windows" in mame.ini; also Supermodel, Output Blaster).</summary>
    public bool WindowMessages { get; set; } = true;
    /// <summary>MAME's network output ("output network"; also Flycast's network output), on <see cref="NetworkPort"/>.</summary>
    public bool Network { get; set; } = true;
    public int NetworkPort { get; set; } = 8000;
    /// <summary>A player's Start lamp also blinks that player's button 1 while it blinks (for Start buttons without LEDs).</summary>
    public bool StartLampsOnButton1 { get; set; } = true;
    /// <summary>
    /// LED-Next's own lamp table: emulator / game ("*" = all) / output → controls. For MAME it comes before
    /// LEDBlinky's MameOutputs.ini (per output); other emulators (Flycast…) use only this. Added in config version 7.
    /// </summary>
    public List<LedNext.Core.Outputs.LampMapEntry> Maps { get; set; } = new();
}

public sealed class GameSettings
{
    /// <summary>LEDBlinky install folder last imported from (read only, never written to).</summary>
    public string? LedBlinkyFolder { get; set; }
    /// <summary>LaunchBox install folder (read only), e.g. D:\LaunchBox. Added in config version 3.</summary>
    public string? LaunchBoxFolder { get; set; }
    /// <summary>Your corrections: LaunchBox platform name → LED-Next platform key. Added in config version 3.</summary>
    public Dictionary<string, string> LaunchBoxPlatformMap { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>
    /// A newer or community colors.ini kept outside the LEDBlinky folder (read only). Read with every LEDBlinky
    /// import; it only lights MAME games LEDBlinky has no lights for. Added in config version 4.
    /// </summary>
    public string? ExtraColorsIni { get; set; }
    /// <summary>
    /// Folder of "6 button layout" banners (<c>&lt;rom&gt;.png</c>, read only): shown in the Games tab, and MAME games
    /// without lights of their own light as many buttons as their banner uses. Added in config version 5.
    /// </summary>
    public string? ButtonBannersFolder { get; set; }
}
