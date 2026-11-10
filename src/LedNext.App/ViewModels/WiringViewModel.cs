using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LedNext.Core;

namespace LedNext.App.ViewModels;

/// <summary>
/// Node graph: one node per button (R/G/B outputs) and one per board (pin inputs).
/// The config's <see cref="WiringMap"/> is the source of truth; the graph is rebuilt from it.
/// </summary>
public sealed partial class WiringViewModel : ObservableObject
{
    private readonly AppSession _session;
    private readonly Dictionary<(string ButtonId, ColorChannel Channel), ConnectorViewModel> _outputs = new();
    private readonly Dictionary<(string BoardKey, int Pin), ConnectorViewModel> _inputs = new();

    public WiringViewModel(AppSession session)
    {
        _session = session;
        Rebuild();
    }

    public ObservableCollection<NodeViewModel> Nodes { get; } = new();
    public ObservableCollection<ConnectionViewModel> Connections { get; } = new();

    [ObservableProperty] private string _summary = "";

    /// <summary>Placeholder content so the editor shows its pending-connection template while dragging.</summary>
    public object PendingConnection { get; } = new();

    private WiringMap Wiring => _session.Config.Wiring;

    public void Rebuild()
    {
        Nodes.Clear();
        _outputs.Clear();
        _inputs.Clear();

        var buttons = _session.Config.Panel.Buttons;
        // Default layout: one column per player, shared controls in column 0.
        var rowInColumn = new Dictionary<int, int>();
        foreach (var b in buttons)
        {
            if (!Wiring.ButtonNodePositions.TryGetValue(b.Id, out var pos))
            {
                int col = b.Player;
                int row = rowInColumn.GetValueOrDefault(col);
                rowInColumn[col] = row + 1;
                pos = new GraphPoint { X = col * 210, Y = row * 130 };
            }

            var node = new NodeViewModel(this, $"{b.Id}", b.Label, new Point(pos.X, pos.Y), boardKey: null, buttonId: b.Id);
            foreach (var ch in Enum.GetValues<ColorChannel>())
            {
                var c = new ConnectorViewModel(node, ch.ToString(), ColorUtil.ChannelBrush(ch), isInput: false) { ButtonId = b.Id, Channel = ch };
                node.Output.Add(c);
                _outputs[(b.Id, ch)] = c;
            }
            Nodes.Add(node);
        }

        foreach (var board in Wiring.Boards)
        {
            var node = new NodeViewModel(this, board.Name, board.Key, new Point(board.GraphX, board.GraphY), boardKey: board.Key, buttonId: null);
            for (int pin = 1; pin <= board.PinCount; pin++)
            {
                var c = new ConnectorViewModel(node, $"Pin {pin}", Brushes.Gainsboro, isInput: true) { BoardKey = board.Key, Pin = pin };
                node.Input.Add(c);
                _inputs[(board.Key, pin)] = c;
            }
            Nodes.Add(node);
        }

        RefreshConnections();
    }

    public void RefreshConnections()
    {
        Connections.Clear();
        foreach (var c in _outputs.Values) { c.IsConnected = false; c.Label = c.Title; }
        foreach (var c in _inputs.Values) { c.IsConnected = false; c.Label = c.Title; }

        foreach (var bind in Wiring.Bindings)
        {
            if (!_outputs.TryGetValue((bind.ButtonId, bind.Channel), out var output) ||
                !_inputs.TryGetValue((bind.BoardKey, bind.Pin), out var input))
                continue;
            output.IsConnected = input.IsConnected = true;
            var boardTag = bind.BoardKey.Contains('#') ? bind.BoardKey[(bind.BoardKey.LastIndexOf('#') + 1)..] : bind.BoardKey;
            output.Label = $"{bind.Channel} → #{boardTag} pin {bind.Pin}";
            input.Label = $"Pin {bind.Pin}  ·  {bind.ButtonId} {bind.Channel}";
            Connections.Add(new ConnectionViewModel(output, input, ColorUtil.ChannelBrush(bind.Channel)));
        }

        int wired = _session.Config.Panel.Buttons.Count(b => Enum.GetValues<ColorChannel>().All(ch => Wiring.Find(b.Id, ch) is not null));
        Summary = $"{wired} of {_session.Config.Panel.Buttons.Count} buttons fully wired · {Wiring.Bindings.Count} pins used";
    }

    public void SetBoardStatus(IReadOnlyCollection<string> onlineKeys, bool simulated)
    {
        foreach (var node in Nodes.Where(n => n.BoardKey is not null))
            node.Footer = onlineKeys.Contains(node.BoardKey!) ? (simulated ? "simulated" : "online") : "not detected";
    }

