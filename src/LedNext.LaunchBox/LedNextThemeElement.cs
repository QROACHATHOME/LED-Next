using System.Windows;
using System.Windows.Controls;
using LedNext.FrontEnd;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace LedNext.LaunchBox;

/// <summary>
/// An invisible BigBox theme element: BigBox tells it about every selection change. A theme uses it by
/// adding to a view (e.g. Views\WheelGamesView.xaml):
/// <code>
/// xmlns:lednext="clr-namespace:LedNext.LaunchBox;assembly=LedNext.LaunchBox"
/// &lt;lednext:LedNextThemeElement /&gt;
/// </code>
/// It never handles keys (returns false), so BigBox navigation is unchanged.
/// </summary>
public sealed class LedNextThemeElement : UserControl, IBigBoxThemeElementPlugin
{
    public LedNextThemeElement()
    {
        Visibility = Visibility.Collapsed;
        Width = 0;
        Height = 0;
        IsHitTestVisible = false;
        Focusable = false;
        PluginHost.EnsureStarted();
    }

    public void OnSelectionChanged(FilterType filterType, string filterValue, IPlatform platform, IPlatformCategory category,
        IPlaylist playlist, IGame game)
    {
        try
        {
            if (game?.Id is not null) PluginHost.SendSelection(game, "theme element");
        }
        catch (Exception ex)
        {
            PluginHost.Log($"OnSelectionChanged: {ex.Message}");
        }
    }

    public bool OnDown(bool held) => false;
    public bool OnEnter() => false;
    public bool OnEscape() => false;
    public bool OnLeft(bool held) => false;
    public bool OnPageDown() => false;
    public bool OnPageUp() => false;
    public bool OnRight(bool held) => false;
    public bool OnUp(bool held) => false;
}
