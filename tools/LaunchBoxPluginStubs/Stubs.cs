// Compile-time stand-in for LaunchBox's plugin API (Unbroken.LaunchBox.Plugins). See the .csproj.
// Checked against the real LaunchBox 14.1.0.5 DLL on the cabinet (visit 5, TASKS/CABINET_RESULTS.md):
// namespaces, member names and signatures below match it. Only the members the plugin uses (plus the
// SystemEventTypes constants and FilterType values, for reference) are here.
#nullable enable

namespace Unbroken.LaunchBox.Plugins
{
    using Unbroken.LaunchBox.Plugins.Data;

    /// <summary>LaunchBox / BigBox start-up and shut-down events (see <see cref="SystemEventTypes"/>).</summary>
    public interface ISystemEventsPlugin
    {
        void OnEventRaised(string eventType);
    }

    /// <summary>Game launch and exit.</summary>
    public interface IGameLaunchingPlugin
    {
        void OnBeforeGameLaunching(IGame? game, IAdditionalApplication? app, IEmulator? emulator);
        void OnAfterGameLaunched(IGame game, IAdditionalApplication? app, IEmulator? emulator);
        void OnGameExited();
    }

    /// <summary>A WPF control a BigBox theme can place in its views; gets selection changes.</summary>
    public interface IBigBoxThemeElementPlugin
    {
        bool OnDown(bool held);
        bool OnEnter();
        bool OnEscape();
        bool OnLeft(bool held);
        bool OnPageDown();
        bool OnPageUp();
        bool OnRight(bool held);
        bool OnUp(bool held);
        void OnSelectionChanged(FilterType filterType, string filterValue, IPlatform platform, IPlatformCategory category,
            IPlaylist playlist, IGame game);
    }

    public static class PluginHelper
    {
        public static IStateManager StateManager { get; set; } = null!;
    }
}

// In the real DLL these are in the Data namespace (the docs put some of them in the root namespace).
namespace Unbroken.LaunchBox.Plugins.Data
{
    public static class SystemEventTypes
    {
        public const string LaunchBoxStartupCompleted = "LaunchBoxStartupCompleted";
        public const string LaunchBoxShutdownBeginning = "LaunchBoxShutdownBeginning";
        public const string PluginInitialized = "PluginInitialized";
        public const string BigBoxStartupCompleted = "BigBoxStartupCompleted";
        public const string BigBoxShutdownBeginning = "BigBoxShutdownBeginning";
        public const string BigBoxThemeChanged = "BigBoxThemeChanged";
        public const string BigBoxLocked = "BigBoxLocked";
        public const string BigBoxUnlocked = "BigBoxUnlocked";
        public const string GameStarting = "GameStarting";
        public const string GameExited = "GameExited";
        public const string SelectionChanged = "SelectionChanged";
        public const string MameGameSessionStarted = "MameGameSessionStarted";
        public const string MameGameSessionEnded = "MameGameSessionEnded";
        public const string MameCoinInserted = "MameCoinInserted";
        public const string MamePlayerStarted = "MamePlayerStarted";
    }

    public enum FilterType
    {
        Genre, PlatformOrCategoryOrPlaylist, Developer, Publisher, Rating, PlayMode, Region, Series, Status, Source,
        MaxPlayers, RecentlyPlayed, Favorites, Local, Cloud, None,
    }

    public interface IStateManager
    {
        bool IsBigBoxLocked { get; }
        bool IsBigBoxInAttractMode { get; }
        bool IsBigBox { get; }
        bool IsPremium { get; }
        string BigBoxCurrentTheme { get; }
        IGame[] GetAllSelectedGames();
        IPlatform GetSelectedPlatform();
    }

    public interface IGame
    {
        string Id { get; }
        string Title { get; set; }
        string Platform { get; set; }
        string ApplicationPath { get; set; }
    }

    public interface IAdditionalApplication { string Id { get; } string GameId { get; } string Name { get; set; } }
    public interface IEmulator { string Id { get; } string Title { get; set; } }
    public interface IPlatform { string Name { get; } }
    public interface IPlatformCategory { string Name { get; } }
    public interface IPlaylist { string PlaylistId { get; } string Name { get; } }
}
