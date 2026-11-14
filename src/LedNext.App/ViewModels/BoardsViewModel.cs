using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LedNext.Core;
using LedNext.Core.Lighting;

namespace LedNext.App.ViewModels;

/// <summary>Board test view: every pin of every board, live, click to light.</summary>
public sealed partial class BoardsViewModel : ObservableObject
{
    private readonly AppSession _session;
    private readonly MainViewModel _main;

    public BoardsViewModel(AppSession session, MainViewModel main)
    {
        _session = session;
        _main = main;
        // Start on the model of the first configured board, so a cabinet with only Ultimate I/O boards shows them.
        if (BoardModels.Find(session.Config.Wiring.Boards.FirstOrDefault()?.Model) is { } first) _addModel = first;
        Rebuild();
    }

    public ObservableCollection<BoardViewModel> Boards { get; } = new();
    public ManualPins Manual { get; } = new();

    public void Rebuild()
    {
        Boards.Clear();
        foreach (var def in _session.Config.Wiring.Boards) Boards.Add(new BoardViewModel(def));
        ApplyModelFilter();
        RefreshUsage();
        RefreshStatus();
    }

    public void RefreshStatus()
    {
        var online = _session.Engine.Boards.ToDictionary(b => b.Key);
        foreach (var vm in Boards)
            vm.Status = online.TryGetValue(vm.Key, out var b) ? (b.IsSimulated ? "Simulated" : "Online") : "Not detected";
    }

    public void RefreshUsage()
    {
        var wiring = _session.Config.Wiring;
        foreach (var board in Boards)
            foreach (var pin in board.Pins)
            {
                var bind = wiring.FindByPin(board.Key, pin.Pin);
                pin.Usage = bind is null ? "" : $"{bind.ButtonId} {bind.Channel}";
                pin.Channel = bind?.Channel;
            }
    }

    public void ApplyFrame(IReadOnlyDictionary<string, byte[]> pins)
    {
        foreach (var board in Boards)
        {
            if (!pins.TryGetValue(board.Key, out var values)) continue;
            for (int i = 0; i < board.Pins.Count && i < values.Length; i++) board.Pins[i].Value = values[i];
        }
    }

    [RelayCommand]
    private void TogglePin(PinCellViewModel? cell)
    {
        if (cell is null) return;
        if (_session.Engine.Source != Manual)
        {
            Manual.Clear();
            _main.SetSource(Manual);
        }
        bool on = Manual.Toggle(cell.BoardKey, cell.Pin);
        AppLog.Info($"{cell.BoardKey} pin {cell.Pin} {(on ? "on" : "off")}{(cell.Usage.Length > 0 ? $" ({cell.Usage})" : "")}");
    }

    [RelayCommand]
    private void ClearPins() => Manual.Clear();

    // ---- adding and removing boards

