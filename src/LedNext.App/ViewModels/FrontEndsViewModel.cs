using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LedNext.Core;
using LedNext.Core.FrontEnd;

namespace LedNext.App.ViewModels;

/// <summary>
/// Front ends tab: how each front end hands its events to LED-Next, and the setup LED-Next can do itself (only its
/// own files: scripts, plugins, the LEDBlinky stand-in; a front end's own settings are steps for the user).
/// </summary>
public sealed partial class FrontEndsViewModel : ObservableObject
{
    private readonly AppSession _session;

    public FrontEndsViewModel(AppSession session)
    {
        _session = session;
        AppFolder = AppContext.BaseDirectory.TrimEnd('\\');
        var stand = FrontEndSetup.EnsureStandIn(AppFolder);
        if (!stand.Ok) AppLog.Warn($"Front ends: {stand.Message}");
        else if (stand.Message.StartsWith("Made", StringComparison.Ordinal)) AppLog.Info($"Front ends: {stand.Message}");
        StandInStatus = stand.Message;
        Build();
    }

    public string AppFolder { get; }
    public string ToolPath => FrontEndSetup.ToolPath(AppFolder);
    public string StandInExe => FrontEndSetup.StandInExe(AppFolder);
    public string StandInFolder => FrontEndSetup.StandInFolder(AppFolder);
    [ObservableProperty] private string _standInStatus = "";

    public ObservableCollection<FrontEndCard> Cards { get; } = new();

    private FrontEndFolders Folders => _session.Config.FrontEnds;

