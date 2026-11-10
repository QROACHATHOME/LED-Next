using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LedNext.Core;
using LedNext.Core.Lighting;
using Microsoft.Win32;

namespace LedNext.App.ViewModels;

/// <summary>Panel designer: the buttons on the canvas, the selected button's properties, the panel photo.</summary>
public sealed partial class PanelViewModel : ObservableObject
{
    private const double PasteOffset = 30;
    private readonly AppSession _session;
    private readonly MainViewModel _main;
    private PanelButton? _clipboard;

    public PanelViewModel(AppSession session, MainViewModel main)
    {
        _session = session;
        _main = main;
        Wizard = new WizardViewModel(session, this, main);
        Rebuild();
    }

    public ObservableCollection<ButtonViewModel> Buttons { get; } = new();
    public WizardViewModel Wizard { get; }

    private PanelLayout Layout => _session.Config.Panel;

    public double Width => Layout.Width;
    public double Height => Layout.Height;

    public static IReadOnlyList<int> Players { get; } = new[] { 0, 1, 2, 3, 4 };
    public static IReadOnlyList<ControlKind> Kinds { get; } = Enum.GetValues<ControlKind>();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyCanExecuteChangedFor(nameof(DeleteSelectedCommand), nameof(IdentifySelectedCommand), nameof(DuplicateCommand), nameof(CopyCommand))]
    private ButtonViewModel? _selected;

    public bool HasSelection => Selected is not null;

    partial void OnSelectedChanged(ButtonViewModel? oldValue, ButtonViewModel? newValue)
    {
        if (oldValue is not null) oldValue.IsSelected = false;
        if (newValue is not null) newValue.IsSelected = true;
        Wizard.OnSelectionChanged();
    }

    public void Rebuild()
    {
        var selectedId = Selected?.Id;
        Buttons.Clear();
        foreach (var b in Layout.Buttons) Buttons.Add(new ButtonViewModel(b, this));
        RefreshWiringInfo();
        Selected = Buttons.FirstOrDefault(b => b.Id == selectedId);
        OnPropertyChanged(nameof(Width));
        OnPropertyChanged(nameof(Height));
        LoadBackground();
        if (!Wizard.IsActive) Wizard.RefreshBoardChoices();
    }

    public void RefreshWiringInfo()
    {
        var wiring = _session.Config.Wiring;
        foreach (var b in Buttons)
        {
            var parts = Enum.GetValues<ColorChannel>().Select(ch =>
            {
                var bind = wiring.Find(b.Id, ch);
                return bind is null ? $"{ch}: not wired" : $"{ch}: {wiring.FindBoard(bind.BoardKey)?.Name ?? bind.BoardKey} pin {bind.Pin}";
            }).ToArray();
            b.WiringInfo = string.Join("\n", parts);
            b.IsFullyWired = parts.All(p => !p.EndsWith("not wired"));
        }
    }

    public void ApplyFrame(IReadOnlyDictionary<string, Rgb> colors)
    {
        foreach (var b in Buttons)
            if (colors.TryGetValue(b.Id, out var c)) b.Led = c.ToMedia();
    }

    // ---- Edits from button view models ------------------------------------------------------

    internal void BeforeEdit(string? coalesceKey) => _session.Checkpoint(coalesceKey);

    internal void OnButtonEdited() => _session.NotifyChanged();

    internal bool CanDrag => !Wizard.IsActive;

    internal bool TryRename(ButtonViewModel button, string newId)
    {
        newId = newId.Trim();
        if (newId.Length == 0 || newId == button.Id) return false;
        if (Layout.Find(newId) is not null)
        {
            AppLog.Warn($"A button named '{newId}' already exists.");
            return false;
        }
        _session.Checkpoint();
        _session.Config.Wiring.RenameButton(button.Id, newId);
        button.Model.Id = newId;
        _session.NotifyChanged(structural: true);
        return true;
    }

    // ---- Commands ---------------------------------------------------------------------------

    [RelayCommand]
    private void AddButton()
    {
        var model = new PanelButton
        {
            Id = Layout.MakeUniqueId("NEW"),
            Label = "New",
            Player = 1,
            X = Layout.Width / 2,
            Y = Layout.Height / 2,
        };
        AddAndSelect(model);
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Duplicate()
    {
        if (Selected is null) return;
        AddAndSelect(CopyOf(Selected.Model));
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Copy()
    {
        if (Selected is null) return;
        _clipboard = PanelLayout.CloneButton(Selected.Model);
        AppLog.Info($"Copied {Selected.Id}");
    }

    [RelayCommand]
    private void Paste()
    {
        if (_clipboard is null) return;
        var copy = CopyOf(_clipboard);
        _clipboard = PanelLayout.CloneButton(copy); // next paste lands further along
        AddAndSelect(copy);
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void DeleteSelected()
    {
        if (Selected is null) return;
        _session.Checkpoint();
        var id = Selected.Id;
        Layout.Buttons.Remove(Selected.Model);
        _session.Config.Wiring.RemoveButton(id);
        Selected = null;
        _session.NotifyChanged(structural: true);
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void IdentifySelected()
    {
        if (Selected is not null) _main.SetSource(new IdentifyButton(Selected.Id));
    }

    public void Nudge(double dx, double dy)
    {
        if (Selected is null || !CanDrag) return;
        _session.Checkpoint($"nudge:{Selected.Id}");
        Selected.MoveBy(dx, dy); // no drag pending, so MoveBy adds no second checkpoint
    }

    private PanelButton CopyOf(PanelButton source)
    {
        var copy = PanelLayout.CloneButton(source);
        copy.Id = Layout.NextIdAfter(source.Id);
        // Bump the label too if it is just a number ("8" → "9").
        if (int.TryParse(source.Label, out var n)) copy.Label = (n + 1).ToString();
        copy.X = Math.Min(Layout.Width, source.X + PasteOffset);
        copy.Y = Math.Min(Layout.Height, source.Y + PasteOffset);
        return copy;
    }

    private void AddAndSelect(PanelButton model)
    {
        _session.Checkpoint();
        Layout.Buttons.Add(model);
        _session.NotifyChanged(structural: true);
        Selected = Buttons.FirstOrDefault(b => b.Id == model.Id);
    }

    // ---- Panel photo ------------------------------------------------------------------------

    [ObservableProperty] private ImageSource? _background;

    public bool HasBackground => Background is not null;
    partial void OnBackgroundChanged(ImageSource? value) => OnPropertyChanged(nameof(HasBackground));

    public double BackgroundOpacity
    {
        get => Layout.BackgroundOpacity;
        set
        {
            value = Math.Round(Math.Clamp(value, 0.05, 1), 2);
            if (Layout.BackgroundOpacity == value) return;
            _session.Checkpoint("bg-opacity");
            Layout.BackgroundOpacity = value;
            OnPropertyChanged();
            _session.MarkDirty();
        }
    }

    [RelayCommand]
    private void SetBackground()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Choose a photo or drawing of your control panel",
            Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp|All files|*.*",
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            var image = LoadImage(dlg.FileName);
            var fileName = "panel-background" + Path.GetExtension(dlg.FileName).ToLowerInvariant();
            var target = Path.Combine(_session.ConfigDirectory, fileName);
            Directory.CreateDirectory(_session.ConfigDirectory);
            if (!string.Equals(Path.GetFullPath(dlg.FileName), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
                File.Copy(dlg.FileName, target, overwrite: true);

            _session.Checkpoint();
            Layout.BackgroundImage = fileName;

            // Match the panel's shape to the photo so it isn't stretched. Width stays the same,
            // so button X positions are unchanged; buttons are kept inside the new height.
            if (image.PixelWidth > 0 && image.PixelHeight > 0)
            {
                Layout.Height = Math.Round(Layout.Width * image.PixelHeight / image.PixelWidth);
                foreach (var b in Layout.Buttons) b.Y = Math.Min(b.Y, Layout.Height);
            }
            AppLog.Info($"Panel photo set: {dlg.FileName} ({image.PixelWidth}×{image.PixelHeight}); panel is now {Layout.Width}×{Layout.Height}.");
            _session.NotifyChanged(structural: true);
        }
        catch (Exception ex)
        {
            AppLog.Error($"Could not use {dlg.FileName} as the panel photo", ex);
        }
    }

    [RelayCommand]
    private void ClearBackground()
    {
        if (Layout.BackgroundImage is null) return;
        _session.Checkpoint();
        Layout.BackgroundImage = null;
        _session.NotifyChanged(structural: true);
    }

    private void LoadBackground()
    {
        OnPropertyChanged(nameof(BackgroundOpacity));
        var name = Layout.BackgroundImage;
        if (string.IsNullOrEmpty(name))
        {
            Background = null;
            return;
        }

        var path = Path.Combine(_session.ConfigDirectory, name);
        if (!File.Exists(path))
        {
            AppLog.Warn($"Panel photo not found: {path}");
            Background = null;
            return;
        }

        try { Background = LoadImage(path); }
        catch (Exception ex)
        {
            AppLog.Error($"Could not load panel photo {path}", ex);
            Background = null;
        }
    }

    /// <summary>Loads fully into memory so the file is not locked (it may be replaced later).</summary>
    private static BitmapImage LoadImage(string path)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.UriSource = new Uri(Path.GetFullPath(path));
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }
}

public sealed partial class ButtonViewModel : ObservableObject
{
    private static readonly Color Off = Color.FromRgb(0x22, 0x23, 0x28);
    private readonly PanelViewModel _owner;

    public ButtonViewModel(PanelButton model, PanelViewModel owner)
    {
        Model = model;
        _owner = owner;
    }

    public PanelButton Model { get; }

    public string Id
    {
        get => Model.Id;
        set
        {
            _owner.TryRename(this, value);
            OnPropertyChanged(); // reverts the text box if the rename was refused
        }
    }

    public string Label
    {
        get => Model.Label;
        set { if (Model.Label != value) { _owner.BeforeEdit($"label:{Id}"); Model.Label = value; OnPropertyChanged(); _owner.OnButtonEdited(); } }
    }

    public int Player
    {
        get => Model.Player;
        set { if (Model.Player != value) { _owner.BeforeEdit(null); Model.Player = value; OnPropertyChanged(); _owner.OnButtonEdited(); } }
    }

    public ControlKind Kind
    {
        get => Model.Kind;
        set
        {
            if (Model.Kind == value) return;
            _owner.BeforeEdit(null);
            Model.Kind = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShapeRadius));
            _owner.OnButtonEdited();
        }
    }

    /// <summary>Corner radius of the drawn shape: a circle for everything except coin buttons (rounded square).</summary>
    public double ShapeRadius => Model.Kind == ControlKind.Coin ? 8 : Model.Size / 2;

    public double GlossSize => Model.Size * 0.45;
    public System.Windows.Thickness GlossMargin => new(Model.Size * 0.16, Model.Size * 0.12, 0, 0);

    public double Size
    {
        get => Model.Size;
        set
        {
            value = Math.Clamp(Math.Round(value), 16, 200);
            if (Model.Size == value) return;
            _owner.BeforeEdit($"size:{Id}");
            Model.Size = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShapeRadius));
            OnPropertyChanged(nameof(GlossSize));
            OnPropertyChanged(nameof(GlossMargin));
            OnPropertyChanged(nameof(Left));
            OnPropertyChanged(nameof(Top));
            _owner.OnButtonEdited();
        }
    }

    public double Left => Model.X - Model.Size / 2;
    public double Top => Model.Y - Model.Size / 2;

    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isFullyWired;
    [ObservableProperty] private string _wiringInfo = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Fill))]
    private Color _led = Off;

    /// <summary>LED colour, with a dark fill when off so the button stays visible.</summary>
    public Color Fill => Led.R + Led.G + Led.B < 30 ? Off : Led;

    private bool _moveCheckpointPending;

    /// <summary>Called when a drag starts. The undo step is recorded on the first real movement,
    /// so a plain click (select) doesn't create an empty undo step.</summary>
    public bool BeginMove()
    {
        if (!_owner.CanDrag) return false;
        _moveCheckpointPending = true;
        return true;
    }

    public void MoveBy(double dx, double dy)
    {
        if (!_owner.CanDrag || (dx == 0 && dy == 0)) return;
        if (_moveCheckpointPending)
        {
            _moveCheckpointPending = false;
            _owner.BeforeEdit(null);
        }
        Model.X = Math.Clamp(Model.X + dx, 0, _owner.Width);
        Model.Y = Math.Clamp(Model.Y + dy, 0, _owner.Height);
        OnPropertyChanged(nameof(Left));
        OnPropertyChanged(nameof(Top));
        _owner.OnButtonEdited();
    }

    /// <summary>Called when a drag ends: snaps to whole units if the button actually moved.</summary>
    public void EndMove()
    {
        bool moved = !_moveCheckpointPending;
        _moveCheckpointPending = false;
        if (moved && _owner.CanDrag) SnapPosition();
    }

    private void SnapPosition()
    {
        Model.X = Math.Round(Model.X);
        Model.Y = Math.Round(Model.Y);
        OnPropertyChanged(nameof(Left));
        OnPropertyChanged(nameof(Top));
        _owner.OnButtonEdited();
    }
}
