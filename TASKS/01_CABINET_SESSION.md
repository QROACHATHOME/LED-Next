# Cabinet session brief (for Claude on the arcade machine)

## Visit 12 (next visit) — lamp maps per emulator, spoken button labels
Emulators now name themselves when a game starts ("MAME", "Flycast"), and each has its own output map:
LED-Next's own table (`cabinet.json` `lamps.maps`, new in config version 7), then for MAME LEDBlinky's
MameOutputs.ini as before. Checked with a fake MAME and a fake Flycast only.
1. `git pull`. Close LED-Next, LaunchBox and BigBox. Ask the user, then run `Install LED-Next.cmd`.
   The log should say "Config upgraded from version 6 to 7".
2. In BigBox, launch **Galaga**. The log should say `galaga started (MAME, network port 8000); outputs mapped by
   C:\LEDBlinky\MameOutputs.ini [default]`, and P1 Start / button 1 blink as in visit 11. Nothing lit after exit.
3. Optional, with the user's OK (it edits LED-Next's own config only): close LED-Next, add to `lamps` in
   D:\LED-Next\config\cabinet.json
   `"maps": [ { "emulator": "MAME", "game": "galaga", "output": "led0", "controls": ["P1_BUTTON2"] } ]`,
   start it and launch Galaga: button 2 should now follow the Start lamp instead. Then remove the row.
4. If Flycast Race's Network Output has been switched on by then: launch Sega Marine Fishing; the log should say
   `started (Flycast, …)` and list its outputs at the end. Write them down for step 3 (a Flycast row).
5. **Spoken button labels** (new Speech tab, config version 8, off by default). In the Speech tab click "Copy
   LEDBlinky's speech settings" (it reads LEDBlinky's settings.ini, read only; the cabinet's has speech on, voice
   Microsoft David). Check the voice list shows that voice. Click Save.
   - In the Games tab select Galaga, then Speech tab → "Test with the game shown": "Galaga, Fire" from the cabinet
     speakers, P1 button 1 blinking while "Fire" is said.
   - In BigBox launch **1942** and **Street Fighter Alpha 2** (labelled 6-button game): the labels are said about
     1 s after the launch, each button blinking, over the game's start. Log: `Speech (launch): …` then `Speech: done`.
   - Launch again and press a button or move the joystick while it talks: it must stop at once
     (`Speech: stopped (… was pressed)`). If it stops by itself straight away, the log names the device; write it down.
   - In MAME press P (pause): the labels are said again; unpause stops it.
   - Ask the user whether the delay, speed and blink feel right; record their choices.
6. Add a "Visit 12" section to `CABINET_RESULTS.md`. Commit, ask before pushing.

---

