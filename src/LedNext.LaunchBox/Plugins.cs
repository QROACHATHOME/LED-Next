using LedNext.FrontEnd;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace LedNext.LaunchBox;

/// <summary>LaunchBox / BigBox start-up and shut-down.</summary>
public sealed class LedNextSystemEvents : ISystemEventsPlugin
{
    public void OnEventRaised(string eventType)
    {
        try
        {
            switch (eventType)
            {
                case "PluginInitialized":
                    PluginHost.EnsureStarted(); // connects (or starts LED-Next) early
                    break;
                case "LaunchBoxStartupCompleted":
                case "BigBoxStartupCompleted":
                    PluginHost.Send(FrontEndEvents.Started);
                    PluginHost.StartSelectionWatcher();
                    break;
                case "LaunchBoxShutdownBeginning":
                case "BigBoxShutdownBeginning":
                    PluginHost.Shutdown();
                    break;
            }
        }
        catch (Exception ex)
        {
            PluginHost.Log($"OnEventRaised({eventType}): {ex.Message}");
        }
    }
}

/// <summary>Game launched / exited: the panel shows the running game, then the selection again.</summary>
public sealed class LedNextGameLaunching : IGameLaunchingPlugin
{
    public void OnBeforeGameLaunching(IGame game, IAdditionalApplication app, IEmulator emulator) =>
        PluginHost.Send(FrontEndEvents.Launch, game);

    public void OnAfterGameLaunched(IGame game, IAdditionalApplication app, IEmulator emulator) { }

    public void OnGameExited() => PluginHost.Send(FrontEndEvents.Exit);
}