    private void Build()
    {
        var tool = ToolPath;
        var standIn = StandInExe;
        var standInFolder = StandInFolder;
        const string Lb = "LEDBlinky-style: LED-Next's LEDBlinky.exe stand-in takes LEDBlinky's commands.";

        Cards.Add(FrontEndCard.Info(this, "LaunchBox / BigBox", "Plugin (installed by LED-Next's installer).",
            "Selection (games and platforms), launch, exit, start-up and closing. Nothing to set up here: \"Install LED-Next.cmd\" installs the plugin when it finds LaunchBox.",
            null));

        Cards.Add(FrontEndCard.Info(this, "HyperSpin 2 (HyperHQ)", Lb,
            "In HyperHQ: HyperSpin > Lights: turn on \"Enable LEDs\" and set \"LEDBlinky Executable Path\" to the path below. " +
            "Leave \"Auto-Sync LEDBlinky Config\" and \"MAME LED Triggers\" off (they write LEDBlinky's own files; LED-Next reads MAME's lamps itself). " +
            "HyperSpin 2 is still in beta: if the lights don't change back after a game, tell us what LED-Next's log shows.",
            standIn));

        Cards.Add(FrontEndCard.Info(this, "HyperSpin 1.x", Lb,
            "Point HyperSpin's LEDBlinky setting (HyperHQ, Main Settings) at the path below. If your HyperSpin starts games through " +
            "RocketLauncher, set RocketLauncher's LEDBlinky path to it too (see RocketLauncher). These steps come from LEDBlinky's documentation " +
            "and haven't been tried with LED-Next yet.",
            standIn));

        Cards.Add(FrontEndCard.Info(this, "RetroFE / CoinOPS", Lb,
            "In RetroFE's settings.conf add the line  LEDBlinkyDirectory = <the folder below>  (RetroFE runs LEDBlinky.exe from that folder). " +
            "RetroFE names a game's emulator by its launcher; when that isn't a platform LED-Next knows (\"retroarch\"), LED-Next uses the " +
            "collection selected before. A launcher can name its platform with  LEDBlinkyEmulator = MAME  in its .conf.",
            standInFolder));

        Cards.Add(FrontEndCard.Info(this, "RocketLauncher", Lb,
            "In RocketLauncherUI, set LEDBlinky's path (its third-party settings) to the path below and enable it. RocketLauncher then sends " +
            "game start and end; the front end in front of it (HyperSpin…) sends the selections.",
            standIn));

        Cards.Add(FrontEndCard.WithFolder(this, "Attract-Mode / Attract-Mode Plus", "LED-Next's own Attract-Mode plugin (plugins\\LED-Next\\plugin.nut).",
            "Pick Attract-Mode's folder (with attract.exe or attractplus.exe) and click Install. Then in Attract-Mode: Configure > Plug-ins > LED-Next > Enabled: Yes. " +
            "Selection, list change, game start and end, screensaver, start-up and exit. The game's System (romlist) names its platform.",
            () => Folders.AttractMode, v => Folders.AttractMode = v,
            FrontEndSetup.FindAttractMode, d => FrontEndSetup.InstallAttractMode(d, tool), FrontEndSetup.RemoveAttractMode, FrontEndSetup.AttractModeInstalled,
            "Attract-Mode isn't picked yet."));

        Cards.Add(FrontEndCard.WithFolder(this, "ES-DE (EmulationStation Desktop Edition)", "Event scripts (scripts\\<event>\\LED-Next.bat).",
            "Install writes one small script per event. Then in ES-DE: Main menu > Other settings: turn on \"Enable custom event scripts\" and " +
            "\"Browsing custom events\" (for the selection). Leave it empty for the installed ES-DE (%USERPROFILE%\\ES-DE), or pick a portable ES-DE's folder.",
            () => Folders.EsDe, v => Folders.EsDe = v,
            FrontEndSetup.FindEsDeData, d => FrontEndSetup.InstallEsDe(d, tool), FrontEndSetup.RemoveEsDe, FrontEndSetup.EsDeInstalled,
            "No ES-DE found (%USERPROFILE%\\ES-DE); pick its folder."));

        Cards.Add(FrontEndCard.WithFolder(this, "RetroBat", "An event script (emulationstation\\.emulationstation\\scripts\\LED-Next.bat).",
            "Install writes one script RetroBat runs for every event (in the background, so browsing never waits). Selection, system change, game start and end, " +
            "screensaver, start-up and quit. RetroBat's game start doesn't say the system: LED-Next takes it from the rom's folder (roms\\<system>).",
            () => Folders.RetroBat, v => Folders.RetroBat = v,
            FrontEndSetup.FindRetroBat, d => FrontEndSetup.InstallRetroBat(d, tool), FrontEndSetup.RemoveRetroBat, FrontEndSetup.RetroBatInstalled,
            "No RetroBat found (X:\\RetroBat); pick its folder."));

        Cards.Add(FrontEndCard.WithFolder(this, "Playnite", "LED-Next's Playnite extension (Extensions\\LED-Next).",
            "Install copies the extension; restart Playnite. Selection, game starting and stopped, start-up and closing, in desktop and fullscreen mode. " +
            "Leave it empty for the installed Playnite (%APPDATA%\\Playnite), or pick a portable Playnite's folder. A game's first platform names its LED-Next platform.",
            () => Folders.Playnite, v => Folders.Playnite = v,
            FrontEndSetup.FindPlayniteExtensions, d => FrontEndSetup.InstallPlaynite(d, AppFolder), FrontEndSetup.RemovePlaynite, FrontEndSetup.PlayniteInstalled,
            "No Playnite found (%APPDATA%\\Playnite); pick a portable Playnite's folder."));

        Cards.Add(FrontEndCard.WithFolder(this, "PinballX", "LED-Next's PinballX plugin (Plugins\\LED-Next PinballX Plugin.dll).",
            "Close PinballX, click Install, then in PinballX: Plugin Manager > tick LED-Next. Selected table, game start and exit, screensaver, start-up and closing. " +
            "A table gets its system's lights (Visual Pinball, Future Pinball…) unless you give it its own in the Games tab. " +
            "Using DOF on an LED-Wiz or Pinscape? Don't let LED-Next drive the same board: DOF drives it during play.",
            () => Folders.PinballX, v => Folders.PinballX = v,
            FrontEndSetup.FindPinballX, d => FrontEndSetup.InstallPinballX(d, AppFolder), FrontEndSetup.RemovePinballX, FrontEndSetup.PinballXInstalled,
            "No PinballX found (X:\\PinballX); pick its folder."));

        Cards.Add(FrontEndCard.Info(this, "PinUP Popper", "LedNextCmd.exe in each emulator's launch and close scripts.",
            "In PinUP Popper Setup > Emulators, for each emulator (Visual Pinball X, Future Pinball…), Launch Setup: add the first line below at the top of " +
            "\"Launch Script\" and the second at the top of \"Close Script\" (change Visual_Pinball to the emulator's platform). Game start and exit only: " +
            "Popper has no event for the selected table. [GAMENAME] is Popper's table name variable.",
            $"\"{tool}\" --source \"PinUP Popper\" \"[GAMENAME]\" \"Visual_Pinball\"\r\n\"{tool}\" --source \"PinUP Popper\" 4"));

        Cards.Add(FrontEndCard.Info(this, "Anything else", "LedNextCmd.exe, with LEDBlinky's command line.",
            "Any front end or script that can run a program: 9 <rom> <emulator> (selected), <rom> <emulator> (game start), 4 (game end), 8 <emulator> (list), " +
            "1 / 2 (front end start / quit). EmulationStation forks like RetroBat's: --retrobat <event> <args>. Every command is logged in logs\\led-next-commands.log.",
            tool));
    }

