using System.IO;
using System.Windows.Threading;
using LedNext.Core;
using LedNext.Core.Lighting;
using LedNext.Data;
using LedNext.Hardware;

namespace LedNext.App;

/// <summary>Owns the live config, the engine and autosave. UI thread only (except the engine itself).</summary>
public sealed class AppSession : IDisposable
{
    private readonly DispatcherTimer _saveTimer;
    private bool _dirty;

    public AppSession()
    {
        var baseDir = AppContext.BaseDirectory;
        ConfigPath = Path.Combine(baseDir, "config", "cabinet.json");
        AppLog.SetFile(Path.Combine(baseDir, "logs", "led-next.log"));
        AppLog.Info($"LED-Next starting ({(Environment.Is64BitProcess ? "64" : "32")}-bit). Config: {ConfigPath}");

        Config = LoadConfig(ConfigPath);
        Engine = new LedEngine(Config);
        Engine.ReplaceProvider(BoardProviders.Create(Config));
        Engine.Start();

        try
        {
            Games = GameDatabase.Open(Path.Combine(ConfigDirectory, "games.db"));
            AppLog.Info($"Game library: {Games.Path} ({Games.GameCount():N0} games)");
        }
        catch (Exception ex)
        {
            AppLog.Error("Could not open the game library; the Games tab is disabled", ex);
        }

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _saveTimer.Tick += (_, _) => SaveNow();
    }

    public string ConfigPath { get; }
    public CabinetConfig Config { get; }
    public LedEngine Engine { get; }
    /// <summary>The game library (config/games.db), or null if it could not be opened.</summary>
    public GameDatabase? Games { get; }

    /// <summary>Raised after buttons are added, removed or renamed, or wiring changes.</summary>
    public event Action? StructureChanged;

    /// <summary>Pushes the edited config to the engine and schedules a save.</summary>
    public void NotifyChanged(bool structural = false)
    {
        Engine.ApplyConfig(Config);
        MarkDirty();
        if (structural) StructureChanged?.Invoke();
    }

    /// <summary>Raised after pin bindings change (no buttons added or removed).</summary>
    public event Action? WiringChanged;

    public void NotifyWiringChanged()
    {
        Engine.ApplyConfig(Config);
        MarkDirty();
        WiringChanged?.Invoke();
    }

    /// <summary>Schedules a save without touching the engine (e.g. graph node moved).</summary>
    public void MarkDirty()
    {
        _dirty = true;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    public IReadOnlyList<ILedBoard> Rescan() => Engine.ReplaceProvider(BoardProviders.Create(Config));

    public string ConfigDirectory => Path.GetDirectoryName(ConfigPath)!;

    // ---- Undo / redo -------------------------------------------------------------------------
    // Covers the panel layout and wiring. Output and hardware settings are deliberately excluded,
    // so undo never flips Simulator/Hardware mode or brightness.

    private readonly UndoHistory _history = new();
    private bool _undoEnabled = true;

    private sealed class EditableState
    {
        public PanelLayout Panel { get; set; } = new();
        public WiringMap Wiring { get; set; } = new();
    }

    /// <summary>Raised when undo/redo availability may have changed.</summary>
    public event Action? HistoryChanged;

    /// <summary>False while the wiring wizard runs (it has its own Back).</summary>
    public bool UndoEnabled
    {
        get => _undoEnabled;
        set { _undoEnabled = value; HistoryChanged?.Invoke(); }
    }

    public bool CanUndo => _undoEnabled && _history.CanUndo;
    public bool CanRedo => _undoEnabled && _history.CanRedo;

    /// <summary>Call just before changing the panel or wiring. Same key within ~1.5 s = one undo step.</summary>
    public void Checkpoint(string? coalesceKey = null)
    {
        if (!_undoEnabled) return;
        _history.Checkpoint(SerializeEditable(), coalesceKey);
        HistoryChanged?.Invoke();
    }

    // Steps identical to the current state (e.g. a click that started a drag but didn't move)
    // are skipped, so every Ctrl+Z visibly changes something.
    public void Undo()
    {
        if (!CanUndo) return;
        var current = SerializeEditable();
        string? state;
        do state = _history.Undo(current); while (state == current);
        if (state is not null) RestoreEditable(state, "Undo");
        else HistoryChanged?.Invoke();
    }

    public void Redo()
    {
        if (!CanRedo) return;
        var current = SerializeEditable();
        string? state;
        do state = _history.Redo(current); while (state == current);
        if (state is not null) RestoreEditable(state, "Redo");
        else HistoryChanged?.Invoke();
    }

    private string SerializeEditable() =>
        System.Text.Json.JsonSerializer.Serialize(new EditableState { Panel = Config.Panel, Wiring = Config.Wiring }, ConfigStore.JsonOptions);

    private void RestoreEditable(string json, string what)
    {
        var state = System.Text.Json.JsonSerializer.Deserialize<EditableState>(json, ConfigStore.JsonOptions)!;
        Config.Panel = state.Panel;
        Config.Wiring = state.Wiring;
        AppLog.Info(what);
        NotifyChanged(structural: true);
        HistoryChanged?.Invoke();
    }

    public void SaveNow()
    {
        _saveTimer.Stop();
        if (!_dirty) return;
        try
        {
            ConfigStore.Save(ConfigPath, Config);
            _dirty = false;
        }
        catch (Exception ex)
        {
            AppLog.Error($"Saving {ConfigPath} failed", ex);
        }
    }

    public void Dispose()
    {
        SaveNow();
        Engine.Dispose();
        AppLog.Info("LED-Next stopped.");
    }

    private static CabinetConfig LoadConfig(string path)
    {
        try
        {
            return ConfigStore.LoadOrCreate(path);
        }
        catch (Exception ex)
        {
            // Keep the broken file for inspection and start from defaults rather than refusing to run.
            var backup = $"{path}.broken-{DateTime.Now:yyyyMMdd-HHmmss}";
            AppLog.Error($"Could not read {path}; moved it to {backup} and created a default config", ex);
            try { File.Move(path, backup); } catch { /* best effort */ }
            return ConfigStore.LoadOrCreate(path);
        }
    }
}
