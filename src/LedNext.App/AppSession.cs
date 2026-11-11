using System.IO;
using System.Windows.Threading;
using LedNext.Core;
using LedNext.Core.Games;
using LedNext.Core.Import.LedBlinky;
using LedNext.Core.Import.Mame;
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
        if (structural)
        {
            ResetKeyLayouts();
            StructureChanged?.Invoke();
        }
    }

    // ---- MAME installs' own keys -----------------------------------------------------------------

    private MameKeyLayouts? _keyLayouts;
    private bool _keyLayoutsLoaded;

    /// <summary>Forget the panel's keys (wiring, panel or LEDBlinky import changed); read again when next needed.</summary>
    public void ResetKeyLayouts()
    {
        _keyLayouts = null;
        _keyLayoutsLoaded = false;
    }

    /// <summary>
    /// The control map for one game: the cabinet's map, with the controls that the game's MAME install (the emulator
    /// LaunchBox runs it with) or its own cfg puts on other buttons moved. The cabinet's map when that is unknown.
    /// </summary>
    /// <param name="launchBoxId">The LaunchBox game, when a front end reported it (the same game can be in LaunchBox twice).</param>
    public (ControlMap Map, string? Note) ControlMapFor(string platform, string? game, string? launchBoxId = null)
    {
        var baseMap = Config.ControlMap;
        if (Games is null || (game is null && launchBoxId is null)) return (baseMap, null);
        try
        {
            var emulator = Games.LaunchBoxEmulator(launchBoxId, platform, game);
            var folder = MameKeyLayouts.MameFolderOf(emulator?.Emulator);
            if (emulator is not { } e || folder is null || KeyLayouts() is not { } layouts) return (baseMap, null);
            var rom = e.Rom.Length > 0 ? e.Rom : game;
            var layout = layouts.For(baseMap, Config.Panel, folder, rom);
            return (layout.Map, layout.Note);
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Could not read the MAME keys for {platform}/{game}: {ex.Message}");
            return (baseMap, null);
        }
    }

    private MameKeyLayouts? KeyLayouts()
    {
        if (_keyLayoutsLoaded) return _keyLayouts;
        _keyLayoutsLoaded = true;
        var folder = Config.Games.LedBlinkyFolder;
        var inputMap = string.IsNullOrEmpty(folder) ? null : Path.Combine(folder, "LEDBlinkyInputMap.xml");
        if (inputMap is null || !File.Exists(inputMap))
        {
            AppLog.Info("MAME installs' own keys are not used: LEDBlinky's input map (which key each button sends) was not found.");
            return null;
        }
        try
        {
            var codes = ControlMapBuilder.CollectButtonCodes(LedBlinkyInputMap.Load(inputMap), Config.Wiring, new List<string>());
            _keyLayouts = new MameKeyLayouts(codes);
            AppLog.Info($"MAME installs' own keys: {codes.Count} panel buttons with keys (from {inputMap}).");
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Could not read {inputMap}: {ex.Message}. MAME installs' own keys are not used.");
        }
        return _keyLayouts;
    }

    /// <summary>Raised after pin bindings change (no buttons added or removed).</summary>
    public event Action? WiringChanged;

    public void NotifyWiringChanged()
    {
        Engine.ApplyConfig(Config);
        MarkDirty();
        ResetKeyLayouts();
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
