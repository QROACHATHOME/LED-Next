using System.Text;

namespace LedNext.Core.FrontEnd;

/// <summary>What a setup step did, in words for the Front ends tab and the log.</summary>
public sealed record SetupResult(bool Ok, string Message);

/// <summary>
/// Sets up the front ends that don't have a LED-Next plugin of their own (Front ends tab). Only ever creates or
/// deletes LED-Next's own files (each carries <see cref="Marker"/>); never edits a front end's settings: those
/// steps are shown to the user instead.
/// <list type="bullet">
/// <item>LEDBlinky-style front ends (HyperSpin, HyperHQ, RetroFE / CoinOPS, RocketLauncher, PinUP Popper scripts):
/// <see cref="EnsureStandIn"/> puts a copy of LedNextCmd.exe named LEDBlinky.exe in its own folder, for front ends
/// that run "&lt;folder&gt;\LEDBlinky.exe".</item>
/// <item>ES-DE: one LED-Next.bat per event folder in &lt;ES-DE&gt;\scripts.</item>
/// <item>RetroBat: one LED-Next.bat in emulationstation\.emulationstation\scripts (run for every event, event first).</item>
/// <item>Attract-Mode: LED-Next's own plugin in &lt;Attract-Mode&gt;\plugins\LED-Next.</item>
/// </list>
/// </summary>
public static class FrontEndSetup
{
    public const string ToolName = "LedNextCmd.exe";
    public const string StandInFolderName = "LEDBlinky-stand-in";
    public const string ScriptName = "LED-Next.bat";
    /// <summary>In every file LED-Next writes, so it only ever removes its own.</summary>
    public const string Marker = "Written by LED-Next";

    /// <summary>The ES-DE events LED-Next uses (each gets its own scripts\&lt;event&gt; folder).</summary>
    public static readonly string[] EsDeEvents =
        { "startup", "quit", "game-start", "game-end", "game-select", "system-select", "screensaver-start", "screensaver-end" };

    public static string ToolPath(string appFolder) => Path.Combine(appFolder, ToolName);
    public static string StandInFolder(string appFolder) => Path.Combine(appFolder, StandInFolderName);
    public static string StandInExe(string appFolder) => Path.Combine(StandInFolder(appFolder), "LEDBlinky.exe");

    // ---- LEDBlinky stand-in ----------------------------------------------------------------------------------

    /// <summary>
    /// &lt;LED-Next&gt;\LEDBlinky-stand-in\LEDBlinky.exe, a copy of LedNextCmd.exe (refreshed when that is newer or
    /// a different size). The tool finds LED-Next's folder one level up, so it logs and starts LED-Next from there.
    /// </summary>
    public static SetupResult EnsureStandIn(string appFolder)
    {
        var tool = ToolPath(appFolder);
        if (!File.Exists(tool)) return new(false, $"{ToolName} isn't in {appFolder}; reinstall LED-Next (it is part of the release).");
        var target = StandInExe(appFolder);
        try
        {
            var src = new FileInfo(tool);
            var dst = new FileInfo(target);
            if (dst.Exists && dst.Length == src.Length && dst.LastWriteTimeUtc >= src.LastWriteTimeUtc)
                return new(true, $"{target} is up to date.");
            Directory.CreateDirectory(StandInFolder(appFolder));
            File.Copy(tool, target, overwrite: true);
            File.WriteAllText(Path.Combine(StandInFolder(appFolder), "README.txt"),
                $"{Marker}. LEDBlinky.exe here is LED-Next's command tool ({ToolName}) under LEDBlinky's name, for front ends\r\n" +
                "that run LEDBlinky.exe from a folder you give them (RetroFE / CoinOPS, HyperSpin, RocketLauncher...).\r\n" +
                "It passes their LEDBlinky commands to LED-Next. It is not LEDBlinky. Safe to delete; the Front ends tab makes it again.\r\n",
                new UTF8Encoding(false));
            return new(true, $"Made {target}.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new(false, $"Couldn't make {target}: {ex.Message}");
        }
    }

    // ---- ES-DE --------------------------------------------------------------------------------------------------

    /// <summary>
    /// ES-DE's application data folder (where its "scripts" folder goes): &lt;picked&gt;\ES-DE for a portable install
    /// (the folder with ES-DE.exe), the picked folder itself when it is the data folder, else %USERPROFILE%\ES-DE.
    /// Null when none of these exists.
    /// </summary>
    public static string? FindEsDeData(string? picked)
    {
        if (!string.IsNullOrWhiteSpace(picked))
        {
            if (File.Exists(Path.Combine(picked, "ES-DE.exe")) && Directory.Exists(Path.Combine(picked, "ES-DE")))
                return Path.Combine(picked, "ES-DE");
            if (Directory.Exists(Path.Combine(picked, "settings")) || Directory.Exists(Path.Combine(picked, "gamelists")))
                return picked;
            return null;
        }
        var home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "ES-DE");
        return Directory.Exists(home) ? home : null;
    }

    public static string EsDeScript(string toolPath, string eventName) =>
        $"@echo off\r\nrem {Marker}: hands ES-DE's {eventName} event to LED-Next. Safe to delete.\r\n" +
        $"\"{toolPath}\" --es-de {eventName} %*\r\n";

