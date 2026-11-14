namespace LedNext.Core.Outputs;

/// <summary>
/// Which emulator sent the outputs. Several emulators speak MAME's output protocols (network and Windows
/// messages) and all say "mame_start", so the protocol alone can't tell them apart; the process that sent them
/// can. <see cref="Identify"/> names it from its exe; LED-Next's lamp table (cabinet.json lamps.maps) uses
/// these names in its "emulator" column. Pure logic, unit-tested.
/// <list type="bullet">
/// <item>MAME (mame.exe, mame64.exe, groovymame, hbmame, arcade64 (MAMEUI)…).</item>
/// <item>Flycast (its own network dialect).</item>
/// <item>Odin (Sega Hikaru, github.com/Tovarichtch/Odin): MAME's network output, odin.ini "[outputs] network = on".</item>
/// <item>Keisho (Sega Model 1/2/3, github.com/Keisho-Emu/Keisho): both MAME protocols, MAME's rom and lamp names.</item>
/// <item>Supermodel (Sega Model 3), Model 2 (M2Emulator, through Output Blaster).</item>
/// <item>TeknoParrot: Output Blaster (OutputBlaster.dll) is loaded into the game's own exe, so the game is
/// recognised by that dll, not by its exe.</item>
/// <item>DemulShooter (gun games' recoil outputs).</item>
/// </list>
/// </summary>
public static class EmulatorNames
{
    public const string Mame = "MAME";
    public const string Flycast = "Flycast";
    public const string Odin = "Odin";
    public const string Keisho = "Keisho";
    public const string Supermodel = "Supermodel";
    public const string Model2 = "Model 2";
    public const string TeknoParrot = "TeknoParrot";
    public const string DemulShooter = "DemulShooter";

    /// <param name="exe">The sending process's exe name, with or without ".exe" (null when it couldn't be found).</param>
    /// <param name="hasOutputBlaster">The process has OutputBlaster.dll loaded.</param>
    /// <param name="protocolDefault">What the protocol says ("MAME", "Flycast"): used for an exe not known here,
    /// so an unusual MAME build still counts as MAME, as before.</param>
    public static string Identify(string? exe, bool hasOutputBlaster, string protocolDefault)
    {
        var name = (exe ?? "").Trim();
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        name = name.ToLowerInvariant();
        if (name.StartsWith("flycast", StringComparison.Ordinal)) return Flycast;
        if (name.StartsWith("odin", StringComparison.Ordinal)) return Odin;
        if (name.StartsWith("keisho", StringComparison.Ordinal)) return Keisho;
        if (name.StartsWith("supermodel", StringComparison.Ordinal)) return Supermodel;
        if (name is "emulator" or "emulator_multicpu") return Model2;
        if (name.StartsWith("demulshooter", StringComparison.Ordinal)) return DemulShooter;
        if (name.Contains("mame", StringComparison.Ordinal) || name is "arcade" or "arcade64" or "arcade32") return Mame;
        if (hasOutputBlaster) return TeknoParrot;
        return protocolDefault;
    }

    /// <summary>
    /// Emulators whose game ids are MAME rom names and whose outputs carry MAME's names, so LEDBlinky's
    /// MameOutputs.ini applies (Keisho names its lamps lamp0…lamp5 like MAME's model2 driver).
    /// </summary>
    public static bool UsesMameOutputsIni(string emulator) =>
        string.Equals(emulator, Mame, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(emulator, Keisho, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Start lamps that are the same in every game of an emulator, used when neither cabinet.json lamps.maps nor
/// MameOutputs.ini maps the output. Only names each emulator's source fixes as a start lamp; everything else
/// varies per game and needs a lamps.maps row. Flycast has none: its lamp0…lamp31 are the JVS output bits,
/// which differ per game.
/// </summary>
public static class BuiltInLampMaps
{
    public const string Source = "LED-Next's built-in start lamps";

    private static readonly Dictionary<string, (string Output, string Control)[]> Rows = new(StringComparer.OrdinalIgnoreCase)
    {
        // Output Blaster's names (Outputs.cpp), also Supermodel's and Keisho's.
        [EmulatorNames.TeknoParrot] = new[] { ("LampStart", "P1_START"), ("2pLampStart", "P2_START") },
        [EmulatorNames.Model2] = new[] { ("LampStart", "P1_START"), ("2pLampStart", "P2_START") },
        [EmulatorNames.Supermodel] = new[] { ("LampStart", "P1_START") },
        [EmulatorNames.Keisho] = new[] { ("LampStart", "P1_START") },
        // odin-games.json
        [EmulatorNames.Odin] = new[] { ("start_lamp", "P1_START"), ("1p_start_lamp", "P1_START"), ("2p_start_lamp", "P2_START") },
    };

    public static LampMapTable For(string emulator) =>
        new(emulator,
            Rows.TryGetValue(emulator, out var rows)
                ? rows.Select(r => new LampMapEntry { Emulator = emulator, Output = r.Output, Controls = new() { r.Control } })
                : null,
            Source);
}
