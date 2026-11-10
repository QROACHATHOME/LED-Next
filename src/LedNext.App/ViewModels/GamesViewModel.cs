using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LedNext.Core;
using LedNext.Core.Games;
using LedNext.Core.Import.LedBlinky;
using LedNext.Core.Lighting;

namespace LedNext.App.ViewModels;

public sealed record PlatformItem(string? Key, string Display);

public sealed partial class GameRow(GameInfo game, string platformName) : ObservableObject
{
    public GameInfo Game { get; private set; } = game;
    public string Description => Game.Description;

    /// <summary>Updates the customized flag in place (keeps the list selection).</summary>
    public void SetCustomized(bool value)
    {
        if (Game.Customized == value) return;
        Game = Game with { Customized = value };
        OnPropertyChanged(nameof(Details));
    }

    public string Details
    {
        get
        {
            var parts = new List<string> { Game.Name, platformName };
            if (Game.Parent is not null) parts.Add($"clone of {Game.Parent}");
            parts.Add(Game.Customized ? "customized" : Game.Lighting switch
            {
                GameLighting.Own => "own lights",
                GameLighting.Parent => "parent's lights",
                _ => "platform default",
            });
            return string.Join("  ·  ", parts);
        }
    }
}

public sealed class ControlRow(ResolvedControl c)
{
    public string Control => c.Control;
    public string Label => c.Label ?? "";
    public string ColorName => c.ColorName ?? c.Color.ToHex();
    public SolidColorBrush Brush { get; } = ColorUtil.Frozen(c.Color.ToMedia());
    public bool IsOnPanel => c.Buttons.Count > 0;
    public string ButtonsText => c.Buttons.Count > 0 ? string.Join(", ", c.Buttons) : "not on this panel";
    public string MapSource => c.Buttons.Count > 0 ? $"by {c.MapSource}" + (c.AlwaysActive ? " · always lit" : "") : "";
}

/// <summary>A colour swatch in the edit palette. Null colour = Off.</summary>
public sealed class PaletteColor(string name, Rgb? color)
{
    private static readonly System.Windows.Media.Color OffColor = System.Windows.Media.Color.FromRgb(0x22, 0x23, 0x28);
    public string Name { get; } = name;
    public Rgb? Color { get; } = color;
    public SolidColorBrush Brush { get; } = ColorUtil.Frozen(color?.ToMedia() ?? OffColor);
}

/// <summary>A panel button in the Games tab's preview, coloured with the game's intended colour.</summary>
public sealed class PreviewButton
{
    private static readonly Color Off = Color.FromRgb(0x22, 0x23, 0x28);

    public PreviewButton(PanelButton b, Rgb? color, bool wired, bool selected, string tooltip)
    {
        Id = b.Id;
        Left = b.X - b.Size / 2;
        Top = b.Y - b.Size / 2;
        Size = b.Size;
        ShapeRadius = b.Kind == ControlKind.Coin ? 8 : b.Size / 2;
        Label = b.Label;
        Kind = b.Kind;
        IsLit = color is not null;
        IsWired = wired;
        IsSelected = selected;
        Fill = color is { } c && c.R + c.G + c.B >= 30 ? c.ToMedia() : Off;
        ToolTip = tooltip;
    }

    public string Id { get; }
    public double Left { get; }
    public double Top { get; }
    public double Size { get; }
    public double ShapeRadius { get; }
    public string Label { get; }
    public ControlKind Kind { get; }
    public bool IsLit { get; }
    public bool IsWired { get; }
    public bool IsSelected { get; }
    public Color Fill { get; }
    public string ToolTip { get; }
}

/// <summary>
/// Games tab: import from LEDBlinky, browse/search the library, live preview of a game's lights,
/// and editing (click a button, pick a colour). Edits are stored apart from imported data.
/// </summary>
public sealed partial class GamesViewModel : ObservableObject
{
    private const int ResultLimit = 500;
    private readonly AppSession _session;
    private readonly MainViewModel _main;
    private readonly DispatcherTimer _searchDelay;
    private Dictionary<string, string> _platformNames = new(StringComparer.OrdinalIgnoreCase);
    private bool _suppressSearch;
    private bool _updatingSelection;

