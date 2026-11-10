using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LedNext.App.ViewModels;

namespace LedNext.App.Views;

public partial class GamesView : UserControl
{
    public GamesView() => InitializeComponent();

    private void OnPreviewButtonClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: PreviewButton b } && DataContext is GamesViewModel vm)
        {
            vm.SelectButton(b.Id);
            e.Handled = true;
        }
    }

    // Enter saves the label (the binding otherwise updates on focus loss).
    private void OnLabelKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not TextBox box) return;
        box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        e.Handled = true;
    }
}