    public IReadOnlyList<BoardModel> Models => BoardModels.All;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AddIds))]
    [NotifyPropertyChangedFor(nameof(AddNote))]
    private BoardModel _addModel = BoardModels.PacLed64;

    [ObservableProperty] private int _addId = 1;
    [ObservableProperty] private string? _boardStatus;

    public IReadOnlyList<int> AddIds => Enumerable.Range(1, AddModel.MaxBoardId).ToArray();
    public string AddNote => $"{AddModel.PinCount} pins. {AddModel.Notes}";

    /// <summary>Only the boards of the model picked in "Add a board" are shown (the user's request).</summary>
    private void ApplyModelFilter()
    {
        foreach (var b in Boards) b.IsShown = string.Equals(b.Model, AddModel.Model, StringComparison.OrdinalIgnoreCase);
        int hidden = Boards.Count(b => !b.IsShown);
        ShownNote = Boards.All(b => !b.IsShown)
            ? $"No {AddModel.DisplayName} board yet. Pick an ID and click Add."
            + (hidden > 0 ? $" ({hidden} other board(s) hidden; pick their model above to see them.)" : "")
            : hidden > 0 ? $"Showing {AddModel.DisplayName} boards; {hidden} other board(s) hidden." : null;
    }

    [ObservableProperty] private string? _shownNote;

    partial void OnAddModelChanged(BoardModel value)
    {
        BoardStatus = null; // the last add/remove message was about the other model
        ApplyModelFilter();
        PickFreeId();
    }

    private void PickFreeId()
    {
        var value = AddModel;
        // The first ID this model doesn't have yet.
        AddId = AddIds.FirstOrDefault(id => _session.Config.Wiring.FindBoard(value.KeyFor(id)) is null, 1);
    }

    [RelayCommand]
    private void AddBoard()
    {
        if (_session.Config.Wiring.FindBoard(AddModel.KeyFor(AddId)) is not null) { BoardStatus = $"{AddModel.NameFor(AddId)} is already there."; return; }
        _session.Checkpoint();
        var def = BoardModels.Add(_session.Config.Wiring, AddModel, AddId);
        if (def is null) { BoardStatus = $"{AddModel.NameFor(AddId)} is already there."; return; }
        AppLog.Info($"Boards: added {def.Name} ({def.Key}, {def.PinCount} pins).");
        _session.NotifyChanged(structural: true);
        BoardStatus = $"Added {def.Name}. Wire its pins in the Wiring tab (or run the wiring wizard).";
        PickFreeId();
    }

    [RelayCommand]
    private void RemoveBoard(BoardViewModel? board)
    {
        if (board is null) return;
        var wiring = _session.Config.Wiring;
        int wired = wiring.Bindings.Count(b => b.BoardKey == board.Key);
        var question = wired == 0
            ? $"Remove {board.Name} from LED-Next?"
            : $"Remove {board.Name}? Its {wired} wired pin(s) are disconnected from their buttons.";
        if (System.Windows.MessageBox.Show(question, "Remove board", System.Windows.MessageBoxButton.OKCancel,
                System.Windows.MessageBoxImage.Question) != System.Windows.MessageBoxResult.OK) return;
        _session.Checkpoint();
        int removed = BoardModels.Remove(wiring, board.Key);
        AppLog.Info($"Boards: removed {board.Name} ({board.Key}) and {removed} pin binding(s).");
        _session.NotifyChanged(structural: true);
        BoardStatus = $"Removed {board.Name}.";
        PickFreeId();
    }
}

public sealed partial class BoardViewModel : ObservableObject
{
    public BoardViewModel(BoardDefinition def)
    {
        Key = def.Key;
        Name = def.Name;
        Model = def.Model;
        for (int pin = 1; pin <= def.PinCount; pin++) Pins.Add(new PinCellViewModel(def.Key, pin));
    }

    public string Key { get; }
    public string Name { get; }
    public string Model { get; }
    public ObservableCollection<PinCellViewModel> Pins { get; } = new();

    [ObservableProperty] private string _status = "";
    /// <summary>False when another model is picked in "Add a board".</summary>
    [ObservableProperty] private bool _isShown = true;
}

public sealed partial class PinCellViewModel(string boardKey, int pin) : ObservableObject
{
    public string BoardKey { get; } = boardKey;
    public int Pin { get; } = pin;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Fill))]
    private byte _value;

    [ObservableProperty] private string _usage = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Fill))]
    private ColorChannel? _channel;

    /// <summary>Wired pins glow in their channel colour, unwired pins in white.</summary>
    public Brush Fill
    {
        get
        {
            if (Value == 0) return Brushes.Transparent;
            var full = Channel switch
            {
                ColorChannel.R => Color.FromRgb(255, 40, 40),
                ColorChannel.G => Color.FromRgb(40, 230, 60),
                ColorChannel.B => Color.FromRgb(60, 120, 255),
                _ => Colors.White,
            };
            double f = Value / 255.0;
            return ColorUtil.Frozen(Color.FromArgb((byte)(60 + 195 * f), full.R, full.G, full.B));
        }
    }
}
