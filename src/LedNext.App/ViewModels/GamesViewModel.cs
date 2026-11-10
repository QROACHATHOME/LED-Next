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

public sealed class GameRow(GameInfo game, string platformName)
{
    public GameInfo Game { get; } = game;
    public string Description => Game.Description;
    public string Details
    {
        get
        {
            var parts = new List<string> { Game.Name, platformName };
            if (Game.Parent is not null) parts.Add($"clone of {Game.Parent}");
            parts.Add(Game.Lighting switch
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

/// <summary>A panel button in the Games tab's preview, coloured with the game's intended colour.</summary>
public sealed class PreviewButton
{
    private static readonly Color Off = Color.FromRgb(0x22, 0x23, 0x28);

    public PreviewButton(PanelButton b, Rgb? color, bool wired, string tooltip)
    {
        Left = b.X - b.Size / 2;
        Top = b.Y - b.Size / 2;
        Size = b.Size;
        ShapeRadius = b.Kind == ControlKind.Coin ? 8 : b.Size / 2;
        Label = b.Label;
        Kind = b.Kind;
        IsLit = color is not null;
        IsWired = wired;
        Fill = color is { } c && c.R + c.G + c.B >= 30 ? c.ToMedia() : Off;
        ToolTip = tooltip;
    }

    public double Left { get; }
    public double Top { get; }
    public double Size { get; }
    public double ShapeRadius { get; }
    public string Label { get; }
    public ControlKind Kind { get; }
    public bool IsLit { get; }
    public bool IsWired { get; }
    public Color Fill { get; }
    public string ToolTip { get; }
}

/// <summary>Games tab: import from LEDBlinky, browse/search the library, live preview of a game's lights.</summary>
public sealed partial class GamesViewModel : ObservableObject
{
    private const int ResultLimit = 500;
    private readonly AppSession _session;
    private readonly MainViewModel _main;
    private readonly DispatcherTimer _searchDelay;
    private Dictionary<string, string> _platformNames = new(StringComparer.OrdinalIgnoreCase);
    private bool _suppressSearch;

    public GamesViewModel(AppSession session, MainViewModel main)
    {
        _session = session;
        _main = main;
        _searchDelay = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _searchDelay.Tick += (_, _) => { _searchDelay.Stop(); Search(); };
        RefreshLibrary();
    }

    public bool IsAvailable => _session.Games is not null;

    public ObservableCollection<PlatformItem> Platforms { get; } = new();
    public ObservableCollection<GameRow> Results { get; } = new();
    public ObservableCollection<ControlRow> Controls { get; } = new();
    public ObservableCollection<PreviewButton> PreviewButtons { get; } = new();
    public ObservableCollection<string> ImportReport { get; } = new();

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
    [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
    private bool _isBusy;

    [ObservableProperty] private string? _busyText;

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
        Platforms.Clear();
        Platforms.Add(new PlatformItem(null, "All platforms"));
        foreach (var s in summaries.Where(s => s.Games > 0))
            Platforms.Add(new PlatformItem(s.Key, $"{s.Name}  ({s.LitGames:N0} lit / {s.Games:N0})"));
        SelectedPlatform = Platforms.FirstOrDefault(p => p.Key == LedBlinkyImporter.Mame) ?? Platforms[0];
        _suppressSearch = false;

        int count = summaries.Sum(s => s.Games);
        var from = db.Meta("imported_from");
        LibraryInfo = count == 0
            ? "No games yet. Import from LEDBlinky to fill the library."
            : $"{count:N0} games on {summaries.Count} platforms, imported from {from} on {db.Meta("imported_at")}.";
        Search();
    }

    private void Search()
    {
        if (_suppressSearch || _session.Games is null) return;
        var (games, total) = _session.Games.Search(SelectedPlatform?.Key, SearchText, LitOnly, ResultLimit);
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
        var lights = GameLightResolver.Resolve(db, platform, game, config.ControlMap, config.Panel);

        PreviewTitle = title;
        var info = game is null ? null : db.GetGame(platform, game);
        var sub = new List<string> { _platformNames.GetValueOrDefault(platform, platform) };
        if (game is not null) sub.Add(game);
        if (info?.Parent is not null) sub.Add($"clone of {info.Parent}");
        if (info is { Players: > 0 }) sub.Add($"{info.Players} player{(info.Players == 1 ? "" : "s")}");
        if (!string.IsNullOrEmpty(info?.Info)) sub.Add(info.Info);
        PreviewSubtitle = string.Join("  ·  ", sub);
        PreviewFrom = $"Lit from {lights.From}" + (info is null ? "" : $"  ·  data: {info.Sources}");

        Controls.Clear();
        foreach (var c in lights.Controls) Controls.Add(new ControlRow(c));

        var byButton = lights.Controls.SelectMany(c => c.Buttons.Select(b => (b, c)))
                             .GroupBy(x => x.b).ToDictionary(g => g.Key, g => g.Select(x => x.c.Label ?? x.c.Control).ToList());
        PreviewButtons.Clear();
        foreach (var b in config.Panel.Buttons)
        {
            bool wired = config.Wiring.Bindings.Count(x => x.ButtonId == b.Id) == 3;
            Rgb? color = lights.ButtonColors.TryGetValue(b.Id, out var c) ? c : null;
            var tip = byButton.TryGetValue(b.Id, out var what) ? $"{b.Id}: {string.Join(", ", what)}" : $"{b.Id}: off";
            if (!wired) tip += " (no LEDs wired)";
            PreviewButtons.Add(new PreviewButton(b, color, wired, tip));
        }
        OnPropertyChanged(nameof(PanelWidth));
        OnPropertyChanged(nameof(PanelHeight));

        _main.SetSource(new GameLights(lights, $"Game: {title}"));
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
