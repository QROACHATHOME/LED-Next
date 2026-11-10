using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LedNext.Core;
using LedNext.Core.Lighting;

namespace LedNext.App.ViewModels;

public sealed record BoardChoice(string? Key, string Name)
{
    public override string ToString() => Name;
}

/// <summary>
/// Drives <see cref="WiringWizard"/> from the Panel tab: one pin is lit, the user clicks the button
/// that lit up and presses R, G or B (or Enter for the suggestion).
/// </summary>
public sealed partial class WizardViewModel : ObservableObject
{
    private readonly AppSession _session;
    private readonly PanelViewModel _panel;
    private readonly MainViewModel _main;
    private WiringWizard? _wizard;
    private int _assigned;

    public WizardViewModel(AppSession session, PanelViewModel panel, MainViewModel main)
    {
        _session = session;
        _panel = panel;
        _main = main;
        RefreshBoardChoices();
    }

    public ObservableCollection<BoardChoice> BoardChoices { get; } = new();

    [ObservableProperty] private BoardChoice? _selectedBoard;
    [ObservableProperty] private bool _clearFirst = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSetup))]
    private bool _isActive;

    public bool IsSetup => !IsActive;

    [ObservableProperty] private string _stepTitle = "";
    [ObservableProperty] private string _currentInfo = "";
    [ObservableProperty] private string _selectionInfo = "";
    [ObservableProperty] private string _suggestionText = "";
    [ObservableProperty] private string _hint = "";
    [ObservableProperty] private double _progress;
    [ObservableProperty] private bool _isDone;

    public void RefreshBoardChoices()
    {
        var previous = SelectedBoard?.Key;
        BoardChoices.Clear();
        BoardChoices.Add(new BoardChoice(null, "All boards"));
        foreach (var b in _session.Config.Wiring.Boards) BoardChoices.Add(new BoardChoice(b.Key, b.Name));
        SelectedBoard = BoardChoices.FirstOrDefault(c => c.Key == previous) ?? BoardChoices[0];
    }

    public void OnSelectionChanged()
    {
        if (IsActive) Update();
    }

    [RelayCommand]
    private void Start()
    {
        if (IsActive) return;
        try
        {
            _session.Checkpoint(); // the whole wizard run is one undo step afterwards
            var keys = SelectedBoard?.Key is { } k ? new[] { k } : null;
            _wizard = new WiringWizard(_session.Config.Wiring, keys, ClearFirst);
        }
        catch (Exception ex)
        {
            AppLog.Error("Could not start the wiring wizard", ex);
            return;
        }

        _session.UndoEnabled = false;
        _assigned = 0;
        IsActive = true;
        Hint = "";
        AppLog.Info($"Wiring wizard started ({SelectedBoard?.Name}, {(ClearFirst ? "starting from empty wiring" : "keeping existing wiring")}).");
        _session.NotifyWiringChanged();
        Update();
    }

    /// <param name="channel">"R", "G" or "B".</param>
    [RelayCommand]
    private void Assign(string channel)
    {
        if (_wizard is null || _wizard.IsDone) return;
        if (_panel.Selected is null)
        {
            Hint = "First click the button on the panel that lit up.";
            return;
        }
        var ch = Enum.Parse<ColorChannel>(channel);
        AppLog.Info($"Wizard: {_wizard.BoardName} pin {_wizard.Pin} = {_panel.Selected.Id} {ch}");
        _wizard.Assign(_panel.Selected.Id, ch);
        _assigned++;
        Hint = "";
        _session.NotifyWiringChanged();
        Update();
    }

    [RelayCommand]
    private void AcceptSuggestion()
    {
        if (_wizard is null) return;
        var s = _wizard.Suggest(_panel.Selected?.Id);
        if (s is null)
        {
            Hint = _panel.Selected is null
                ? "First click the button on the panel that lit up."
                : "This button already has R, G and B. Click the next button, or press R/G/B.";
            return;
        }
        Assign(s.Value.ToString());
    }

    [RelayCommand]
    private void Skip()
    {
        if (_wizard is null || _wizard.IsDone) return;
        AppLog.Info($"Wizard: {_wizard.BoardName} pin {_wizard.Pin} skipped");
        _wizard.Skip();
        Hint = "";
        Update();
    }

    [RelayCommand]
    private void Back()
    {
        if (_wizard is null || !_wizard.Back()) return;
        Hint = "";
        _session.NotifyWiringChanged();
        Update();
    }

    [RelayCommand]
    private void Finish()
    {
        if (!IsActive) return;
        AppLog.Info($"Wiring wizard finished: {_assigned} pin(s) assigned. Ctrl+Z undoes the whole run.");
        _wizard = null;
        IsActive = false;
        _session.UndoEnabled = true;
        _main.SetSource(new AllOff());
    }

    private void Update()
    {
        if (_wizard is null) return;
        Progress = _wizard.Total == 0 ? 0 : 100.0 * _wizard.Completed / _wizard.Total;
        IsDone = _wizard.IsDone;

        var selected = _panel.Selected?.Id;
        SelectionInfo = selected is null ? "No button selected" : $"Selected: {selected}";

        if (_wizard.IsDone)
        {
            StepTitle = "All pins done";
            CurrentInfo = $"{_assigned} pin(s) assigned. Press Finish, or Back to fix the last one.";
            SuggestionText = "";
            _main.SetSource(new AllOff());
            return;
        }

        StepTitle = $"{_wizard.BoardName} · pin {_wizard.Pin} of {_wizard.PinCount}";
        var current = _wizard.CurrentBinding;
        CurrentInfo = current is null ? "This pin isn't wired yet." : $"Currently wired to {current.ButtonId} {current.Channel}.";

        var suggestion = _wizard.Suggest(selected);
        SuggestionText = suggestion is null || selected is null ? "" : $"Enter = {selected} {suggestion}";

        _main.SetSource(new SinglePin(_wizard.BoardKey, _wizard.BoardName, _wizard.Pin));
    }
}
