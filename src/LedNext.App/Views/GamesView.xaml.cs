using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LedNext.App.ViewModels;

namespace LedNext.App.Views;

public partial class GamesView : UserControl
{
    public GamesView() => InitializeComponent();

    /// <summary>A LaunchBox picture: opens it full size in Windows' viewer.</summary>
    private void OnPictureClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ReferencePicture p })
        {
            GamesViewModel.OpenPicture(p.Path);
            e.Handled = true;
        }
    }

    private void OnPreviewButtonClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: PreviewButton b } && DataContext is GamesViewModel vm)
        {
            vm.SelectButton(b.Id);
            e.Handled = true;
        }
    }

    private void OnGamesSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox list || DataContext is not GamesViewModel vm) return;
        var clicked = e.AddedItems.Count > 0 ? e.AddedItems[^1] as GameRow : null;
        vm.SetListSelection(list.SelectedItems.Cast<GameRow>().ToList(), clicked);
    }

    // Enter saves the label (the binding otherwise updates on focus loss).
    private void OnLabelKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not TextBox box) return;
        box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        e.Handled = true;
    }
}
