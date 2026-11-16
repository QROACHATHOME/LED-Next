using LedNext.Core;
using LedNext.Hardware.Hid;
using LedNext.Hardware.Serial;
using LedNext.Hardware.Ultimarc;

namespace LedNext.Hardware;

public static class BoardProviders
{
    /// <summary>Builds the provider for the configured hardware mode.</summary>
    public static ILedBoardProvider Create(CabinetConfig config) => config.Hardware.Mode switch
    {
        HardwareMode.Hardware => new CompositeBoardProvider(
            new PacLed64Provider(config.Hardware.PacDriveDllPath),
            new LedWizProvider(),
            new LedStripProvider(config.Wiring.Boards)),
        _ => new SimulatedBoardProvider(config.Wiring.Boards),
    };
}

/// <summary>
/// Every kind of real board at once: Ultimarc's (PacDrive SDK), LED-Wiz (HID) and LED strips (serial). One failing
/// (the PacDrive DLL missing, say) doesn't stop the others.
/// </summary>
public sealed class CompositeBoardProvider(params ILedBoardProvider[] providers) : ILedBoardProvider
{
    public string Name => "Hardware";

    public IReadOnlyList<ILedBoard> Discover()
    {
        var boards = new List<ILedBoard>();
        foreach (var p in providers)
        {
            try
            {
                var found = p.Discover();
                if (found.Count > 0) AppLog.Info($"{p.Name}: {string.Join(", ", found.Select(b => b.Key))}");
                foreach (var b in found)
                {
                    if (boards.Any(x => x.Key == b.Key)) { AppLog.Warn($"{p.Name}: {b.Key} found twice; skipping the second."); continue; }
                    boards.Add(b);
                }
            }
            catch (Exception ex) { AppLog.Error($"{p.Name}: discovery failed", ex); }
        }
        return boards.OrderBy(b => b.Key, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public void Dispose()
    {
        foreach (var p in providers)
        {
            try { p.Dispose(); }
            catch (Exception ex) { AppLog.Error($"{p.Name}: dispose failed", ex); }
        }
    }
}
