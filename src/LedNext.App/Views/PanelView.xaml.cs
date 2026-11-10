using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using LedNext.App.ViewModels;

namespace LedNext.App.Views;

public partial class PanelView : UserControl
{
    public PanelView() => InitializeComponent();

    private PanelViewModel? Vm => DataContext as PanelViewModel;

    private void OnDragStarted(object sender, DragStartedEventArgs e)
    {
        Focus(); // so keyboard shortcuts reach this view
        if (Vm is null || sender is not FrameworkElement { DataContext: ButtonViewModel b }) return;
        Vm.Selected = b;
        b.BeginMove();
    }

    private void OnDragDelta(object sender, DragDeltaEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ButtonViewModel b }) b.MoveBy(e.HorizontalChange, e.VerticalChange);
    }

    private void OnDragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ButtonViewModel b }) b.EndMove();
    }

    private void OnPanelBackgroundClick(object sender, MouseButtonEventArgs e)
    {
        Focus();
        if (Vm is not null) Vm.Selected = null;
    }

    /// <summary>
    /// Keeps keyboard focus on this view after clicks (buttons, sliders, the canvas) so the wizard
    /// and editing shortcuts keep working. Text boxes and dropdowns keep their own focus.
    /// </summary>
    /// <remarks>
    /// A button or checkbox cancels its click if it loses keyboard focus while pressed, so for those
    /// focus is taken back only after the mouse-up (and the click) has been handled.
    /// </remarks>
    private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        for (var d = e.OriginalSource as DependencyObject; d is not null && d != this; d = ParentOf(d))
        {
            if (d is TextBox or ComboBox) return;
            if (d is ButtonBase)
            {
                _refocusAfterClick = true;
                return;
            }
        }
        Dispatcher.BeginInvoke(() => Focus(), System.Windows.Threading.DispatcherPriority.Input);
    }

    private bool _refocusAfterClick;

    private void OnPreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_refocusAfterClick) return;
        _refocusAfterClick = false;
        // Queued behind the current mouse-up, so the button's Click runs first.
        Dispatcher.BeginInvoke(() => Focus(), System.Windows.Threading.DispatcherPriority.Input);
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var vm = Vm;
        if (vm is null) return;
        // Leave typing alone.
        if (e.OriginalSource is TextBox or ComboBox or ComboBoxItem) return;

        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;

        if (vm.Wizard.IsActive)
        {
            var w = vm.Wizard;
            e.Handled = e.Key switch
            {
                Key.R => Run(w.AssignCommand, "R"),
                Key.G => Run(w.AssignCommand, "G"),
                Key.B => Run(w.AssignCommand, "B"),
                Key.Enter => Run(w.AcceptSuggestionCommand),
                Key.Space => Run(w.SkipCommand),
                Key.Back => Run(w.BackCommand),
                Key.Escape => Run(w.FinishCommand),
                _ => false,
            };
            return;
        }

        double step = shift ? 10 : 1;
        e.Handled = e.Key switch
        {
            Key.Delete => Run(vm.DeleteSelectedCommand),
            Key.D when ctrl => Run(vm.DuplicateCommand),
            Key.C when ctrl => Run(vm.CopyCommand),
            Key.V when ctrl => Run(vm.PasteCommand),
            Key.Left when vm.HasSelection => Nudge(vm, -step, 0),
            Key.Right when vm.HasSelection => Nudge(vm, step, 0),
            Key.Up when vm.HasSelection => Nudge(vm, 0, -step),
            Key.Down when vm.HasSelection => Nudge(vm, 0, step),
            Key.Escape when vm.HasSelection => Deselect(vm),
            _ => false,
        };
    }

    private static DependencyObject? ParentOf(DependencyObject d) =>
        d is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
            ? System.Windows.Media.VisualTreeHelper.GetParent(d)
            : LogicalTreeHelper.GetParent(d);

    private static bool Run(ICommand command, object? parameter = null)
    {
        if (command.CanExecute(parameter)) command.Execute(parameter);
        return true;
    }

    private static bool Nudge(PanelViewModel vm, double dx, double dy)
    {
        vm.Nudge(dx, dy);
        return true;
    }

    private static bool Deselect(PanelViewModel vm)
    {
        vm.Selected = null;
        return true;
    }
}
