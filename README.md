# LED-Next

A modern replacement for LEDBlinky: visual arcade panel layout, node-graph LED wiring,
multiple LED boards per PC, and (coming) per-game lighting for LaunchBox/BigBox.

## Install
Close LED-Next, LaunchBox and BigBox, then double-click **`Install LED-Next.cmd`** (needs the .NET 8 SDK).
It builds LED-Next and installs it to `D:\LED-Next` (or `C:\LED-Next` with no D: drive). It also adds a
Start menu shortcut and installs the LaunchBox/BigBox plugin, pointed at the installed exe. The installed
folder holds only:

```
LED-Next.exe      the whole app in one file
PacDrive64.dll    Ultimarc's SDK DLL (copied from your current setup; not shipped with LED-Next)
config\           cabinet.json, games.db, window.json (your settings; never overwritten)
logs\             led-next.log
```

Run it again to update: `config\` is kept, and leftovers of an older multi-file install are removed. On the
first install, settings are copied from the repo's Debug build folder; that folder is left untouched.
Options (pass them to the `.cmd` or to `tools\install.ps1`):
- `-InstallDir E:\LED-Next`, `-LaunchBox E:\LaunchBox`
- `-From <old folder>`: where to copy the settings from
- `-NoPlugin`, `-NoShortcut`
- `-SelfContained`: a bigger exe that needs no .NET on that PC
- `-Uninstall`: keeps `config\`; add `-RemoveSettings` to delete it too.

## Build and run (development)
```
dotnet build LedNext.sln
dotnet run --project src/LedNext.App
dotnet test
```

The app starts in **Simulator** mode, so it works without boards attached. Switch to
**Hardware** mode on the cabinet. See `TASKS/00_ACTIVE_PRIORITIES.md` for hardware setup.

Config is stored in `config/cabinet.json` next to the exe; logs in `logs/led-next.log`.
