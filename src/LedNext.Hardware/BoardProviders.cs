using LedNext.Core;
using LedNext.Hardware.Ultimarc;

namespace LedNext.Hardware;

public static class BoardProviders
{
    /// <summary>Builds the provider for the configured hardware mode.</summary>
    public static ILedBoardProvider Create(CabinetConfig config) => config.Hardware.Mode switch
    {
        HardwareMode.Hardware => new PacLed64Provider(config.Hardware.PacDriveDllPath),
        _ => new SimulatedBoardProvider(config.Wiring.Boards),
    };
}