    internal void OnNodeMoved(NodeViewModel node)
    {
        if (node.BoardKey is not null)
        {
            var board = Wiring.FindBoard(node.BoardKey);
            if (board is null) return;
            board.GraphX = Math.Round(node.Location.X);
            board.GraphY = Math.Round(node.Location.Y);
        }
        else if (node.ButtonId is not null)
        {
            Wiring.ButtonNodePositions[node.ButtonId] = new GraphPoint { X = Math.Round(node.Location.X), Y = Math.Round(node.Location.Y) };
        }
        _session.MarkDirty();
    }

    /// <summary>Called by the editor with a (source, target) tuple; either end may be the output.</summary>
    [RelayCommand]
    private void Connect(object? parameter)
    {
        if (parameter is not Tuple<object, object> { Item1: ConnectorViewModel a, Item2: ConnectorViewModel b }) return;
        if (a.IsInput == b.IsInput) return; // pin→pin or button→button makes no sense
        var output = a.IsInput ? b : a;
        var input = a.IsInput ? a : b;

        _session.Checkpoint();
        Wiring.Connect(output.ButtonId!, output.Channel, input.BoardKey!, input.Pin);
        AppLog.Info($"Wired {output.ButtonId} {output.Channel} → {input.BoardKey} pin {input.Pin}");
        Changed();
    }

    [RelayCommand]
    private void DisconnectConnector(ConnectorViewModel? connector)
    {
        if (connector is null) return;
        _session.Checkpoint();
        if (connector.IsInput) Wiring.DisconnectPin(connector.BoardKey!, connector.Pin);
        else Wiring.Disconnect(connector.ButtonId!, connector.Channel);
        Changed();
    }

    [RelayCommand]
    private void RemoveConnection(ConnectionViewModel? connection)
    {
        if (connection is null) return;
        _session.Checkpoint();
        Wiring.Disconnect(connection.Output.ButtonId!, connection.Output.Channel);
        Changed();
    }

    [RelayCommand]
    private void AutoWire()
    {
        var result = MessageBox.Show(
            "Replace all wiring with sequential R, G, B pins in panel order?\nBoard 1 fills first, then board 2. (Ctrl+Z undoes this.)",
            "Auto-wire", MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (result != MessageBoxResult.OK) return;

        _session.Checkpoint();
        int unwired = Wiring.AutoWire(_session.Config.Panel);
        if (unwired > 0) AppLog.Warn($"Auto-wire: {unwired} button(s) did not fit on the available boards.");
        Changed();
    }

    [RelayCommand]
    private void ClearWiring()
    {
        if (MessageBox.Show("Remove all wiring?", "Clear wiring", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
        _session.Checkpoint();
        Wiring.Bindings.Clear();
        Changed();
    }

    private void Changed() => _session.NotifyWiringChanged();
}

public sealed partial class NodeViewModel : ObservableObject
{
    private readonly WiringViewModel _owner;

    public NodeViewModel(WiringViewModel owner, string title, string subtitle, Point location, string? boardKey, string? buttonId)
    {
        _owner = owner;
        Title = title;
        Subtitle = subtitle;
        _location = location;
        BoardKey = boardKey;
        ButtonId = buttonId;
    }

    public string Title { get; }
    public string Subtitle { get; }
    public string? BoardKey { get; }
    public string? ButtonId { get; }
    public bool IsBoard => BoardKey is not null;

    public ObservableCollection<ConnectorViewModel> Input { get; } = new();
    public ObservableCollection<ConnectorViewModel> Output { get; } = new();

    [ObservableProperty] private Point _location;
    [ObservableProperty] private string? _footer;

    partial void OnLocationChanged(Point value) => _owner.OnNodeMoved(this);
}

public sealed partial class ConnectorViewModel : ObservableObject
{
    public ConnectorViewModel(NodeViewModel node, string title, Brush brush, bool isInput)
    {
        Node = node;
        Title = title;
        _label = title;
        Brush = brush;
        IsInput = isInput;
    }

    public NodeViewModel Node { get; }
    public string Title { get; }
    public Brush Brush { get; }
    public bool IsInput { get; }

    public string? ButtonId { get; init; }
    public ColorChannel Channel { get; init; }
    public string? BoardKey { get; init; }
    public int Pin { get; init; }

    [ObservableProperty] private Point _anchor;
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private string _label;
}

public sealed class ConnectionViewModel(ConnectorViewModel output, ConnectorViewModel input, Brush brush)
{
    public ConnectorViewModel Output { get; } = output;
    public ConnectorViewModel Input { get; } = input;
    public Brush Brush { get; } = brush;
}