    // What is being previewed / edited.
    private string? _platform;
    private string? _game;
    private string _title = "";
    private ResolvedLights? _lights;
    private string? _selectedButtonId;

    public GamesViewModel(AppSession session, MainViewModel main)
    {
        _session = session;
        _main = main;
        _searchDelay = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _searchDelay.Tick += (_, _) => { _searchDelay.Stop(); Search(); };

        foreach (var (name, rgb) in new LedBlinkyColors().Named.Where(c => c.Key != "Black"))
            Palette.Add(new PaletteColor(name, rgb));
        Palette.Add(new PaletteColor("Dim", new LedBlinkyColors().Named["Black"]));
        Palette.Add(new PaletteColor("Off", null));

        RefreshLibrary();
    }

    public bool IsAvailable => _session.Games is not null;

    public ObservableCollection<PlatformItem> Platforms { get; } = new();
    public ObservableCollection<GameRow> Results { get; } = new();
    public ObservableCollection<ControlRow> Controls { get; } = new();
    public ObservableCollection<PreviewButton> PreviewButtons { get; } = new();
    public ObservableCollection<string> ImportReport { get; } = new();
    public ObservableCollection<PaletteColor> Palette { get; } = new();

    public double PanelWidth => _session.Config.Panel.Width;
    public double PanelHeight => _session.Config.Panel.Height;

    [ObservableProperty] private PlatformItem? _selectedPlatform;
    partial void OnSelectedPlatformChanged(PlatformItem? value) => Search();

    [ObservableProperty] private string _searchText = "";
    partial void OnSearchTextChanged(string value)
    {
        _searchDelay.Stop();
        _searchDelay.Start();
    }

    [ObservableProperty] private bool _litOnly = true;
    partial void OnLitOnlyChanged(bool value) => Search();

    [ObservableProperty] private bool _customizedOnly;
    partial void OnCustomizedOnlyChanged(bool value) => Search();

    [ObservableProperty] private GameRow? _selectedGame;
    partial void OnSelectedGameChanged(GameRow? value)
    {
        if (value is not null) Preview(value.Game.Platform, value.Game.Name, value.Description);
    }

