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
        Rebuild();
    }

    public ObservableCollection<BoardViewModel> Boards { get; } = new();
    public ManualPins Manual { get; } = new();

    public void Rebuild()
    {
        Boards.Clear();
        foreach (var def in _session.Config.Wiring.Boards) Boards.Add(new BoardViewModel(def));
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
