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
        OnPropertyChanged(nameof(ControlIds));
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
        {
            var segments = wiring.FindBoard(board.Key)?.Segments ?? [];
            foreach (var pin in board.Pins)
            {
                var bind = wiring.FindByPin(board.Key, pin.Pin);
                if (bind is null && segments.FirstOrDefault(s => s.Contains((pin.Pin + 2) / 3)) is { } seg)
                {
                    // A strip segment's LED: it shows the control's colour.
                    pin.Usage = seg.ButtonId;
                    pin.Channel = (ColorChannel)((pin.Pin - 1) % 3);
                    continue;
                }
                pin.Usage = bind is null ? "" : $"{bind.ButtonId} {bind.Channel}";
                pin.Channel = bind?.Channel;
            }
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
    public string AddNote => AddModel.IsStrip ? AddModel.Notes : $"{AddModel.PinCount} pins. {AddModel.Notes}";

    // A strip's settings when adding one.
    [ObservableProperty] private int _addLeds = BoardModels.DefaultStripLeds;
    [ObservableProperty] private string? _addPort;
    [ObservableProperty] private int _addBaud = BoardModels.DefaultBaudRate;
    public IReadOnlyList<int> BaudRates => BoardModels.BaudRates;
    /// <summary>The PC's serial ports right now (refreshed when the list opens).</summary>
    public ObservableCollection<string> Ports { get; } = new();

    [RelayCommand]
    private void RefreshPorts()
    {
        string[] names;
        try { names = System.IO.Ports.SerialPort.GetPortNames(); }
        catch { names = []; }
        foreach (var n in names.Order(StringComparer.OrdinalIgnoreCase).Where(n => !Ports.Contains(n))) Ports.Add(n);
        foreach (var gone in Ports.Where(p => !names.Contains(p)).ToArray()) Ports.Remove(gone);
        AddPort ??= Ports.FirstOrDefault();
    }

    /// <summary>The panel's controls, for strip segments.</summary>
    public IReadOnlyList<string> ControlIds => _session.Config.Panel.Buttons.Select(b => b.Id).ToArray();

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
        if (value.IsStrip) RefreshPorts();
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
        if (AddModel.IsStrip && string.IsNullOrWhiteSpace(AddPort)) { BoardStatus = "Pick the strip's serial port (COM…) first."; return; }
        _session.Checkpoint();
        var def = BoardModels.Add(_session.Config.Wiring, AddModel, AddId, AddLeds, AddPort, AddBaud);
        if (def is null) { BoardStatus = $"{AddModel.NameFor(AddId)} is already there."; return; }
        AppLog.Info($"Boards: added {def.Name} ({def.Key}, {def.PinCount} pins" +
                    (def.SerialPort is { } p ? $", {def.LedCount} LEDs on {p} at {def.BaudRate} baud)." : ")."));
        _session.NotifyChanged(structural: true);
        BoardStatus = AddModel.IsStrip
            ? $"Added {def.Name}. Wire single LEDs in the Wiring tab, or give runs of LEDs to a control below."
            : $"Added {def.Name}. Wire its pins in the Wiring tab (or run the wiring wizard).";
        PickFreeId();
    }

    // ---- LED strips: settings and segments

    [RelayCommand]
    private void ApplyStrip(BoardViewModel? board)
    {
        if (board?.IsStrip != true || _session.Config.Wiring.FindBoard(board.Key) is not { } def) return;
        if (string.IsNullOrWhiteSpace(board.EditPort)) { BoardStatus = "Pick the strip's serial port first."; return; }
        _session.Checkpoint();
        def.SerialPort = board.EditPort.Trim();
        def.BaudRate = board.EditBaud;
        int lost = BoardModels.SetStripLeds(_session.Config.Wiring, def, board.EditLeds);
        AppLog.Info($"Boards: {def.Key} now {def.LedCount} LEDs on {def.SerialPort} at {def.BaudRate} baud" +
                    (lost > 0 ? $"; {lost} pin(s) past the end unwired." : "."));
        _session.NotifyChanged(structural: true);
        BoardStatus = $"{def.Name}: {def.LedCount} LEDs on {def.SerialPort}." + (lost > 0 ? $" {lost} wired pin(s) past the new end were disconnected." : "");
    }

    [RelayCommand]
    private void AddSegment(BoardViewModel? board)
    {
        if (board?.IsStrip != true || _session.Config.Wiring.FindBoard(board.Key) is not { } def) return;
        if (string.IsNullOrEmpty(board.SegmentControl)) { BoardStatus = "Pick the control the LEDs should follow."; return; }
        int leds = def.LedCount ?? def.PinCount / 3;
        int first = Math.Clamp(board.SegmentFirst, 1, leds);
        int count = Math.Clamp(board.SegmentCount, 1, leds - first + 1);
        _session.Checkpoint();
        (def.Segments ??= new()).Add(new StripSegment { ButtonId = board.SegmentControl, FirstLed = first, Count = count });
        AppLog.Info($"Boards: {def.Key} {def.Segments[^1]}.");
        _session.NotifyChanged(structural: true);
        BoardStatus = $"{def.Name}: {def.Segments[^1]}.";
    }

    [RelayCommand]
    private void RemoveSegment(SegmentViewModel? segment)
    {
        if (segment is null || _session.Config.Wiring.FindBoard(segment.BoardKey) is not { Segments: { } list } def) return;
        if (segment.Index < 0 || segment.Index >= list.Count) return;
        _session.Checkpoint();
        var s = list[segment.Index];
        list.RemoveAt(segment.Index);
        AppLog.Info($"Boards: {def.Key} removed {s}.");
        _session.NotifyChanged(structural: true);
        BoardStatus = $"{def.Name}: removed {s}.";
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
        IsStrip = BoardModels.Find(def.Model)?.IsStrip == true;
        for (int pin = 1; pin <= def.PinCount; pin++)
            Pins.Add(new PinCellViewModel(def.Key, pin, IsStrip ? BoardModels.PinName(def, pin)[4..] : pin.ToString()));
        if (!IsStrip) return;
        _editPort = def.SerialPort;
        _editBaud = def.BaudRate ?? BoardModels.DefaultBaudRate;
        _editLeds = def.LedCount ?? def.PinCount / 3;
        StripSummary = $"{def.SerialPort ?? "no port"} · {_editBaud} baud · {_editLeds} LEDs (Adalight)";
        var list = def.Segments ?? [];
        for (int i = 0; i < list.Count; i++) Segments.Add(new SegmentViewModel(def.Key, i, list[i].ToString()));
        _segmentFirst = list.Count == 0 ? 1 : Math.Min(_editLeds, list.Max(s => s.LastLed) + 1);
        _segmentCount = Math.Max(1, _editLeds - _segmentFirst + 1);
    }

    public string Key { get; }
    public string Name { get; }
    public string Model { get; }
    public ObservableCollection<PinCellViewModel> Pins { get; } = new();
    /// <summary>Strips: 15 columns, so a row is five whole LEDs.</summary>
    public int Columns => IsStrip ? 15 : 16;

    // ---- strips
    public bool IsStrip { get; }
    public string? StripSummary { get; }
    public ObservableCollection<SegmentViewModel> Segments { get; } = new();
    [ObservableProperty] private string? _editPort;
    [ObservableProperty] private int _editBaud;
    [ObservableProperty] private int _editLeds;
    [ObservableProperty] private string? _segmentControl;
    [ObservableProperty] private int _segmentFirst = 1;
    [ObservableProperty] private int _segmentCount = 1;

    [ObservableProperty] private string _status = "";
    /// <summary>False when another model is picked in "Add a board".</summary>
    [ObservableProperty] private bool _isShown = true;
}

/// <summary>One strip segment in the Boards tab; <see cref="Index"/> is its place in the board's list.</summary>
public sealed record SegmentViewModel(string BoardKey, int Index, string Text);

public sealed partial class PinCellViewModel(string boardKey, int pin, string label) : ObservableObject
{
    public string BoardKey { get; } = boardKey;
    public int Pin { get; } = pin;
    /// <summary>"7", or "3 G" (LED 3's green) on a strip.</summary>
    public string Label { get; } = label;

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
