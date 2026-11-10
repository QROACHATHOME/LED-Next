// Compile-time stand-in for LaunchBox's plugin API (Unbroken.LaunchBox.Plugins). See the .csproj.
// Members mirror LaunchBox's documented plugin interfaces; anything that differs in the real 14.1 DLL
// shows up as a build error when the plugin is built on the cabinet against the real file.

namespace Unbroken.LaunchBox.Plugins
{
    using Unbroken.LaunchBox.Plugins.Data;

    /// <summary>LaunchBox / BigBox start-up and shut-down events.</summary>
    public interface ISystemEventsPlugin
    {
        void OnEventRaised(string eventType);
    }

    public static class SystemEventTypes
    {
        public const string PluginInitialized = "PluginInitialized";
        public const string LaunchBoxStartupCompleted = "LaunchBoxStartupCompleted";
        public const string LaunchBoxShutdownBeginning = "LaunchBoxShutdownBeginning";
        public const string BigBoxStartupCompleted = "BigBoxStartupCompleted";
        public const string BigBoxShutdownBeginning = "BigBoxShutdownBeginning";
    }

    /// <summary>Game launch and exit.</summary>
    public interface IGameLaunchingPlugin
    {
        void OnBeforeGameLaunching(IGame game, IAdditionalApplication app, IEmulator emulator);
        void OnAfterGameLaunched(IGame game, IAdditionalApplication app, IEmulator emulator);
        void OnGameExited();
    }

    public enum FilterType
    {
        None, Platform, PlatformCategory, Playlist, Genre, Developer, Publisher, ReleaseYear, Series,
        PlayMode, Rating, Region, Source, Status, Favorite, Completed, Broken, Hidden, Portable, Installed,
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

    public interface IStateManager
    {
        IGame[] GetAllSelectedGames();
    }

    public static class PluginHelper
    {
        public static IStateManager StateManager { get; set; }
    }
}

namespace Unbroken.LaunchBox.Plugins.Data
{
    public interface IGame
    {
        string Id { get; }
        string Title { get; }
        string Platform { get; }
        string ApplicationPath { get; }
    }

    public interface IAdditionalApplication { string Name { get; } }
    public interface IEmulator { string Title { get; } }
    public interface IPlatform { string Name { get; } }
    public interface IPlatformCategory { string Name { get; } }
    public interface IPlaylist { string Name { get; } }
}
