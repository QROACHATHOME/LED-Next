# LED-Next

A modern replacement for LEDBlinky: visual arcade panel layout, node-graph LED wiring,
multiple LED boards per PC, and per-game lighting for LaunchBox/BigBox.

## What it supports
Tested on a 4-player cabinet with two Ultimarc PacLED64 boards, LaunchBox/BigBox 14.1 and LEDBlinky's data.

**LED controllers**
- Ultimarc **PacLED64** (RGB), several boards per PC, identified by board id rather than USB order.
- **Simulator** mode: everything works with no boards attached.
- Not supported yet: other Ultimarc boards (PacDrive, Ultimate I/O, I-PAC LED), LEDWiz, and other makers' boards.

**Panel and wiring**
- A visual panel designer: buttons, joysticks, trackball, coin and start, with a photo of your panel as a background.
- A node-graph wiring editor and a wiring wizard, which lights each pin in turn so you can find its button.
- A Boards view with a live grid for every pin, plus test patterns.

**Game lighting**
- Imports LEDBlinky's data (controls, colours and input map) and lights each game the way LEDBlinky would. It
  matched LEDBlinky on the real cabinet. It only reads LEDBlinky's files; it never changes them.
- Light inheritance: game → parent → platform → MAME default.
- Per-game and per-platform colour overrides, templates, bulk copy, and saved smart lists.
- **Each emulator's own key bindings.** When a game's buttons are remapped in the emulator, the lights follow:
  - **MAME:** each install's `ctrlr` file, `default.cfg` and per-game `cfg` (for cabinets with several MAME installs);
  - **TeknoParrot:** each game's user profile (DirectInput and RawInput keyboard bindings).

**Front ends**
- **LaunchBox / BigBox:**
  - LED-Next reads its game library; it only reads it and never changes it.
  - A plugin lights the panel as you browse games, platforms and the BigBox wheel, while a game plays, and when you exit.
  - The plugin starts LED-Next in the system tray.
- LaunchBox pictures ("Arcade - Control Panel", "Arcade - Controls Information") and button-layout banners in
  the Games tab.

**Live lamps from emulators**
- **MAME**, over network output (`output network`) or Windows messages (`output windows`). Lamps are mapped
  through LEDBlinky's `MameOutputs.ini`; for example, the Start lamp blinks while Galaga waits for a coin.
- **Flycast** network output: it can be read, but so far it has only been tested against a simulated Flycast.
- Not supported: Supermodel, and TeknoParrot's own output system.

**Not yet:** other front ends (only LaunchBox/BigBox), LEDBlinky animations (`.lwax`), BigBox playlists, and
lighting by front-end section.

**Needs:** Windows 10 or 11, the .NET 8 Desktop Runtime (or the self-contained build), and, for PacLED64 boards,
Ultimarc's `PacDrive64.dll`, which you supply yourself.

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
