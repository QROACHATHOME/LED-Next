using System.Windows.Controls;
using LedNext.App.ViewModels;

namespace LedNext.App.Views;

public partial class BoardsView : UserControl
{
    public BoardsView() => InitializeComponent();

    /// <summary>Lists the PC's serial ports again each time a port list opens (a strip may just have been plugged in).</summary>
    private void OnPortsDropDownOpened(object? sender, EventArgs e)
    {
        if (DataContext is BoardsViewModel vm) vm.RefreshPortsCommand.Execute(null);
    }
}