    [ObservableProperty] private string _libraryInfo = "";
    [ObservableProperty] private string _resultInfo = "";
    [ObservableProperty] private string? _previewTitle;
    [ObservableProperty] private string? _previewSubtitle;
    [ObservableProperty] private string? _previewFrom;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ImportCommand), nameof(PasteSelectedCommand), nameof(PasteAllMatchesCommand), nameof(RevertSelectedCommand))]
    private bool _isBusy;

    [ObservableProperty] private string? _busyText;

    // ---- Editing state -------------------------------------------------------------------------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    [NotifyCanExecuteChangedFor(nameof(SetColorCommand), nameof(ApplyHexCommand), nameof(CopyButtonCommand))]
    private string? _selectedControl;

    [ObservableProperty] private string _selectionInfo = "Click a button on the panel to change its colour.";
    [ObservableProperty] private string _selectedLabel = "";
    [ObservableProperty] private string _customHex = "#FF8000";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RevertCommand))]
    private bool _isCustomized;

    public bool CanEdit => SelectedControl is not null && _lights is not null;

    partial void OnSelectedLabelChanged(string value)
    {
        if (_updatingSelection || SelectedControl is null) return;
        var control = SelectedControl;
        // Labels only exist on lit controls; editing the label of an unlit control does nothing.
        if (_lights?.Controls.Any(c => c.Control == control) != true) return;
        Edit(p => GameLightEditor.SetLabel(p, control, value), $"{control} label '{value}'");
    }

    // ---- Library and search ------------------------------------------------------------------

    private void RefreshLibrary()
    {
        var db = _session.Games;
        if (db is null)
        {
            LibraryInfo = "The game library could not be opened; see the Log tab.";
            return;
        }
        var summaries = db.GetPlatformSummaries();
        _platformNames = summaries.ToDictionary(s => s.Key, s => s.Name, StringComparer.OrdinalIgnoreCase);

        _suppressSearch = true;
        var keep = SelectedPlatform?.Key;
        Platforms.Clear();
        Platforms.Add(new PlatformItem(null, "All platforms"));
        foreach (var s in summaries.Where(s => s.Games > 0))
            Platforms.Add(new PlatformItem(s.Key, $"{s.Name}  ({s.LitGames:N0} lit / {s.Games:N0})"));
        SelectedPlatform = Platforms.FirstOrDefault(p => keep is not null && p.Key == keep)
                           ?? Platforms.FirstOrDefault(p => p.Key == LedBlinkyImporter.Mame) ?? Platforms[0];
        _suppressSearch = false;

        int count = summaries.Sum(s => s.Games);
        _libraryBase = count == 0
            ? "No games yet. Import from LEDBlinky to fill the library."
            : $"{count:N0} games on {summaries.Count} platforms, imported from {db.Meta("imported_from")} on {db.Meta("imported_at")}.";
        RefreshLibraryInfoOnly();
        Search();
    }

    private string _libraryBase = "";

    private void Search()
    {
        if (_suppressSearch || _session.Games is null) return;
        var (games, total) = _session.Games.Search(SelectedPlatform?.Key, SearchText, LitOnly, ResultLimit, CustomizedOnly);
        _matchCount = total;
        OnPropertyChanged(nameof(PasteAllText));
        PasteAllMatchesCommand.NotifyCanExecuteChanged();
        Results.Clear();
        foreach (var g in games) Results.Add(new GameRow(g, _platformNames.GetValueOrDefault(g.Platform, g.Platform)));
        ResultInfo = total > games.Count ? $"Showing {games.Count:N0} of {total:N0} matches. Type to narrow it down."
                                         : $"{total:N0} match{(total == 1 ? "" : "es")}";
    }

    // ---- Preview ----------------------------------------------------------------------------

    [RelayCommand]
    private void PreviewPlatformDefault()
    {
        var key = SelectedPlatform?.Key;
        if (key is null) return;
        SelectedGame = null;
        Preview(key, null, $"{_platformNames.GetValueOrDefault(key, key)} default");
    }

    private void Preview(string platform, string? game, string title)
    {
        var db = _session.Games;
        if (db is null) return;
        var config = _session.Config;
        _platform = platform;
        _game = game;
        _title = title;
        var lights = GameLightResolver.Resolve(db, platform, game, config.ControlMap, config.Panel);
        _lights = lights;

        PreviewTitle = title;
        var info = game is null ? null : db.GetGame(platform, game);
        var sub = new List<string> { _platformNames.GetValueOrDefault(platform, platform) };
        if (game is not null) sub.Add(game);
        if (info?.Parent is not null) sub.Add($"clone of {info.Parent}");
        if (info is { Players: > 0 }) sub.Add($"{info.Players} player{(info.Players == 1 ? "" : "s")}");
        if (!string.IsNullOrEmpty(info?.Info)) sub.Add(info.Info);
        PreviewSubtitle = string.Join("  ·  ", sub);
        PreviewFrom = $"Lit from {lights.From}" + (info is null ? "" : $"  ·  data: {info.Sources}");
        IsCustomized = db.IsCustomized(platform, game);

        Controls.Clear();
        foreach (var c in lights.Controls) Controls.Add(new ControlRow(c));
        RebuildPreviewButtons();
        UpdateSelection();
        OnPropertyChanged(nameof(PanelWidth));
        OnPropertyChanged(nameof(PanelHeight));
        OnPropertyChanged(nameof(CanEdit));
        CopyAllCommand.NotifyCanExecuteChanged();
        CopyButtonCommand.NotifyCanExecuteChanged();

        _main.SetSource(new GameLights(lights, $"Game: {title}"));
    }

    private void RebuildPreviewButtons()
    {
        if (_lights is null) return;
        var config = _session.Config;
        var byButton = _lights.Controls.SelectMany(c => c.Buttons.Select(b => (b, c)))
                              .GroupBy(x => x.b).ToDictionary(g => g.Key, g => g.Select(x => x.c.Label ?? x.c.Control).ToList());
        PreviewButtons.Clear();
        foreach (var b in config.Panel.Buttons)
        {
            bool wired = config.Wiring.Bindings.Count(x => x.ButtonId == b.Id) == 3;
            Rgb? color = _lights.ButtonColors.TryGetValue(b.Id, out var c) ? c : null;
            var tip = byButton.TryGetValue(b.Id, out var what) ? $"{b.Id}: {string.Join(", ", what)}" : $"{b.Id}: off";
            if (!wired) tip += " (no LEDs wired)";
            PreviewButtons.Add(new PreviewButton(b, color, wired, b.Id == _selectedButtonId, tip + "\nClick to edit"));
        }
    }

    // ---- Editing ----------------------------------------------------------------------------

    /// <summary>Called by the view when a preview button is clicked.</summary>
    public void SelectButton(string buttonId)
    {
        _selectedButtonId = _selectedButtonId == buttonId ? null : buttonId;
        RebuildPreviewButtons();
        UpdateSelection();
    }

    /// <summary>Works out which control the selected button stands for in the previewed game.</summary>
    private void UpdateSelection()
    {
        _updatingSelection = true;
        try
        {
            if (_selectedButtonId is null || _lights is null)
            {
                SelectedControl = null;
                SelectedLabel = "";
                SelectionInfo = _lights is null ? "Pick a game first." : "Click a button on the panel to change its colour.";
                return;
            }
            var config = _session.Config;
            var control = GameLightEditor.ControlForButton(_selectedButtonId, _lights, config.ControlMap, config.Panel);
            SelectedControl = control;
            if (control is null)
            {
                SelectedLabel = "";
                SelectionInfo = $"{_selectedButtonId}: no game control lights this button.";
                return;
            }
            var lit = _lights.Controls.FirstOrDefault(c => c.Control == control);
            SelectedLabel = lit?.Label ?? "";
            SelectionInfo = lit is null
                ? $"{_selectedButtonId} = {control} · off in this game"
                : $"{_selectedButtonId} = {control} · {lit.ColorName ?? lit.Color.ToHex()}" +
                  (lit.FromPlatformAlwaysActive ? " · always lit by the platform default" : "");
        }
        finally
        {
            _updatingSelection = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void SetColor(PaletteColor? swatch)
    {
        if (swatch is null || SelectedControl is null) return;
        var control = SelectedControl;
        if (swatch.Color is { } rgb)
            Edit(p => GameLightEditor.SetColor(p, control, rgb, swatch.Name), $"{control} → {swatch.Name}");
        else
            Edit(p => GameLightEditor.TurnOff(p, control), $"{control} → off");
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void ApplyHex()
    {
        if (SelectedControl is null) return;
        Rgb rgb;
        try { rgb = Rgb.ParseHex(CustomHex); }
        catch (FormatException) { SelectionInfo = $"'{CustomHex}' is not a colour; use #RRGGBB."; return; }
        var control = SelectedControl;
        Edit(p => GameLightEditor.SetColor(p, control, rgb, rgb.ToHex()), $"{control} → {rgb.ToHex()}");
    }

    private bool CanRevert() => IsCustomized;

    [RelayCommand(CanExecute = nameof(CanRevert))]
    private void Revert()
    {
        if (_session.Games is null || _platform is null) return;
        _session.Games.RevertUserLights(_platform, _game);
        AppLog.Info($"Reverted {_title} to the imported lights.");
        AfterEdit();
    }

    /// <summary>
    /// Applies one edit to the previewed game (or platform default). The first edit copies what the
    /// panel shows now into a customized profile, so nothing else changes.
    /// </summary>
    private void Edit(Func<LightProfile, LightProfile> change, string what)
    {
        var db = _session.Games;
        if (db is null || _platform is null || _lights is null) return;
        var current = db.GetLights(_platform, _game) is { Customized: true } own ? own : GameLightEditor.Materialize(_lights);
        db.SaveUserLights(_platform, _game, change(current));
        AppLog.Info($"Edited {_title}: {what}");
        AfterEdit();
    }

    private void AfterEdit()
    {
        if (_platform is null) return;
        bool wasCustomized = IsCustomized;
        Preview(_platform, _game, _title);
        if (_game is not null)
            foreach (var row in Results.Where(r => r.Game.Platform == _platform && r.Game.Name == _game))
                row.SetCustomized(IsCustomized);
        RevertSelectedCommand.NotifyCanExecuteChanged();
        if (wasCustomized != IsCustomized) RefreshLibraryInfoOnly();
    }

    private void RefreshLibraryInfoOnly()
    {
        var db = _session.Games;
        if (db is null) return;
        int custom = db.CustomizedCount();
        LibraryInfo = _libraryBase + (custom > 0 ? $" {custom:N0} customized (kept on re-import)." : "");
    }

    // ---- Copy / paste to many games -----------------------------------------------------------

    private List<GameRow> _selectedRows = new();
    private int _matchCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ClipboardInfo))]
    [NotifyCanExecuteChangedFor(nameof(PasteSelectedCommand), nameof(PasteAllMatchesCommand))]
    private LightClipboard? _clipboard;

    public string ClipboardInfo => Clipboard is null
        ? "Copy a game's lights (or one button) in the edit strip, select games here, then paste."
        : $"Copied: {Clipboard.Describe()}";

    public string PasteSelectedText => $"Paste to {_selectedRows.Count:N0} selected";
    public string PasteAllText => $"Paste to all {_matchCount:N0} matches";

    /// <summary>Called by the view when the list selection changes. Previews the game just clicked.</summary>
    public void SetListSelection(List<GameRow> selected, GameRow? clicked)
    {
        _selectedRows = selected;
        OnPropertyChanged(nameof(PasteSelectedText));
        PasteSelectedCommand.NotifyCanExecuteChanged();
        RevertSelectedCommand.NotifyCanExecuteChanged();
        var show = clicked ?? (selected.Count == 1 ? selected[0] : null);
        if (show is not null) Preview(show.Game.Platform, show.Game.Name, show.Description);
    }

    private bool CanCopy() => _lights is not null;

    [RelayCommand(CanExecute = nameof(CanCopy))]
    private void CopyAll()
    {
        if (_lights is null) return;
        Clipboard = new LightClipboard(_title, GameLightEditor.Materialize(_lights), null);
        AppLog.Info($"Copied {Clipboard.Describe()}");
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void CopyButton()
    {
        if (_lights is null || SelectedControl is null) return;
        Clipboard = new LightClipboard(_title, GameLightEditor.Materialize(_lights), new[] { SelectedControl });
        AppLog.Info($"Copied {Clipboard.Describe()}");
    }

    private bool CanPasteSelected() => !IsBusy && Clipboard is not null && _selectedRows.Count > 0;
    private bool CanPasteAll() => !IsBusy && Clipboard is not null && _matchCount > 0;
    private bool CanRevertSelected() => !IsBusy && _selectedRows.Any(r => r.Game.Customized);

    [RelayCommand(CanExecute = nameof(CanPasteSelected))]
    private Task PasteSelected() => PasteTo(_selectedRows.Select(r => r.Game).ToList(), "selected");

    [RelayCommand(CanExecute = nameof(CanPasteAll))]
    private Task PasteAllMatches()
    {
        var db = _session.Games;
        if (db is null) return Task.CompletedTask;
        var (all, _) = db.Search(SelectedPlatform?.Key, SearchText, LitOnly, int.MaxValue, CustomizedOnly);
        return PasteTo(all, "matching the search");
    }

    private async Task PasteTo(List<GameInfo> targets, string which)
    {
        var db = _session.Games;
        var clip = Clipboard;
        if (db is null || clip is null || targets.Count == 0) return;

        int already = targets.Count(t => t.Customized);
        var message = $"Paste {clip.Describe()}\nonto {targets.Count:N0} game{(targets.Count == 1 ? "" : "s")} {which}?" +
                      (already > 0 ? $"\n\n{already:N0} of them already have your changes; those changes will be replaced." : "") +
                      "\n\nEach game can be reverted to its imported lights afterwards.";
        if (System.Windows.MessageBox.Show(message, "Paste lights", System.Windows.MessageBoxButton.OKCancel,
                System.Windows.MessageBoxImage.Question) != System.Windows.MessageBoxResult.OK) return;

        var snapshot = ConfigStore.Clone(_session.Config);
        IsBusy = true;
        BusyText = $"Pasting onto {targets.Count:N0} games…";
        try
        {
            await Task.Run(() =>
            {
                var edits = targets.Select(g =>
                {
                    var current = db.GetLights(g.Platform, g.Name) is { Customized: true } own
                        ? own
                        : GameLightEditor.Materialize(GameLightResolver.Resolve(db, g.Platform, g.Name, snapshot.ControlMap, snapshot.Panel));
                    return (g.Platform, (string?)g.Name, GameLightEditor.Paste(current, clip));
                }).ToList();
                db.SaveUserLightsMany(edits);
            });
            AppLog.Info($"Pasted {clip.Describe()} onto {targets.Count:N0} games.");
            MarkRows(targets, customized: true);
            BusyText = $"Pasted onto {targets.Count:N0} games.";
        }
        catch (Exception ex)
        {
            AppLog.Error("Paste failed; no game was changed", ex);
            BusyText = $"Paste failed (no game was changed): {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRevertSelected))]
    private void RevertSelected()
    {
        var db = _session.Games;
        if (db is null) return;
        var targets = _selectedRows.Where(r => r.Game.Customized).Select(r => r.Game).ToList();
        if (System.Windows.MessageBox.Show($"Revert {targets.Count:N0} game{(targets.Count == 1 ? "" : "s")} to the imported lights? Your changes to them are dropped.",
                "Revert", System.Windows.MessageBoxButton.OKCancel, System.Windows.MessageBoxImage.Question) != System.Windows.MessageBoxResult.OK) return;
        db.RevertUserLightsMany(targets.Select(t => (t.Platform, (string?)t.Name)));
        AppLog.Info($"Reverted {targets.Count:N0} games to the imported lights.");
        MarkRows(targets, customized: false);
        BusyText = $"Reverted {targets.Count:N0} games.";
    }

    /// <summary>Updates list rows, the library count and the preview after a bulk change.</summary>
    private void MarkRows(List<GameInfo> games, bool customized)
    {
        var keys = games.Select(g => InMemoryGameLibrary.Key(g.Platform, g.Name)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var row in Results)
            if (keys.Contains(InMemoryGameLibrary.Key(row.Game.Platform, row.Game.Name))) row.SetCustomized(customized);
        RevertSelectedCommand.NotifyCanExecuteChanged();
        RefreshLibraryInfoOnly();
        if (_platform is not null) Preview(_platform, _game, _title);
    }

    // ---- Import -----------------------------------------------------------------------------

    private bool CanImport() => !IsBusy && _session.Games is not null;

    [RelayCommand(CanExecute = nameof(CanImport))]
    private async Task Import()
    {
        var db = _session.Games!;
        var last = _session.Config.Games.LedBlinkyFolder;
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Choose the LEDBlinky folder (it is only read, never changed)",
            InitialDirectory = Directory.Exists(last) ? last : Directory.Exists(@"C:\LEDBlinky") ? @"C:\LEDBlinky" : "",
        };
        if (dialog.ShowDialog() != true) return;
        var folder = dialog.FolderName;

        // The importer runs on a worker thread, so it gets its own copy of the panel and wiring.
        var snapshot = ConfigStore.Clone(_session.Config);
        IsBusy = true;
        BusyText = "Starting import…";
        var progress = new Progress<string>(s => BusyText = s);
        try
        {
            var result = await Task.Run(() =>
            {
                var r = LedBlinkyImporter.Import(folder, snapshot.Panel, snapshot.Wiring, progress);
                ((IProgress<string>)progress).Report("Saving the game library…");
                db.ReplaceAll(r.Library, folder);
                return r;
            });

            _session.Config.ControlMap = result.ControlMap;
            _session.Config.Games.LedBlinkyFolder = folder;
            _session.MarkDirty();

            ImportReport.Clear();
            ImportReport.Add(result.Summary);
            int custom = db.CustomizedCount();
            if (custom > 0) ImportReport.Add($"{custom:N0} customized games/platform defaults were kept.");
            foreach (var w in result.Warnings) { ImportReport.Add("⚠ " + w); AppLog.Warn(w); }
            foreach (var n in result.Notes) { ImportReport.Add(n); AppLog.Info(n); }
            RefreshLibrary();
            BusyText = null;
        }
        catch (Exception ex)
        {
            AppLog.Error($"LEDBlinky import from {folder} failed", ex);
            BusyText = $"Import failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
