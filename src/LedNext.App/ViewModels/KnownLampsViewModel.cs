using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LedNext.Core;
using LedNext.Core.Outputs;

namespace LedNext.App.ViewModels;

/// <summary>Lamps tab: every output an emulator has reported (games.db lamp_outputs), read only.</summary>
public sealed partial class KnownLampsViewModel : ObservableObject
{
    public const string AllEmulators = "(all emulators)";
    private readonly AppSession _session;

    public KnownLampsViewModel(AppSession session)
    {
        _session = session;
        Refresh();
    }

    public ObservableCollection<KnownLamp> Lamps { get; } = new();
    public ObservableCollection<string> Emulators { get; } = new() { AllEmulators };

    [ObservableProperty] private string _emulator = AllEmulators;
    [ObservableProperty] private string _filter = "";
    [ObservableProperty] private bool _unmappedOnly;
    [ObservableProperty] private string _summary = "";

    partial void OnEmulatorChanged(string value) => Refresh();
    partial void OnFilterChanged(string value) => Refresh();
    partial void OnUnmappedOnlyChanged(bool value) => Refresh();

    [RelayCommand]
    public void Refresh()
    {
        var db = _session.Games;
        if (db is null) { Summary = "The game library could not be opened."; return; }
        try
        {
            var emulators = db.LampEmulators();
            foreach (var e in emulators) if (!Emulators.Contains(e)) Emulators.Add(e);
            var list = db.ListLampOutputs(Emulator == AllEmulators ? null : Emulator, Filter, UnmappedOnly);
            Lamps.Clear();
            foreach (var l in list) Lamps.Add(l);
            int total = db.LampOutputCount();
            Summary = total == 0
                ? "No lamps known yet: they are added when a game with outputs ends (MAME, Flycast, Odin, Keisho, TeknoParrot…)."
                : $"{list.Count:N0} shown of {total:N0} known lamps · {list.Select(l => (l.Emulator, l.Game)).Distinct().Count():N0} games" +
                  $" · {list.Count(l => !l.Mapped):N0} not mapped";
        }
        catch (Exception ex)
        {
            AppLog.Error("Lamps tab: could not read the known lamps", ex);
            Summary = $"Could not read the known lamps: {ex.Message}";
        }
    }
}
