using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LedNext.Core;
using LedNext.Core.Lighting;

namespace LedNext.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private const int MaxLogLines = 500;
    private readonly AppSession _session;
    private readonly Dispatcher _dispatcher;
    private int _framePending;

    public MainViewModel(AppSession session)
    {
        _session = session;
        _dispatcher = Application.Current.Dispatcher;

        Panel = new PanelViewModel(session, this);
        Wiring = new WiringViewModel(session);
        Boards = new BoardsViewModel(session, this);

        AppLog.Logged += OnLogged;
        session.StructureChanged += OnStructureChanged;
        session.HistoryChanged += () =>
        {
            UndoCommand.NotifyCanExecuteChanged();
            RedoCommand.NotifyCanExecuteChanged();
        };
        session.WiringChanged += () =>
        {
            Wiring.RefreshConnections();
            Panel.RefreshWiringInfo();
            Boards.RefreshUsage();
        };
        session.Engine.FrameRendered += OnFrameRendered;

        _brightness = Math.Round(session.Config.Output.Brightness * 100);
        _gamma = session.Config.Output.Gamma;
        RefreshBoardStatus();
        SourceName = session.Engine.Source.Name;
    }

    public PanelViewModel Panel { get; }
    public WiringViewModel Wiring { get; }
    public BoardsViewModel Boards { get; }
    public ObservableCollection<string> LogLines { get; } = new();
    public string ConfigPath => _session.ConfigPath;

    [ObservableProperty] private string _sourceName = "";
    [ObservableProperty] private string? _frameStatus;
    [ObservableProperty] private string _boardsSummary = "";

    public bool IsHardwareMode
    {
        get => _session.Config.Hardware.Mode == HardwareMode.Hardware;
        set
        {
            var mode = value ? HardwareMode.Hardware : HardwareMode.Simulator;
            if (_session.Config.Hardware.Mode == mode) return;
            _session.Config.Hardware.Mode = mode;
            _session.MarkDirty();
            AppLog.Info($"Switched to {mode} mode.");
            OnPropertyChanged();
            Rescan();
        }
    }

    [ObservableProperty] private double _brightness;
    partial void OnBrightnessChanged(double value)
    {
        _session.Config.Output.Brightness = Math.Clamp(value / 100, 0, 1);
        _session.NotifyChanged();
    }

    [ObservableProperty] private double _gamma;
    partial void OnGammaChanged(double value)
    {
        _session.Config.Output.Gamma = Math.Round(Math.Clamp(value, 0.5, 3), 2);
        _session.NotifyChanged();
    }

    public void SetSource(ILightSource source)
    {
        _session.Engine.Source = source;
        SourceName = source.Name;
        FrameStatus = null;
    }

    [RelayCommand] private void Off() => SetSource(new AllOff());
    [RelayCommand] private void White() => SetSource(new SolidColor(Rgb.White, "All white"));
    [RelayCommand] private void Chase() => SetSource(new ButtonChase());
    [RelayCommand] private void RgbCycle() => SetSource(new RgbCycle());
    [RelayCommand] private void Rainbow() => SetSource(new Rainbow());
    [RelayCommand] private void PinWalk() => SetSource(new PinWalk());

    private bool CanUndo() => _session.CanUndo;
    private bool CanRedo() => _session.CanRedo;

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo() => _session.Undo();

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo() => _session.Redo();

    [RelayCommand]
    private void Rescan()
    {
        _session.Rescan();
        RefreshBoardStatus();
    }

    [RelayCommand]
    private void Save()
    {
        _session.MarkDirty();
        _session.SaveNow();
        AppLog.Info($"Saved {_session.ConfigPath}");
    }

    private void RefreshBoardStatus()
    {
        var online = _session.Engine.Boards;
        bool simulated = _session.Config.Hardware.Mode == HardwareMode.Simulator;
        Boards.RefreshStatus();
        Wiring.SetBoardStatus(online.Select(b => b.Key).ToHashSet(), simulated);

        int expected = _session.Config.Wiring.Boards.Count;
        int found = online.Count(b => _session.Config.Wiring.FindBoard(b.Key) is not null);
        BoardsSummary = simulated
            ? $"Simulator · {online.Count} virtual board(s)"
            : $"Hardware · {found} of {expected} board(s) online";
    }

    private void OnStructureChanged()
    {
        Panel.Rebuild();
        Wiring.Rebuild();
        Boards.Rebuild();
        RefreshBoardStatus();
    }

    private void OnFrameRendered(FrameSnapshot frame)
    {
        // Engine thread. Drop frames while the UI is still drawing the previous one.
        if (Interlocked.Exchange(ref _framePending, 1) == 1) return;
        _dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            try
            {
                Panel.ApplyFrame(frame.ButtonColors);
                Boards.ApplyFrame(frame.BoardPins);
                FrameStatus = frame.Status;
            }
            finally
            {
                Interlocked.Exchange(ref _framePending, 0);
            }
        });
    }

    private void OnLogged(LogEntry entry)
    {
        _dispatcher.BeginInvoke(() =>
        {
            LogLines.Add(entry.ToString());
            while (LogLines.Count > MaxLogLines) LogLines.RemoveAt(0);
        });
    }
}