    public static SetupResult InstallEsDe(string esDeData, string toolPath)
    {
        try
        {
            foreach (var ev in EsDeEvents)
            {
                var dir = Path.Combine(esDeData, "scripts", ev);
                Directory.CreateDirectory(dir);
                WriteOwn(Path.Combine(dir, ScriptName), EsDeScript(toolPath, ev));
            }
            return new(true, $"Wrote {EsDeEvents.Length} scripts in {Path.Combine(esDeData, "scripts")}.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new(false, $"Couldn't write ES-DE's scripts: {ex.Message}");
        }
    }

    public static SetupResult RemoveEsDe(string esDeData)
    {
        int n = 0;
        foreach (var ev in EsDeEvents)
            if (DeleteOwn(Path.Combine(esDeData, "scripts", ev, ScriptName))) n++;
        return new(true, n == 0 ? "No LED-Next scripts were there." : $"Removed {n} LED-Next scripts.");
    }

    public static bool EsDeInstalled(string esDeData) =>
        EsDeEvents.All(ev => IsOwn(Path.Combine(esDeData, "scripts", ev, ScriptName)));

    // ---- RetroBat ---------------------------------------------------------------------------------------------

    /// <summary>RetroBat's folder (with retrobat.exe or emulationstation\): the picked one, else X:\RetroBat on any drive.</summary>
    public static string? FindRetroBat(string? picked)
    {
        static bool IsRetroBat(string d) =>
            Directory.Exists(Path.Combine(d, "emulationstation")) &&
            (File.Exists(Path.Combine(d, "retrobat.exe")) || Directory.Exists(Path.Combine(d, "emulationstation", ".emulationstation")));
        if (!string.IsNullOrWhiteSpace(picked)) return IsRetroBat(picked) ? picked : null;
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType is DriveType.Fixed && d.IsReady))
        {
            var d = Path.Combine(drive.RootDirectory.FullName, "RetroBat");
            if (IsRetroBat(d)) return d;
        }
        return null;
    }

    public static string RetroBatScriptPath(string retroBat) =>
        Path.Combine(retroBat, "emulationstation", ".emulationstation", "scripts", ScriptName);

    public static string RetroBatScript(string toolPath) =>
        $"@echo off\r\nrem {Marker}: hands RetroBat's events (the event name comes first) to LED-Next. Safe to delete.\r\n" +
        $"\"{toolPath}\" --retrobat %*\r\n";

    public static SetupResult InstallRetroBat(string retroBat, string toolPath)
    {
        var path = RetroBatScriptPath(retroBat);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            WriteOwn(path, RetroBatScript(toolPath));
            return new(true, $"Wrote {path}.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new(false, $"Couldn't write {path}: {ex.Message}");
        }
    }

    public static SetupResult RemoveRetroBat(string retroBat) =>
        DeleteOwn(RetroBatScriptPath(retroBat)) ? new(true, "Removed LED-Next's RetroBat script.") : new(true, "No LED-Next script was there.");

    public static bool RetroBatInstalled(string retroBat) => IsOwn(RetroBatScriptPath(retroBat));

    // ---- Attract-Mode -----------------------------------------------------------------------------------------

    /// <summary>Attract-Mode's folder (attract.exe / attractplus.exe, or attract.cfg): the picked one, else none.</summary>
    public static string? FindAttractMode(string? picked)
    {
        if (string.IsNullOrWhiteSpace(picked) || !Directory.Exists(picked)) return null;
        return File.Exists(Path.Combine(picked, "attract.exe")) || File.Exists(Path.Combine(picked, "attractplus.exe"))
               || File.Exists(Path.Combine(picked, "attract.cfg")) ? picked : null;
    }

    public static string AttractPluginPath(string attract) => Path.Combine(attract, "plugins", "LED-Next", "plugin.nut");

    public static SetupResult InstallAttractMode(string attract, string toolPath)
    {
        var path = AttractPluginPath(attract);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            WriteOwn(path, AttractModePlugin.Script(toolPath));
            return new(true, $"Wrote {path}. In Attract-Mode: Configure > Plug-ins > LED-Next > Enabled: Yes.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new(false, $"Couldn't write {path}: {ex.Message}");
        }
    }

    public static SetupResult RemoveAttractMode(string attract)
    {
        var path = AttractPluginPath(attract);
        if (!DeleteOwn(path)) return new(true, "No LED-Next plugin was there.");
        var dir = Path.GetDirectoryName(path)!;
        try { if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir); } catch (IOException) { }
        return new(true, "Removed LED-Next's Attract-Mode plugin.");
    }

    public static bool AttractModeInstalled(string attract) => IsOwn(AttractPluginPath(attract));

    // ---- PinballX and Playnite plugins (shipped in <LED-Next>\front-ends) ---------------------------------------

    public const string PinballXPluginFile = "LED-Next PinballX Plugin.dll";
    public const string PlayniteFolderName = "LED-Next";

    /// <summary>&lt;LED-Next&gt;\front-ends: the PinballX and Playnite plugins as the release (or a build) ships them.</summary>
    public static string HelpersFolder(string appFolder) => Path.Combine(appFolder, "front-ends");

    /// <summary>PinballX's folder (PinballX.exe, or a Plugins folder with PinballX's own plugins), else C:\PinballX or X:\PinballX.</summary>
    public static string? FindPinballX(string? picked)
    {
        static bool IsPbx(string d) => File.Exists(Path.Combine(d, "PinballX.exe"));
        if (!string.IsNullOrWhiteSpace(picked)) return IsPbx(picked) ? picked : null;
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType is DriveType.Fixed && d.IsReady))
        {
            var d = Path.Combine(drive.RootDirectory.FullName, "PinballX");
            if (IsPbx(d)) return d;
        }
        return null;
    }

    public static string PinballXPluginPath(string pinballX) => Path.Combine(pinballX, "Plugins", PinballXPluginFile);

    public static SetupResult InstallPinballX(string pinballX, string appFolder)
    {
        var src = Path.Combine(HelpersFolder(appFolder), "PinballX", PinballXPluginFile);
        if (!File.Exists(src)) return new(false, $"{src} is missing; reinstall LED-Next (the plugin is part of the release).");
        var dst = PinballXPluginPath(pinballX);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            File.Copy(src, dst, overwrite: true);
            return new(true, $"Copied the plugin to {dst}. In PinballX: Plugin Manager > tick LED-Next. Close PinballX first next time you update it.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new(false, $"Couldn't copy the plugin (close PinballX first): {ex.Message}");
        }
    }

    public static SetupResult RemovePinballX(string pinballX)
    {
        var dst = PinballXPluginPath(pinballX);
        if (!File.Exists(dst)) return new(true, "The plugin wasn't there.");
        try { File.Delete(dst); return new(true, $"Removed {dst}."); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return new(false, $"Couldn't remove it (close PinballX first): {ex.Message}"); }
    }

    public static bool PinballXInstalled(string pinballX) => File.Exists(PinballXPluginPath(pinballX));

    /// <summary>
    /// Playnite's Extensions folder: &lt;picked&gt;\Extensions for a portable Playnite (the folder with
    /// Playnite.DesktopApp.exe), else %APPDATA%\Playnite\Extensions when Playnite is installed. Null when neither.
    /// </summary>
    public static string? FindPlayniteExtensions(string? picked)
    {
        if (!string.IsNullOrWhiteSpace(picked))
            return File.Exists(Path.Combine(picked, "Playnite.DesktopApp.exe")) ? Path.Combine(picked, "Extensions") : null;
        var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Playnite");
        return Directory.Exists(appData) ? Path.Combine(appData, "Extensions") : null;
    }

    public static SetupResult InstallPlaynite(string extensions, string appFolder)
    {
        var src = Path.Combine(HelpersFolder(appFolder), "Playnite");
        if (!File.Exists(Path.Combine(src, "extension.yaml"))) return new(false, $"{src} is missing; reinstall LED-Next (the extension is part of the release).");
        var dst = Path.Combine(extensions, PlayniteFolderName);
        try
        {
            Directory.CreateDirectory(dst);
            foreach (var f in Directory.GetFiles(src)) File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), overwrite: true);
            return new(true, $"Copied the extension to {dst}. Restart Playnite; it shows under Add-ons > Extensions.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new(false, $"Couldn't copy the extension (close Playnite first): {ex.Message}");
        }
    }

    public static SetupResult RemovePlaynite(string extensions)
    {
        var dst = Path.Combine(extensions, PlayniteFolderName);
        // Only ever LED-Next's own extension folder (its manifest names LED-Next's module).
        var manifest = Path.Combine(dst, "extension.yaml");
        if (!File.Exists(manifest) || !File.ReadAllText(manifest).Contains("LedNext.Playnite.dll", StringComparison.Ordinal))
            return new(true, "The extension wasn't there.");
        try { Directory.Delete(dst, recursive: true); return new(true, $"Removed {dst}."); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return new(false, $"Couldn't remove it (close Playnite first): {ex.Message}"); }
    }

    public static bool PlayniteInstalled(string extensions) => File.Exists(Path.Combine(extensions, PlayniteFolderName, "LedNext.Playnite.dll"));

    // ---- own files ----------------------------------------------------------------------------------------------

    /// <summary>Writes the file unless one that isn't LED-Next's is there (then throws: never overwrite the user's).</summary>
    private static void WriteOwn(string path, string text)
    {
        if (File.Exists(path) && !IsOwn(path)) throw new IOException($"{path} exists and isn't LED-Next's; left as it is.");
        File.WriteAllText(path, text, new UTF8Encoding(false)); // no BOM: cmd.exe would run "ï»¿@echo off"
    }

    public static bool IsOwn(string path)
    {
        try { return File.Exists(path) && File.ReadLines(path).Take(3).Any(l => l.Contains(Marker, StringComparison.Ordinal)); }
        catch (IOException) { return false; }
    }

    private static bool DeleteOwn(string path)
    {
        if (!IsOwn(path)) return false;
        File.Delete(path);
        return true;
    }
}
