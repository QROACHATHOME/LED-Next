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

/// <summary>A LaunchBox picture shown next to the panel (control panel photo, controls card). Click opens the file.</summary>
public sealed record ReferencePicture(string Caption, ImageSource Image, string Path);

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
            if (Game.AddedToLaunchBox is { } added) parts.Add($"added {added.LocalDateTime:yyyy-MM-dd}");
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
    /// <summary>The control map the previewed game was lit with (its MAME install's keys), for "which control is this button".</summary>
    private ControlMap? _map;
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
        _selectedAdded = AddedChoices[0];

        RefreshLibrary();
        RefreshLaunchBoxInfo();
        RefreshBannerInfo();
        _ = StartUpReads();
    }

    /// <summary>
    /// Picks up games added to LaunchBox since last time (quietly; skipped if no folder is set yet), then banners
    /// added or changed since last time (one after the other: both use the busy state).
    /// </summary>
    private async Task StartUpReads()
    {
        await ReadLaunchBoxFrom(_session.Config.Games.LaunchBoxFolder, askIfMissing: false);
        await ReadBanners(_session.Config.Games.ButtonBannersFolder, onlyIfChanged: true);
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

    [ObservableProperty] private bool _hideClones;
    partial void OnHideClonesChanged(bool value) => Search();

    // Number filters are text so an empty box means "any".
    [ObservableProperty] private string _minButtonsText = "";
    [ObservableProperty] private string _maxButtonsText = "";
    [ObservableProperty] private string _minPlayersText = "";
    [ObservableProperty] private string _maxPlayersText = "";
    partial void OnMinButtonsTextChanged(string value) => DelayedSearch();
    partial void OnMaxButtonsTextChanged(string value) => DelayedSearch();
    partial void OnMinPlayersTextChanged(string value) => DelayedSearch();
    partial void OnMaxPlayersTextChanged(string value) => DelayedSearch();

    public IReadOnlyList<string> ControlChoices { get; } = new[] { "any" }.Concat(ControlTags.All).ToList();
    [ObservableProperty] private string _controlFilter = "any";
    partial void OnControlFilterChanged(string value) => Search();

    [ObservableProperty] private bool _filtersExpanded;

    public sealed record AddedChoice(int? Days, string Label);
    public IReadOnlyList<AddedChoice> AddedChoices { get; } = new AddedChoice[]
    {
        new(null, "any time"), new(1, "today"), new(7, "last 7 days"), new(14, "last 14 days"),
        new(30, "last 30 days"), new(90, "last 90 days"), new(365, "last year"),
    };

    [ObservableProperty] private AddedChoice? _selectedAdded;
    partial void OnSelectedAddedChanged(AddedChoice? value) => Search();

    /// <summary>Any number of days, typed; wins over the preset when filled in.</summary>
    [ObservableProperty] private string _addedDaysText = "";
    partial void OnAddedDaysTextChanged(string value) => DelayedSearch();

    [ObservableProperty] private bool _inLaunchBoxOnly;
    partial void OnInLaunchBoxOnlyChanged(bool value) => Search();

    [ObservableProperty] private bool _needsSetupOnly;
    partial void OnNeedsSetupOnlyChanged(bool value) => Search();

    /// <summary>Part of LaunchBox's genre, e.g. "fight". Empty = any.</summary>
    [ObservableProperty] private string _genreText = "";
    partial void OnGenreTextChanged(string value) => DelayedSearch();

    public string FilterSummary => CurrentFilter().Describe();

    private void DelayedSearch()
    {
        _searchDelay.Stop();
        _searchDelay.Start();
    }

    private static int? ParseCount(string text) =>
        int.TryParse(text.Trim(), out var n) && n >= 0 ? n : null;

    /// <summary>The filter the list shows now (what a smart list saves).</summary>
    public GameFilter CurrentFilter() => new()
    {
        Platform = SelectedPlatform?.Key,
        Text = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim(),
        LitOnly = LitOnly,
        CustomizedOnly = CustomizedOnly,
        HideClones = HideClones,
        MinButtons = ParseCount(MinButtonsText),
        MaxButtons = ParseCount(MaxButtonsText),
        MinPlayers = ParseCount(MinPlayersText),
        MaxPlayers = ParseCount(MaxPlayersText),
        Control = ControlFilter == "any" ? null : ControlFilter,
        AddedWithinDays = ParseCount(AddedDaysText) is { } typed and > 0 ? typed : SelectedAdded?.Days,
        InLaunchBoxOnly = InLaunchBoxOnly,
        NeedsSetupOnly = NeedsSetupOnly,
        Genre = string.IsNullOrWhiteSpace(GenreText) ? null : GenreText.Trim(),
    };

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
    /// <summary>The previewed game's button banner (button names), if there is a banners folder and a picture for it.</summary>
    [ObservableProperty] private ImageSource? _bannerImage;
    [ObservableProperty] private string _bannerInfo = "";
    /// <summary>The previewed game's LaunchBox control panel / controls information pictures.</summary>
    public ObservableCollection<ReferencePicture> Pictures { get; } = new();
    /// <summary>True when there is a banner or a picture to show beside the panel.</summary>
    [ObservableProperty] private bool _hasSidePictures;
    private LedNext.Core.Import.LaunchBox.LaunchBoxImages? _launchBoxImages;
    private int _pictureRequest;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ImportCommand), nameof(ChooseExtraColorsIniCommand), nameof(ChooseBannersFolderCommand), nameof(ReadLaunchBoxCommand), nameof(ChooseLaunchBoxFolderCommand),
        nameof(PasteSelectedCommand), nameof(RevertSelectedCommand),
        nameof(ApplyTemplateToSelectedCommand), nameof(ApplyTemplateToAllCommand))]
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

    // Templates are previewed read-only: edit a game, then save it as a template.
    public bool CanEdit => SelectedControl is not null && _lights is not null && _platform is not null;

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
        LoadSmartLists(SelectedSmartList?.Name);
        LoadTemplates(SelectedTemplate?.Name);
        Search();
    }

    private string _libraryBase = "";

    private void Search()
    {
        if (_suppressSearch || _session.Games is null) return;
        var (games, total) = _session.Games.Search(CurrentFilter(), ResultLimit);
        _matchCount = total;
        OnPropertyChanged(nameof(ApplyAllText));
        OnPropertyChanged(nameof(FilterSummary));
        ApplyTemplateToAllCommand.NotifyCanExecuteChanged();
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
        // The game's own MAME install (the emulator LaunchBox runs it with) may use other keys than the cabinet's map.
        var (map, keys) = _session.ControlMapFor(platform, game);
        _map = map;
        var lights = GameLightResolver.Resolve(db, platform, game, map, config.Panel);
        if (keys is not null) lights = lights with { From = $"{lights.From}; {keys}" };
        _lights = lights;

        PreviewTitle = title;
        var info = game is null ? null : db.GetGame(platform, game);
        var sub = new List<string> { _platformNames.GetValueOrDefault(platform, platform) };
        if (game is not null) sub.Add(game);
        if (info?.Parent is not null) sub.Add($"clone of {info.Parent}");
        if (info is { Players: > 0 }) sub.Add($"{info.Players} player{(info.Players == 1 ? "" : "s")}");
        if (!string.IsNullOrEmpty(info?.Info)) sub.Add(info.Info);
        if (info?.AddedToLaunchBox is { } added) sub.Add($"added to LaunchBox {added.LocalDateTime:yyyy-MM-dd}");
        PreviewSubtitle = string.Join("  ·  ", sub);
        PreviewFrom = $"Lit from {lights.From}" + (info is null ? "" : $"  ·  data: {info.Sources}");
        BannerImage = game is null ? null : FindBanner(db, platform, game) is { } banner ? ButtonBannerReader.Load(banner) : null;
        _ = LoadPictures(db, platform, game);
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

        _main.SetSource(new GameLights(lights, $"Game: {title}", title));
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
            var control = GameLightEditor.ControlForButton(_selectedButtonId, _lights, _map ?? config.ControlMap, config.Panel);
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
    [NotifyCanExecuteChangedFor(nameof(PasteSelectedCommand))]
    private LightClipboard? _clipboard;

    public string ClipboardInfo => Clipboard is null
        ? "Copy a game's lights (or one button) in the edit strip, select games here, then paste."
        : $"Copied: {Clipboard.Describe()}";

    public string PasteSelectedText => $"Paste to {_selectedRows.Count:N0} selected";

    /// <summary>Called by the view when the list selection changes. Previews the game just clicked.</summary>
    public void SetListSelection(List<GameRow> selected, GameRow? clicked)
    {
        _selectedRows = selected;
        OnPropertyChanged(nameof(PasteSelectedText));
        PasteSelectedCommand.NotifyCanExecuteChanged();
        RevertSelectedCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(ApplySelectedText));
        ApplyTemplateToSelectedCommand.NotifyCanExecuteChanged();
        TryTemplateOnSelectedCommand.NotifyCanExecuteChanged();
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
    private bool CanRevertSelected() => !IsBusy && _selectedRows.Any(r => r.Game.Customized);

    [RelayCommand(CanExecute = nameof(CanPasteSelected))]
    private Task PasteSelected() => PasteTo(Clipboard, _selectedRows.Select(r => r.Game).ToList(), "selected");

    // No "paste to all matches": it sat next to "Paste to selected" and was easy to hit by accident (user's call,
    // 2026-11-13). Select the games (Ctrl+A for all listed) and paste to them. Templates keep "Apply to all".

    /// <summary>Every game matching the filters, including ones past the list's limit (for "Apply to all").</summary>
    private List<GameInfo> AllMatches() => _session.Games?.Search(CurrentFilter(), int.MaxValue).Games ?? new();

    private async Task PasteTo(LightClipboard? clip, List<GameInfo> targets, string which)
    {
        var db = _session.Games;
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

    // ---- Smart lists ------------------------------------------------------------------------

    public sealed record SmartListItem(string Name, GameFilter? Filter)
    {
        public string Tip => Filter?.Describe() ?? "Your own filter";
    }

    public ObservableCollection<SmartListItem> SmartLists { get; } = new();
    private static readonly SmartListItem NoList = new("(your own filter)", null);

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteSmartListCommand))]
    private SmartListItem? _selectedSmartList;

    partial void OnSelectedSmartListChanged(SmartListItem? value)
    {
        if (_updatingSelection || value?.Filter is null) return;
        ApplyFilter(value.Filter);
        NewSmartListName = value.Name;
    }

    [ObservableProperty] private string _newSmartListName = "";

    private void LoadSmartLists(string? select = null)
    {
        var db = _session.Games;
        if (db is null) return;
        SmartLists.Clear();
        SmartLists.Add(NoList);
        foreach (var (name, filter) in db.ListSmartLists()) SmartLists.Add(new SmartListItem(name, filter));
        _updatingSelection = true;
        SelectedSmartList = SmartLists.FirstOrDefault(s => s.Name == select) ?? NoList;
        _updatingSelection = false;
    }

    /// <summary>Shows a smart list: sets every filter field, then searches once.</summary>
    private void ApplyFilter(GameFilter f)
    {
        _suppressSearch = true;
        SelectedPlatform = Platforms.FirstOrDefault(p => string.Equals(p.Key, f.Platform, StringComparison.OrdinalIgnoreCase)) ?? Platforms.FirstOrDefault();
        SearchText = f.Text ?? "";
        LitOnly = f.LitOnly;
        CustomizedOnly = f.CustomizedOnly;
        HideClones = f.HideClones;
        MinButtonsText = f.MinButtons?.ToString() ?? "";
        MaxButtonsText = f.MaxButtons?.ToString() ?? "";
        MinPlayersText = f.MinPlayers?.ToString() ?? "";
        MaxPlayersText = f.MaxPlayers?.ToString() ?? "";
        ControlFilter = f.Control ?? "any";
        var preset = AddedChoices.FirstOrDefault(a => a.Days == f.AddedWithinDays);
        SelectedAdded = preset ?? AddedChoices[0];
        AddedDaysText = preset is null && f.AddedWithinDays is { } d ? d.ToString() : "";
        InLaunchBoxOnly = f.InLaunchBoxOnly;
        NeedsSetupOnly = f.NeedsSetupOnly;
        GenreText = f.Genre ?? "";
        _suppressSearch = false;
        _searchDelay.Stop();
        Search();
    }

    [RelayCommand]
    private void SaveSmartList()
    {
        var db = _session.Games;
        var name = NewSmartListName.Trim();
        if (db is null) return;
        if (name.Length == 0) { BusyText = "Type a name for the smart list first."; return; }
        if (SmartLists.Any(s => s.Filter is not null && string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)) &&
            System.Windows.MessageBox.Show($"Replace the smart list '{name}' with the current filters?", "Smart list",
                System.Windows.MessageBoxButton.OKCancel) != System.Windows.MessageBoxResult.OK) return;
        db.SaveSmartList(name, CurrentFilter());
        AppLog.Info($"Saved smart list '{name}': {FilterSummary}");
        LoadSmartLists(name);
        BusyText = $"Saved smart list '{name}'.";
    }

    private bool CanDeleteSmartList() => SelectedSmartList?.Filter is not null;

    [RelayCommand(CanExecute = nameof(CanDeleteSmartList))]
    private void DeleteSmartList()
    {
        var db = _session.Games;
        var item = SelectedSmartList;
        if (db is null || item?.Filter is null) return;
        if (System.Windows.MessageBox.Show($"Delete the smart list '{item.Name}'? Games are not changed.", "Smart list",
                System.Windows.MessageBoxButton.OKCancel) != System.Windows.MessageBoxResult.OK) return;
        db.DeleteSmartList(item.Name);
        AppLog.Info($"Deleted smart list '{item.Name}'.");
        LoadSmartLists();
    }

    // ---- Templates --------------------------------------------------------------------------

    public ObservableCollection<LightTemplate> Templates { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyTemplateToSelectedCommand), nameof(ApplyTemplateToAllCommand), nameof(DeleteTemplateCommand),
        nameof(ShowTemplateCommand), nameof(TryTemplateOnSelectedCommand))]
    private LightTemplate? _selectedTemplate;

    partial void OnSelectedTemplateChanged(LightTemplate? value)
    {
        if (value is not null) PreviewTemplate(value);
    }

    [ObservableProperty] private string _newTemplateName = "";

    public string ApplySelectedText => $"Apply to {_selectedRows.Count:N0} selected";
    public string ApplyAllText => $"Apply to all {_matchCount:N0} matches";

    private void LoadTemplates(string? select = null)
    {
        var db = _session.Games;
        if (db is null) return;
        Templates.Clear();
        foreach (var t in db.ListTemplates()) Templates.Add(t);
        if (select is not null) SelectedTemplate = Templates.FirstOrDefault(t => t.Name == select);
    }

    private void PreviewTemplate(LightTemplate t)
    {
        var config = _session.Config;
        ShowUnsavedPreview(GameLightResolver.ResolveProfile(t.Profile, t.Name, config.ControlMap, config.Panel),
            $"Template: {t.Name}", t.Description, "Preview only. Apply it to games, or edit a game and save it as a template.");
    }

    private bool CanTryTemplate() => SelectedTemplate is not null && _selectedRows.Count > 0 && _session.Games is not null;

    /// <summary>
    /// Shows what the first selected game would look like with the template (the same result as
    /// "Apply to selected": the game's own button descriptions are kept). Nothing is saved.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanTryTemplate))]
    private void TryTemplateOnSelected()
    {
        var db = _session.Games;
        if (db is null || TemplateClip() is not { } clip || _selectedRows.FirstOrDefault() is not { } row) return;
        var config = _session.Config;
        var g = row.Game;
        var current = db.GetLights(g.Platform, g.Name) is { Customized: true } own
            ? own
            : GameLightEditor.Materialize(GameLightResolver.Resolve(db, g.Platform, g.Name, config.ControlMap, config.Panel));
        var result = GameLightEditor.Paste(current, clip);
        var lights = GameLightResolver.ResolveProfile(result, SelectedTemplate!.Name, config.ControlMap, config.Panel);
        var more = _selectedRows.Count > 1 ? $" · first of {_selectedRows.Count:N0} selected" : "";
        ShowUnsavedPreview(lights, $"{row.Description} + {SelectedTemplate!.Name}", $"{g.Name} · {g.Platform.Replace('_', ' ')}{more}",
            $"Trying the template; nothing is saved. \"{ApplySelectedText}\" keeps it, picking the game again shows its own lights.");
        AppLog.Info($"Trying template '{SelectedTemplate.Name}' on {g.Platform}/{g.Name} (not saved).");
    }

    private void ShowUnsavedPreview(ResolvedLights lights, string title, string? subtitle, string from)
    {
        _platform = null;
        _game = null;
        _title = title;
        _lights = lights;
        PreviewTitle = _title;
        PreviewSubtitle = subtitle;
        PreviewFrom = from;
        IsCustomized = false;
        Controls.Clear();
        foreach (var c in lights.Controls) Controls.Add(new ControlRow(c));
        RebuildPreviewButtons();
        UpdateSelection();
        OnPropertyChanged(nameof(CanEdit));
        CopyAllCommand.NotifyCanExecuteChanged();
        _main.SetSource(new GameLights(lights, _title)); // a template being tried: no spoken title
    }

    [RelayCommand(CanExecute = nameof(CanDeleteTemplate))]
    private void ShowTemplate()
    {
        if (SelectedTemplate is { } t) PreviewTemplate(t);
    }

    private LightClipboard? TemplateClip() =>
        SelectedTemplate is { } t ? new LightClipboard($"template {t.Name}", t.Profile, null) : null;

    private bool CanApplySelected() => !IsBusy && SelectedTemplate is not null && _selectedRows.Count > 0;
    private bool CanApplyAll() => !IsBusy && SelectedTemplate is not null && _matchCount > 0;

    [RelayCommand(CanExecute = nameof(CanApplySelected))]
    private Task ApplyTemplateToSelected() => PasteTo(TemplateClip(), _selectedRows.Select(r => r.Game).ToList(), "selected");

    [RelayCommand(CanExecute = nameof(CanApplyAll))]
    private Task ApplyTemplateToAll() => PasteTo(TemplateClip(), AllMatches(), $"matching {FilterSummary}");

    /// <summary>Saves what the panel shows now (a game, platform default or template) under a name.</summary>
    [RelayCommand]
    private void SaveTemplate()
    {
        var db = _session.Games;
        var name = NewTemplateName.Trim();
        if (db is null) return;
        if (_lights is null) { BusyText = "Pick a game whose lights you want to save first."; return; }
        if (name.Length == 0) { BusyText = "Type a name for the template first."; return; }
        if (Templates.Any(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)) &&
            System.Windows.MessageBox.Show($"Replace the template '{name}' with the lights shown now ({_title})?", "Template",
                System.Windows.MessageBoxButton.OKCancel) != System.Windows.MessageBoxResult.OK) return;
        var profile = GameLightEditor.Materialize(_lights);
        db.SaveTemplate(new LightTemplate(name, profile, $"Saved from {_title}."));
        AppLog.Info($"Saved template '{name}' from {_title}.");
        LoadTemplates(name);
        BusyText = $"Saved template '{name}'.";
    }

    private bool CanDeleteTemplate() => SelectedTemplate is not null;

    [RelayCommand(CanExecute = nameof(CanDeleteTemplate))]
    private void DeleteTemplate()
    {
        var db = _session.Games;
        var t = SelectedTemplate;
        if (db is null || t is null) return;
        if (System.Windows.MessageBox.Show($"Delete the template '{t.Name}'? Games it was applied to keep their lights.", "Template",
                System.Windows.MessageBoxButton.OKCancel) != System.Windows.MessageBoxResult.OK) return;
        db.DeleteTemplate(t.Name);
        AppLog.Info($"Deleted template '{t.Name}'.");
        LoadTemplates();
    }

    // ---- LaunchBox --------------------------------------------------------------------------

    /// <summary>One LaunchBox platform and the LED-Next platform its games are matched to (changeable).</summary>
    public sealed partial class LaunchBoxPlatformRow : ObservableObject
    {
        private readonly GamesViewModel _owner;
        private bool _loading = true;

        public LaunchBoxPlatformRow(GamesViewModel owner, LedNext.Data.GameDatabase.LaunchBoxPlatformSummary s, IReadOnlyList<string> choices)
        {
            _owner = owner;
            LaunchBoxPlatform = s.LaunchBoxPlatform;
            Choices = choices;
            Details = $"{s.Games:N0} game{(s.Games == 1 ? "" : "s")} · {s.ByRom:N0} by ROM name, {s.ByTitle:N0} by title, {s.New:N0} new · platform {s.How}";
            NeedsAttention = s.How == "new";
            _ledPlatform = s.Platform;
            _loading = false;
        }

        public string LaunchBoxPlatform { get; }
        public string Details { get; }
        public bool NeedsAttention { get; }
        public IReadOnlyList<string> Choices { get; }

        [ObservableProperty] private string _ledPlatform;
        partial void OnLedPlatformChanged(string value)
        {
            if (!_loading) _owner.RemapPlatform(LaunchBoxPlatform, value);
        }
    }

    public ObservableCollection<LaunchBoxPlatformRow> LaunchBoxPlatforms { get; } = new();
    [ObservableProperty] private string _launchBoxInfo = "LaunchBox not read yet.";

    private void RefreshLaunchBoxInfo()
    {
        var db = _session.Games;
        if (db is null) return;
        var summaries = db.LaunchBoxSummaries();
        var choices = db.GetPlatformSummaries().Select(p => p.Key).ToList();
        LaunchBoxPlatforms.Clear();
        foreach (var s in summaries) LaunchBoxPlatforms.Add(new LaunchBoxPlatformRow(this, s, choices));
        int games = summaries.Sum(s => s.Games);
        LaunchBoxInfo = games == 0
            ? "LaunchBox not read yet. Read it to find recently added games."
            : $"LaunchBox: {games:N0} games on {summaries.Count} platforms, read {db.Meta("launchbox_read_at")}.";
        LaunchBoxFolderTip = db.Meta("launchbox_folder") is { } f ? $"Read from {f}. Reads again at start-up." : "Reads LaunchBox's game list (never changed).";
    }

    [ObservableProperty] private string _launchBoxFolderTip = "Reads LaunchBox's game list (never changed).";

    [RelayCommand(CanExecute = nameof(CanImport))]
    private Task ReadLaunchBox() => ReadLaunchBoxFrom(_session.Config.Games.LaunchBoxFolder, askIfMissing: true);

    [RelayCommand(CanExecute = nameof(CanImport))]
    private Task ChooseLaunchBoxFolder() => ReadLaunchBoxFrom(null, askIfMissing: true);

    /// <summary>Reads the LaunchBox library (read only) and matches it to the game library.</summary>
    private async Task ReadLaunchBoxFrom(string? folder, bool askIfMissing)
    {
        _launchBoxImages = null; // list LaunchBox's picture folders again (pictures may have been added)
        var db = _session.Games;
        if (db is null) return;
        if (folder is null || !LedNext.Core.Import.LaunchBox.LaunchBoxLibrary.LooksLikeLaunchBox(folder))
        {
            if (!askIfMissing) return;
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Choose the LaunchBox folder (it is only read, never changed)",
                InitialDirectory = Directory.Exists(@"D:\LaunchBox") ? @"D:\LaunchBox" : "",
            };
            if (dialog.ShowDialog() != true) return;
            folder = dialog.FolderName;
            if (!LedNext.Core.Import.LaunchBox.LaunchBoxLibrary.LooksLikeLaunchBox(folder))
            {
                BusyText = $"{folder} has no Data\\Platforms folder; pick the LaunchBox folder itself.";
                return;
            }
        }

        var overrides = new Dictionary<string, string>(_session.Config.Games.LaunchBoxPlatformMap, StringComparer.OrdinalIgnoreCase);
        IsBusy = true;
        BusyText = "Reading LaunchBox…";
        try
        {
            var warnings = new List<string>();
            var games = await Task.Run(() =>
            {
                var list = LedNext.Core.Import.LaunchBox.LaunchBoxLibrary.Read(folder, warnings);
                db.ImportLaunchBox(list, overrides, folder);
                return list;
            });
            foreach (var w in warnings) AppLog.Warn(w);
            _session.Config.Games.LaunchBoxFolder = folder;
            _session.MarkDirty();
            AppLog.Info($"Read {games.Count:N0} games from LaunchBox at {folder}.");
            RefreshLibrary();
            RefreshLaunchBoxInfo();
            BusyText = warnings.Count == 0 ? null : $"LaunchBox read with {warnings.Count} warning(s); see the Log tab.";
        }
        catch (Exception ex)
        {
            AppLog.Error($"Reading LaunchBox at {folder} failed", ex);
            BusyText = $"Reading LaunchBox failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Called when you change a LaunchBox platform's LED-Next platform; re-matches the games.</summary>
    internal void RemapPlatform(string launchBoxPlatform, string ledPlatform)
    {
        _session.Config.Games.LaunchBoxPlatformMap[launchBoxPlatform] = ledPlatform;
        _session.MarkDirty();
        AppLog.Info($"LaunchBox platform '{launchBoxPlatform}' now uses LED-Next platform '{ledPlatform}'.");
        _ = ReadLaunchBoxFrom(_session.Config.Games.LaunchBoxFolder, askIfMissing: false);
    }

    // ---- Import -----------------------------------------------------------------------------

    private bool CanImport() => !IsBusy && _session.Games is not null;

    [RelayCommand(CanExecute = nameof(CanImport))]
    private async Task Import()
    {
        var last = _session.Config.Games.LedBlinkyFolder;
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Choose the LEDBlinky folder (it is only read, never changed)",
            InitialDirectory = Directory.Exists(last) ? last : Directory.Exists(@"C:\LEDBlinky") ? @"C:\LEDBlinky" : "",
        };
        if (dialog.ShowDialog() != true) return;
        await RunImport(dialog.FolderName);
    }

    // ---- Button banners ---------------------------------------------------------------------

    /// <summary>The banner file for a game: its own rom's, else the nearest parent's (also through MAME's data).</summary>
    private string? FindBanner(Core.Games.IGameLibrary db, string platform, string game)
    {
        var folder = _session.Config.Games.ButtonBannersFolder;
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return null;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in new[] { platform, "MAME" })
            for (string? name = game; name is not null && seen.Count < 32 && seen.Add(p + "/" + name); name = db.GetGame(p, name)?.Parent)
            {
                var file = Path.Combine(folder, name + ".png");
                if (name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && File.Exists(file)) return file;
            }
        return null;
    }

    /// <summary>
    /// Looks up the game's LaunchBox pictures (its own LaunchBox entries, else its parent's) on a worker thread;
    /// a newer preview replaces the result of an older one.
    /// </summary>
    private async Task LoadPictures(LedNext.Data.GameDatabase db, string platform, string? game)
    {
        int request = ++_pictureRequest;
        Pictures.Clear();
        HasSidePictures = BannerImage is not null;
        var folder = _session.Config.Games.LaunchBoxFolder;
        if (game is null || string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;
        try
        {
            var images = _launchBoxImages is { } li && string.Equals(li.LaunchBoxFolder, folder, StringComparison.OrdinalIgnoreCase) ? li : null;
            var found = await Task.Run(() =>
            {
                images ??= new LedNext.Core.Import.LaunchBox.LaunchBoxImages(folder);
                var result = new List<ReferencePicture>();
                foreach (var (type, caption) in new[]
                         {
                             (LedNext.Core.Import.LaunchBox.LaunchBoxImages.ControlPanel, "Control panel"),
                             (LedNext.Core.Import.LaunchBox.LaunchBoxImages.ControlsInformation, "Controls information"),
                         })
                {
                    var (file, count, where) = FindPicture(db, images, platform, game, type);
                    if (file is null || ButtonBannerReader.Load(file, 600) is not { } image) continue;
                    var name = where is null ? "" : $" ({where})";
                    result.Add(new ReferencePicture($"{caption}{name}{(count > 1 ? $" · 1 of {count}" : "")}", image, file));
                }
                return result;
            });
            _launchBoxImages = images;
            if (request != _pictureRequest) return;
            foreach (var p in found) Pictures.Add(p);
            HasSidePictures = BannerImage is not null || Pictures.Count > 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
        {
            AppLog.Warn($"LaunchBox pictures for {game}: {ex.Message}");
        }
    }

    /// <summary>The first picture of this type for the game's LaunchBox entries, else its parent's (also through MAME).</summary>
    private static (string? File, int Count, string? Where) FindPicture(LedNext.Data.GameDatabase db,
        LedNext.Core.Import.LaunchBox.LaunchBoxImages images, string platform, string game, string mediaType)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in new[] { platform, "MAME" })
            for (string? name = game; name is not null && seen.Count < 32 && seen.Add(p + "/" + name); name = db.GetGame(p, name)?.Parent)
                foreach (var (lbPlatform, title) in db.LaunchBoxEntries(p, name, orMameDataPlatforms: p == "MAME"))
                {
                    var files = images.Find(lbPlatform, title, mediaType);
                    if (files.Count > 0)
                        return (files[0], files.Count, string.Equals(name, game, StringComparison.OrdinalIgnoreCase) ? lbPlatform : $"{lbPlatform}, {title}");
                }
        return (null, 0, null);
    }

    /// <summary>Opens a picture in Windows' default viewer (read only).</summary>
    public static void OpenPicture(string path)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            AppLog.Warn($"Could not open {path}: {ex.Message}");
        }
    }

    private void RefreshBannerInfo()
    {
        var folder = _session.Config.Games.ButtonBannersFolder;
        int n = _session.Games?.BannerCount() ?? 0;
        BannerInfo = string.IsNullOrEmpty(folder) ? "Button banners: none"
            : $"Button banners: {n:N0} game{(n == 1 ? "" : "s")}";
        OnPropertyChanged(nameof(BannerTip));
    }

    public string BannerTip => _session.Config.Games.ButtonBannersFolder is { Length: > 0 } f
        ? $"Button banners from {f}\nShown next to the panel; MAME games without lights of their own light as many buttons as " +
          "their banner uses. Choose again to change the folder; cancel the dialog to stop using it."
        : "Choose a folder of \"6 button layout\" banners (<rom>.png; only read). They are shown next to the panel, and MAME " +
          "games without lights of their own light as many buttons as their banner uses.";

    [RelayCommand(CanExecute = nameof(CanImport))]
    private async Task ChooseBannersFolder()
    {
        var games = _session.Config.Games;
        var current = games.ButtonBannersFolder;
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Choose the button banners folder (<rom>.png; it is only read, never changed)",
            InitialDirectory = Directory.Exists(current) ? current : "",
        };
        string? chosen;
        if (dialog.ShowDialog() == true) chosen = dialog.FolderName;
        else
        {
            if (string.IsNullOrEmpty(current)) return;
            var answer = System.Windows.MessageBox.Show($"Stop using the button banners?\n\n{current}", "Button banners",
                System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
            if (answer != System.Windows.MessageBoxResult.Yes) return;
            chosen = null;
        }
        games.ButtonBannersFolder = chosen;
        _session.MarkDirty();
        AppLog.Info(chosen is null ? "Button banners removed." : $"Button banners folder: {chosen}");
        await ReadBanners(chosen, onlyIfChanged: false);
    }

    /// <summary>Reads the banners on a worker thread and stores the button counts. Null folder: removes them.</summary>
    private async Task ReadBanners(string? folder, bool onlyIfChanged)
    {
        var db = _session.Games;
        if (db is null || IsBusy) return;
        try
        {
            if (string.IsNullOrEmpty(folder))
            {
                if (db.BannerCount() > 0) db.ReplaceBannerButtons(new Dictionary<string, IReadOnlyList<int>>(), null, null);
                return;
            }
            if (!Directory.Exists(folder)) { AppLog.Warn($"Button banners folder not found: {folder}"); return; }
            var stamp = await Task.Run(() => ButtonBannerReader.Stamp(folder));
            if (onlyIfChanged && db.Meta("banner_folder") == folder && db.Meta("banner_stamp") == stamp) return;

            IsBusy = true;
            BusyText = "Reading button banners…";
            var progress = new Progress<string>(s => BusyText = s);
            int skipped = 0;
            var banners = await Task.Run(() =>
            {
                var r = ButtonBannerReader.Read(folder, progress, out skipped);
                db.ReplaceBannerButtons(r, folder, stamp);
                return r;
            });
            var msg = $"Button banners: {banners.Count:N0} read from {folder}" + (skipped > 0 ? $"; {skipped:N0} skipped (not a banner)." : ".");
            AppLog.Info(msg);
            BusyText = msg;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
        {
            AppLog.Error("Reading button banners failed", ex);
            BusyText = $"Reading button banners failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            RefreshBannerInfo();
            if (_platform is not null) Preview(_platform, _game, _title);
        }
    }

    /// <summary>Path of the extra colors.ini, or a hint when there is none (for the button's tooltip).</summary>
    public string ExtraColorsIniTip => _session.Config.Games.ExtraColorsIni is { Length: > 0 } p
        ? $"Extra colors.ini: {p}\nIt only lights MAME games LEDBlinky has no lights for. Choose again to change it; cancel the dialog to remove it."
        : "Add a newer or community colors.ini (kept anywhere; it is only read). It only lights MAME games LEDBlinky has no lights for.";

    /// <summary>Chooses (or, on cancel, offers to remove) the extra colors.ini, then imports from LEDBlinky again.</summary>
    [RelayCommand(CanExecute = nameof(CanImport))]
    private async Task ChooseExtraColorsIni()
    {
        var games = _session.Config.Games;
        var current = games.ExtraColorsIni;
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose an extra colors.ini (it is only read, never changed)",
            Filter = "colors.ini|*.ini|All files|*.*",
            InitialDirectory = Path.GetDirectoryName(current) is { } dir && Directory.Exists(dir) ? dir : "",
        };
        string? chosen;
        if (dialog.ShowDialog() == true)
        {
            chosen = dialog.FileName;
        }
        else
        {
            if (string.IsNullOrEmpty(current)) return;
            var answer = System.Windows.MessageBox.Show($"Stop using the extra colors.ini?\n\n{current}", "Extra colors.ini",
                System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
            if (answer != System.Windows.MessageBoxResult.Yes) return;
            chosen = null;
        }
        if (string.Equals(chosen, current, StringComparison.OrdinalIgnoreCase)) return;

        games.ExtraColorsIni = chosen;
        _session.MarkDirty();
        OnPropertyChanged(nameof(ExtraColorsIniTip));
        AppLog.Info(chosen is null ? "Extra colors.ini removed." : $"Extra colors.ini: {chosen}");

        if (string.IsNullOrEmpty(games.LedBlinkyFolder) || !Directory.Exists(games.LedBlinkyFolder))
        {
            BusyText = "Saved. Import from LEDBlinky to use it.";
            return;
        }
        await RunImport(games.LedBlinkyFolder);
    }

    private async Task RunImport(string folder)
    {
        var db = _session.Games!;
        // The importer runs on a worker thread, so it gets its own copy of the panel and wiring.
        var snapshot = ConfigStore.Clone(_session.Config);
        IsBusy = true;
        BusyText = "Starting import…";
        var progress = new Progress<string>(s => BusyText = s);
        try
        {
            var result = await Task.Run(() =>
            {
                var r = LedBlinkyImporter.Import(folder, snapshot.Panel, snapshot.Wiring, progress,
                    extraColorsIni: snapshot.Games.ExtraColorsIni);
                ((IProgress<string>)progress).Report("Saving the game library…");
                db.ReplaceAll(r.Library, folder);
                return r;
            });

            _session.Config.ControlMap = result.ControlMap;
            _session.Config.Games.LedBlinkyFolder = folder;
            _session.ResetKeyLayouts();
            _session.MarkDirty();

            ImportReport.Clear();
            ImportReport.Add(result.Summary);
            int custom = db.CustomizedCount();
            if (custom > 0) ImportReport.Add($"{custom:N0} customized games/platform defaults were kept.");
            foreach (var w in result.Warnings) { ImportReport.Add("⚠ " + w); AppLog.Warn(w); }
            foreach (var n in result.Notes) { ImportReport.Add(n); AppLog.Info(n); }
            RefreshLibrary();
            BusyText = null;
            IsBusy = false;
            // Game names may have changed, so match LaunchBox again.
            await ReadLaunchBoxFrom(_session.Config.Games.LaunchBoxFolder, askIfMissing: false);
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