## Visit 11 — the lamp lifecycle (done, see CABINET_RESULTS)
The front end and the emulator lamps now go through one state machine (`LampLifecycle`, ARCHITECTURE "Live lamps
from emulators"). The log has a line for every change, e.g. `Lamps: Launching → Playing (…)`. It was checked
with a fake MAME and a fake BigBox only.
1. `git pull`. Close LED-Next, LaunchBox and BigBox. Install from source: `Install LED-Next.cmd` (ask the user
   first). The log in D:\LED-Next\logs is the one to read.
2. In BigBox, launch **Galaga** (Mame02XX, network output) and insert a coin. The log should go Browsing →
   Launching → Playing → PlayingWithLamps, and P1 button 1 should blink with the Start lamp as in visit 7. Exit:
   "galaga ended (…)", then Browsing, with the selection lit and nothing left blinking.
3. **Exit with the game still blinking:** launch Dig Dug, coin up so Start blinks, then quit MAME at once. Any
   lamp that was on must be off afterwards.
4. **Windows messages:** launch Chase H.Q. (output windows). Same timeline; its outputs are listed at the end.
5. While a game runs, scroll in BigBox if it lets you: the panel must stay on the game.
6. Add a "Visit 11" section to `CABINET_RESULTS.md` with the `Lamps:` lines of one game. Commit, ask before
   pushing.

---

## Visit 10 (next visit) — LaunchBox plugin for the first release
LED-Next now has GitHub releases (README "Making a release"). GitHub builds the two LED-Next zips. It can't
build the plugin, because that needs LaunchBox's own DLL.
1. `git pull`, then `git checkout v0.1.0` (the release's tag) so the plugin matches the release.
2. Run `powershell -ExecutionPolicy Bypass -File tools\build-plugin-release.ps1`. It only builds, and changes
   nothing in LaunchBox. Record its output. It writes `dist\LED-Next-LaunchBox-plugin-0.1.0.zip`.
3. Test the release as a user would. Ask the user first, because it replaces the installed exe and plugin.
   - Download `LED-Next-0.1.0-win-x64.zip` from https://github.com/QROACHATHOME/LED-Next/releases/tag/v0.1.0
     and unzip it to a new folder. Unzip the plugin zip into that folder.
   - Close LED-Next, LaunchBox and BigBox, then run `Install LED-Next.cmd`.
   - It should say "from this release folder", keep D:\LED-Next\config, and install the plugin from the
     "LaunchBox plugin" folder.
   - Start BigBox and check that the lights follow the selection.
4. Attach the plugin zip to the release. The user can do it, or you can with the browser if it's signed in
   to GitHub: Edit release, then drag in the zip. Ask the user first, because this publishes it.
5. `git checkout main`. Add a "Visit 10" section to `CABINET_RESULTS.md`; commit, ask before pushing.

---

## Visit 9 (next visit) — install to D:\LED-Next
LED-Next now has an installer (`Install LED-Next.cmd` → `tools\install.ps1`, README "Install"). It publishes
one `LED-Next.exe` to D:\LED-Next, copies config\ and PacDrive64.dll from the Debug build folder the cabinet
has been running, adds a Start menu shortcut and re-installs the plugin pointed at the new exe. Tested on the
dev PC with a fake old install and without LaunchBox, so the plugin step and hardware mode are untested.
1. `git pull`. Close LED-Next, LaunchBox and BigBox. Ask the user, then run `Install LED-Next.cmd`. Record its
   output. D:\LaunchBox\Plugins\LED-Next\LedNext.LaunchBox.json should now name D:\LED-Next\LED-Next.exe.
2. Start LaunchBox: it should start the installed LED-Next (log at D:\LED-Next\logs\led-next.log, config path
   D:\LED-Next\config\cabinet.json). Check Hardware mode lights the boards, the Games tab has its games, and
   live lighting follows the selection. Start Galaga: lamps as in visit 7.
3. If anything fails, the old Debug build still has its own config and works as before. Note it and leave the
   user on whichever works.
4. Flycast (from visit 8), only if the user has checked Flycast Race's Network Output setting: retry Sega
   Marine Fishing.
5. Add a "Visit 9" section to `CABINET_RESULTS.md`; commit, ask before pushing.

---

## Visit 8 (next visit) — Flycast lamps again
Flycast's network output turned out to differ from MAME's (`game = id`, hex values, no stop message); LED-Next
now reads it. Checked only with a fake Flycast.
1. `git pull`, build, test.
2. LED-Next running (Hardware mode). In LaunchBox start **Sega Marine Fishing** (its emu.cfg section has
   `NetworkOutput = yes`). The log should show "Lamps: Flycast on network port 8000 reports game '…'" and then
   "reports <output>" lines. Play a little, exit; the log lists every output it reported.
3. Note the game id Flycast reports and the output names. They are not in MameOutputs.ini yet, so nothing on the
   panel changes unless the user wants a mapping: a `[<game id>]` section in MameOutputs.ini is LEDBlinky's file,
   so ask before editing it (or note the mapping for the dev PC).
4. Add a "Visit 8" section to `CABINET_RESULTS.md`; commit, ask before pushing.

---

## Visit 7 (next visit) — live lamps from emulators
LED-Next now listens for emulators' lamp outputs (ARCHITECTURE "Live lamps from emulators"). Built from MAME's
source and checked only with a fake MAME on the dev PC. Close LEDBlinky first if it runs (it may listen too).
1. `git pull`, build, test. Config upgrades to version 6 (logged). The log at start shows "Lamps: listening for
   MAME's Windows output messages" and "watching for emulator network output on port 8000".
2. **mame.ini check (read only):** list the `output` line of every MAME install's mame.ini (Mame02XX, mame0251, the
   others under D:\CoinOPS Collections\emulators, E:\Emulators\mamePC, F:\Collections\…). `auto` (the default)
   sends **nothing**; lamps need `output windows`. Record the list in the results. Changing a mame.ini is the
   user's call: ask first, back the file up, change only that one line.
3. **MAME:** Hardware mode on. In LaunchBox start **Galaga** (Mame02XX) and insert a coin. The log should show
   "Lamps: galaga started (Windows messages)" and "reports led0 → P1_START"; P1 button 1 should blink with the
   Start lamp, then light normally once the game starts. Ask the user what the panel does. Exit: the log lists
   all outputs; lights go back to the selection. Try 2-3 more (Dig Dug, a driving game with lamps, Terminator 2).
4. **Supermodel** (if installed): needs `-outputs=win` (check its ini / LaunchBox command line, read only). Same check.
5. **TeknoParrot:** look for Output Blaster in the TeknoParrot folder and the game profile's "Enable Outputs"
   setting (read only). If present, try a game it supports (Outrun 2 SP); note what the log shows (the rom name
   Output Blaster reports is not a MAME name, so the base lights stay the front end's).
6. **Flycast:** check for standalone Flycast and its "Network Output" setting (emu.cfg, read only). Try a Naomi
   game; the log should show "started (network port 8000)". Note any freeze (reported on some Naomi games).
7. If something doesn't connect: note what the log shows and whether LEDBlinky's MAMEOutputTest sees outputs.
8. Add a "Visit 7" section to `CABINET_RESULTS.md` (the mame.ini list, each emulator's result, the outputs games
   reported); commit, ask before pushing.

---

## Visit 6 (next visit) — timing, platform wheel, early connect
1. `git pull`, build, test. Close LaunchBox, BigBox and LED-Next. Re-install the plugin (ask the user first):
   `powershell -ExecutionPolicy Bypass -File tools\install-launchbox-plugin.ps1`.
2. Start **LaunchBox**. The plugin log should show "Connected to LED-Next." a few seconds after LED-Next
   starts, **before** any scrolling, and LED-Next's log "Front end: LaunchBox started".
3. Scroll through games in LaunchBox, slowly and fast. LED-Next's log has a line per change:
   `Timing '<game>': pipe … ms, wait … ms, lookup … ms, boards … ms; total … ms`. Ask the user if the lag
   is still there. If the totals are small (under ~30 ms) the lag is inside LaunchBox (the event comes late);
   note it. If one part is large, record which.
4. Start **BigBox**. On the **platform wheel** the panel should light each highlighted platform's default
   (LED-Next log: "platform '<name>' → …"). Ask the user if it looks right for a few platforms (Arcade,
   Neo Geo, a console). Playlists keep the last lights. Open a game list: lights follow games; going back to
   the wheel shows the platform again. Watch for a flicker to the platform between games (would mean
   BigBox briefly reports no selected game).
5. Add a "Visit 6" section to `CABINET_RESULTS.md` (timing lines, what the user saw); commit, ask before
   pushing.

---

## Visit 5 (next visit) — live lighting from LaunchBox / BigBox
The plugin (`src/LedNext.LaunchBox`) was built on the dev PC against `tools/LaunchBoxPluginStubs`, a
stand-in for LaunchBox's API written from its plugin docs. This visit builds it against the real file.
1. `git pull`, build, test. Close LaunchBox, BigBox and LED-Next.
2. **List the real plugin API** (read only), so mismatches with the stand-in are known up front:
   ```powershell
   $a = [Reflection.Assembly]::LoadFrom("D:\LaunchBox\Core\Unbroken.LaunchBox.Plugins.dll")
   ```
   PowerShell 5.1 may not load a .NET 10 assembly; if so, use a tiny `dotnet` console project with
   `System.Reflection.Metadata` or `MetadataLoadContext`. Record in the results: the members of
   `ISystemEventsPlugin`, `IGameLaunchingPlugin`, `IBigBoxThemeElementPlugin`, `PluginHelper`,
   `IStateManager`, `SystemEventTypes`, and **any interface with "Selection" in its name** (a better
   selection hook than polling, if LaunchBox 14.1 has one).
3. Ask the user, then run `powershell -ExecutionPolicy Bypass -File toolsinstall-launchbox-plugin.ps1`.
   It builds against `D:\LaunchBox\Core\Unbroken.LaunchBox.Plugins.dll` and writes **only** to
   `D:\LaunchBox\Plugins\LED-Next\`. Build errors about LaunchBox types = the stand-in differs: fix the
   plugin and `tools/LaunchBoxPluginStubs/Stubs.cs` to match the real API, rebuild, re-run.
4. LED-Next: Hardware mode on, then close it. Start **LaunchBox**: LED-Next should start minimized
   (plugin log `D:\LaunchBox\Plugins\LED-Next\LedNext.LaunchBox.log`; LED-Next's log shows
   "Front end connected"). Scroll through games: the panel should follow the selection. Launch a game
   (lights stay on that game), exit (back to the selection), close LaunchBox (all off).
5. Same in **BigBox**. If selection doesn't follow (log: "Selection watcher stopped", or no "select"
   in LED-Next's log), add the theme element to the user's BigBox theme — only with the user's OK, and
   back the view up first (see `LedNextThemeElement.cs` for the two XAML lines).
6. Note how quickly the panel follows when scrolling fast, and whether anything feels slow in LaunchBox.
7. Add a "Visit 5" section to `CABINET_RESULTS.md` (API listing, what worked, fixes); commit, ask before
   pushing. Don't commit LaunchBox's DLLs.

---

## Visit 4 (next visit) — visit-3 fixes, the fighter template
1. `git pull`, build, run. The game library upgrades to schema 5 (logged). The window should open fully on
   screen (position now kept in `config/window.json`); move it, restart, check it comes back there.
2. Games tab: the app reads LaunchBox at start-up. Check the log for "Platforms Cave, ZiNc … now use
   MAME's game data" and "… LaunchBox games took their button count … from MAME's data". In the
   platform list, Cave and ZiNc should now show lit games. `Laser Disc / Inter Stellar Zangus` should be
   listed under its title, not as `mame`.
3. Smart list **6-button fighting games**: note the count. Pick a fighter the user knows (Street Fighter II,
   Marvel vs. Capcom…), Templates tab → **6-button fighter** → **Try on selected game**, Hardware mode on.
   Ask the user: are kicks (buttons 4-6) the bottom row on the real panel? Nothing is saved by trying.
4. Add a "Visit 4" section to `CABINET_RESULTS.md`; commit, ask before pushing.

---

## Visit 3 (next visit) — LaunchBox data, templates, editing
1. `git pull`, build, run (config upgrades to version 3, game library to schema 4; both logged).
2. Games tab → **Import from LEDBlinky…** → `C:\LEDBlinky` (fills the button/control filters).
3. **Read LaunchBox** → `D:\LaunchBox`. Check the LaunchBox tab: does each LaunchBox platform map to the
   right LED-Next platform? Fix any that don't (that's saved) and note them in the results.
4. Smart list **New in LaunchBox, needs setup (30 days)**: ask the user if the list matches games they
   added recently. Note any game that is missing or matched to the wrong game.
5. Smart list **6-button games** → Templates → **6-button fighter** → Apply to all matches. Hardware
   mode on; preview a fighter: kicks (buttons 4-6) should be the bottom row. Then preview a trackball
   game and a Neo Geo game (not covered in visit 2). Edit one game live and check the panel follows.
6. For phase 4b (the plugin), record in the results: the file version of
   `D:\LaunchBox\Core\Unbroken.LaunchBox.Plugins.dll`, and the .NET version in
   `D:\LaunchBox\Core\*.runtimeconfig.json` (if present). Don't copy LaunchBox's DLLs into the repo.
7. Copy LaunchBox samples to `import-samples/launchbox/` as its README says.
8. Add a "Visit 3" section to `CABINET_RESULTS.md`; commit, ask before pushing.

---

## Visit 2 (next visit) — check the game import
Visit 1 is done (results in `TASKS/CABINET_RESULTS.md`; the driver works). For visit 2:
1. `git pull`, build, run. The config upgrades itself to version 2 on start (logged).
2. Games tab → **Import from LEDBlinky…** → `C:\LEDBlinky`. Read the Import report tab: it should
   say MAME's `cfg/default.cfg` was found (`D:\CoinOPS Collections\emulators\Mame02XX`).
3. Hardware mode on. Preview a handful of games the user knows (a 2-button, a 6-button, a
   trackball game, a 4-player game, a Neo Geo game) and ask the user whether the real panel
   matches what LEDBlinky lights. Note especially whether P3 button 3 lighting green
   (`UI_SELECT`, it sends Enter) matches LEDBlinky.
4. Copy MAME's `cfg/default.cfg` to `import-samples/mame/default.cfg` (read-only copy).
5. Add a "Visit 2" section to `CABINET_RESULTS.md` with what matched and what didn't; commit,
   ask before pushing.

The rest of this file is the visit 1 brief, kept for reference.

---

You are continuing LED-Next on the **arcade machine**. Development so far happened on another PC
with **no LED hardware**, in simulator mode only. Your job in this session is to get the app working
against the real boards, capture the real panel layout and wiring, and record everything in
`TASKS/CABINET_RESULTS.md` so the development PC can pick it up.

Read `CLAUDE.md`, `ARCHITECTURE.md` and `TASKS/00_ACTIVE_PRIORITIES.md` first.

## The cabinet
- 4-player control panel, RGB LED buttons, lit joysticks and trackball (some may turn out unlit)
- Two **Ultimarc PacLED64** boards (expected keys `PacLED64#1`, `PacLED64#2`)
- LEDBlinky and LaunchBox/BigBox are installed here. **Do not modify LEDBlinky's files.** Close
  LEDBlinky (and anything else that drives the boards) before testing, or the two will fight.

## What is verified vs. not
| Area | Status |
|---|---|
| Core model, wiring rules, engine, test patterns, wizard logic | Unit-tested (31 tests) |
| WPF app: panel, wiring graph, boards view, wizard, undo, photo | Driven and screenshot-checked in simulator mode |
| PacLED64 driver (`src/LedNext.Hardware/Ultimarc/`) | **Never run.** P/Invoke signatures written from memory of the PacDrive SDK |

Known risks in the driver, in order of likelihood:
1. **Port indexing**: `Pac64SetLEDIntensity(id, port, …)` is assumed 0-based (0–63). If pin 1 in the
   Boards tab lights the board's LED 2 (or nothing, and pin 2 lights LED 1), fix the index in
   `PacLed64Board.Write`.
2. **`Pac64SetLEDIntensities` missing** in the SDK build → the driver logs a warning and falls
   back to per-pin writes. Check this isn't visibly slow during Rainbow.
3. **Board ID detection** uses PID `0x1401`–`0x1404` → board ID 1–4. If the log lists the devices
   but neither is recognised, compare the logged VID/PID with this assumption.
4. **Both boards with the same ID** → only one is used (warning in the log). Fix with Ultimarc's
   configuration utility, or add `Pac64SetDeviceId` support.
5. **DLL bitness**: the app is x64 and needs `PacDrive64.dll`. A `BadImageFormatException` in
   the log means the wrong DLL.

Keep changes to the driver minimal and logged. Add unit tests for anything testable without hardware.

## Setup
1. Install the **.NET 8 SDK** if `dotnet --list-sdks` shows nothing 8.x.
2. Get Ultimarc's **PacDrive SDK** and copy `PacDrive64.dll` to `src/LedNext.App/native/`
   (it is git-ignored; the build copies it next to the exe).
3. `dotnet build LedNext.sln` then `dotnet test`.
4. Run `src/LedNext.App/bin/Debug/net8.0-windows/LED-Next.exe`. It creates
   `config/cabinet.json` (the default 4-player layout) and `logs/led-next.log` next to the exe.

## Session plan
Follow "At the arcade machine — suggested order" in `TASKS/00_ACTIVE_PRIORITIES.md`:
1. Boards talking: Hardware mode, Rescan, Boards tab pin clicks, check the log.
2. Real panel layout: photo, drag controls, add/delete to match. Delete unlit controls.
3. Wiring wizard for all pins; confirm with RGB cycle and Chase.
4. Exit and confirm all LEDs go off.

The user drives the app; you read the log, fix code, rebuild. Ask the user what they see on the
panel — you cannot see the LEDs.

## Before you finish — bring the results back
1. Fill in `TASKS/CABINET_RESULTS.md` (every section; write "not tested" rather than leaving gaps).
2. Copy the real config into the repo so the dev PC gets the actual layout and wiring:
   - `…/bin/Debug/net8.0-windows/config/cabinet.json` → `cabinet/cabinet.json`
   - `…/config/panel-background.*` → `cabinet/` (if a photo was set)
   - `…/logs/led-next.log` → `cabinet/led-next.log` (trim to the relevant session if huge)
3. Copy LEDBlinky's data files (read-only copies) into `import-samples/ledblinky/` — see
   `import-samples/README.md` for which files.
4. Note the LaunchBox version in the results file.
5. Commit with a clear message and `git push` (ask the user before pushing).