    [RelayCommand]
    private void OpenCommandLog()
    {
        var log = Path.Combine(AppFolder, "logs", "led-next-commands.log");
        if (!File.Exists(log)) { StandInStatus = "No command has come in yet (logs\\led-next-commands.log)."; return; }
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(log) { UseShellExecute = true }); }
        catch (Exception ex) { StandInStatus = $"Couldn't open the log: {ex.Message}"; }
    }

    internal void Saved() => _session.MarkDirty();
}

/// <summary>One front end on the Front ends tab.</summary>
public sealed partial class FrontEndCard : ObservableObject
{
    private readonly FrontEndsViewModel _owner;
    private Action<string?>? _set;
    private Func<string?, string?>? _find;
    private Func<string, SetupResult>? _install;
    private Func<string, SetupResult>? _remove;
    private Func<string, bool>? _installed;
    private string _notFound = "";

    private FrontEndCard(FrontEndsViewModel owner, string name, string how, string steps)
    {
        _owner = owner;
        Name = name;
        How = how;
        Steps = steps;
    }

    public string Name { get; }
    public string How { get; }
    public string Steps { get; }
    /// <summary>A path or lines to paste into the front end (with a Copy button); null for none.</summary>
    public string? CopyText { get; private init; }
    public bool HasCopyText => !string.IsNullOrEmpty(CopyText);
    public bool CanInstall => _install is not null;

    [ObservableProperty] private string _folder = "";
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _isInstalled;

    public static FrontEndCard Info(FrontEndsViewModel owner, string name, string how, string steps, string? copy) =>
        new(owner, name, how, steps) { CopyText = copy };

    public static FrontEndCard WithFolder(FrontEndsViewModel owner, string name, string how, string steps,
        Func<string?> get, Action<string?> set, Func<string?, string?> find, Func<string, SetupResult> install,
        Func<string, SetupResult> remove, Func<string, bool> installed, string notFound)
    {
        var c = new FrontEndCard(owner, name, how, steps)
        {
            _set = set, _find = find, _install = install, _remove = remove, _installed = installed, _notFound = notFound,
        };
        c._folder = get() ?? "";
        c.Refresh();
        return c;
    }

    /// <summary>Where setup goes: the folder found from the picked one (or the usual place), null when there is none.</summary>
    private string? Target() => _find?.Invoke(string.IsNullOrWhiteSpace(Folder) ? null : Folder.Trim());

    private void Refresh()
    {
        if (_find is null) return;
        var target = Target();
        IsInstalled = target is not null && _installed!(target);
        Status = target is null
            ? (string.IsNullOrWhiteSpace(Folder) ? _notFound : $"That isn't {Name}'s folder.")
            : IsInstalled ? $"Set up in {target}." : $"Found {target}; not set up yet.";
    }

    partial void OnFolderChanged(string value)
    {
        if (_set is null) return;
        _set(string.IsNullOrWhiteSpace(value) ? null : value.Trim());
        _owner.Saved();
        Refresh();
    }

    [RelayCommand]
    private void Browse()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = $"{Name}: pick its folder" };
        if (!string.IsNullOrWhiteSpace(Folder) && Directory.Exists(Folder)) dialog.InitialDirectory = Folder;
        if (dialog.ShowDialog() == true) Folder = dialog.FolderName;
    }

    [RelayCommand]
    private void Install()
    {
        var target = Target();
        if (target is null) { Refresh(); return; }
        var r = _install!(target);
        (r.Ok ? (Action<string>)AppLog.Info : AppLog.Warn)($"Front ends: {Name}: {r.Message}");
        Refresh();
        Status = r.Message;
    }

    [RelayCommand]
    private void Remove()
    {
        var target = Target();
        if (target is null) { Refresh(); return; }
        var r = _remove!(target);
        (r.Ok ? (Action<string>)AppLog.Info : AppLog.Warn)($"Front ends: {Name}: {r.Message}");
        Refresh();
        Status = r.Message;
    }

    [RelayCommand]
    private void Copy()
    {
        if (string.IsNullOrEmpty(CopyText)) return;
        try
        {
            Clipboard.SetText(CopyText);
            Status = "Copied.";
        }
        catch (Exception ex)
        {
            Status = $"Couldn't copy: {ex.Message}";
        }
    }
}
