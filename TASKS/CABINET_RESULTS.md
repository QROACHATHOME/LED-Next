# Cabinet results

Filled in on the arcade machine. Read by the development PC afterwards.

**Date:** 2026-11-10
**Machine notes** (Windows version, .NET SDK version, LaunchBox version):
Windows 11 Home 10.0.22000 · .NET SDK 8.0.427 (installed this session with winget) · LaunchBox / BigBox 14.1.0 (`D:\LaunchBox`) ·
single 1920×1080 display. Git for Windows 2.56 installed this session (it was not on the machine before).

## 1. Boards
- PacDrive SDK version / where the DLL came from: `PacDrive64.dll`, 77,312 bytes, file date 2020-01-10, x64, copied in by
  the user from Ultimarc's PacDrive SDK. (The machine also has LEDBlinky's 32-bit `PacDrive32.dll` from 2020 and two
  old 2010 `PacDrive.dll` copies under mamehooker; none of those were used.) Exports include everything the
  driver calls, plus `Pac64SetDeviceId`, `Pac64SetLEDStates`, `Pac64SetLEDFlashSpeed(s)` and the script functions.
- Log lines from discovery (VID/PID/type/version per device):
  ```
  PacDrive SDK reports 2 Ultimarc device(s).
    device 0: VID D209 PID 1401 type 5 version 25
    device 1: VID D209 PID 1402 type 5 version 25
  PacLED64: 2 board(s): PacLED64#1, PacLED64#2
  ```
- Keys detected (`PacLED64#1`, `PacLED64#2`?): yes, both, with unique IDs. The PID → board ID assumption (0x1401 = ID 1) is correct.
- Port indexing correct? (pin 1 in the Boards tab = LED 1 on the board?): **yes**, 0-based at the native call is right. No change needed.
- `Pac64SetLEDIntensities` available, or per-pin fallback?: available; no fallback warning in the log.
- Performance during Rainbow (smooth / stutters): smooth.
- LEDs off on exit?: yes, all off.

## 2. Driver changes made
- `src/LedNext.Hardware/Ultimarc/PacDriveNative.cs`: the DLL import resolver now caches the loaded handle. It is called
  once per imported function, so before this the log repeated `Loaded …PacDrive64.dll` for every function. Log noise only;
  no behaviour change for the boards.
- No changes to signatures, port indexing or board detection: all were correct on real hardware.

## 3. Panel layout
- Number of controls per player (buttons / start / coin / joystick):
  P1 8 buttons + Start + Coin · P2 8 buttons + Start + Coin · P3 4 buttons + Start + Coin · P4 4 buttons + Start + Coin.
  33 controls in total in `cabinet.json`.
- Trackball lit? Spinner? Admin buttons? Anything else lit: trackball is lit (RGB). No spinner, no admin buttons.
- Controls deleted from the default (unlit): `P1_STICK`…`P4_STICK` (joysticks have no LEDs).
- Start and Coin are **not RGB/LED buttons yet**. They stay in the layout on purpose (the user plans to fit RGB buttons
  later) but have no wiring, so they show as "not fully wired". That's expected, not a fault.
- Photo used? (copied to `cabinet/`): no photo set.

## 4. Wiring
- Wizard completed for all pins?: yes, all 128 pins on both boards visited. 25 controls fully wired (75 pins).
- Channel order learned (R,G,B / G,R,B / …): **R, G, B** on three consecutive pins for every control.
- Pins used per board:
  | Board | Pins | Controls (in pin order) |
  |---|---|---|
  | PacLED64#1 | 1–12 | P3_B3, P3_B4, P3_B1, P3_B2 |
  | PacLED64#1 | 13–36 | P1_B1, P1_B8, P1_B4, P1_B5, P1_B6, P1_B3, P1_B7, P1_B2 |
  | PacLED64#2 | 1–3 | TRACKBALL |
  | PacLED64#2 | 4–12 | P2_B7, P2_B1, P2_B2 |
  | PacLED64#2 | 31–42 | P4_B4, P4_B3, P4_B2, P4_B1 |
  | PacLED64#2 | 46–60 | P2_B3, P2_B8, P2_B4, P2_B5, P2_B6 |
- Pins skipped / unused: #1 pins 37–64 · #2 pins 13–30, 43–45, 61–64. (#2 43–45 is a gap between used pins: a spare
  connector or a dead LED; not investigated.)
- RGB cycle result (any buttons showing wrong colours?): all correct.
- Chase result: correct.

## 5. Problems and app bugs
- **Fixed: mouse clicks ignored on Panel tab buttons and checkboxes** (Start wiring wizard, Add, Duplicate, Delete,
  Identify, Set photo…, the "Start from empty wiring" checkbox). `PanelView.OnPreviewMouseDown` moved keyboard focus to
  the view on mouse-down; a WPF `ButtonBase` cancels its click when it loses keyboard focus while pressed. Keyboard
  shortcuts and the wizard's own buttons (already `Focusable=False`) were unaffected, which is why it went unnoticed in
  the simulator. Fix: for clicks inside a `ButtonBase`, focus is taken back on `PreviewMouseLeftButtonUp`, after the
  click. Verified at the cabinet (Identify works with the mouse). Not unit-testable (WPF input).
- **Open: the window can open partly off-screen.** It was found at x 919–2359 on a 1920-wide screen, hiding the Panel tab's
  right column. `MainWindow` uses `CenterScreen` at 1440×860, so it was probably dragged. Suggest saving/restoring the
  window position and clamping it to the work area at startup.
- The default ComboBox light style and the single-selection limit were noticed but caused no problems.

## 6. LEDBlinky and LaunchBox (for phase 3)
- LEDBlinky install path and version: `C:\LEDBlinky`, LEDBlinky.exe file version 8.3.0.0.
- Files copied to `import-samples/ledblinky/` (read-only copies; nothing in `C:\LEDBlinky` was changed):
  `LEDBlinkyControls.xml` (2.1 MB), `LEDBlinkyInputMap.xml`, `LEDBlinkyMinimizedMame.xml` (8 MB), `Colors.ini`,
  `Color-RGB.ini`, `Controls.ini`, `settings.ini`, `LEDBlinkyConfigWizard.ini`, `LEDBlinkyAnimationEditor.ini`,
  `MameOutputs.ini`, `NewInputCodes.ini`, `pixelcade.xml`, and 5 of the 78 animations in `lwa/`
  (`Pattern36/46/47/48/49.lwax`). Not copied: `License.txt`, `Readme.txt`, `settings-old.ini`, the `hi2txt/` and `jdr/` folders.
- LaunchBox / BigBox version: 14.1.0 (`D:\LaunchBox\Core\LaunchBox.exe`).

## 7. Files brought back
- [x] `cabinet/cabinet.json`
- [ ] `cabinet/panel-background.*` (no photo set)
- [x] `cabinet/led-next.log` (whole log, 360 lines)
- [x] `import-samples/ledblinky/…`

---

# Visit 2: game import (2026-11-10)

## Setup
- Pulled `4387a02` (Phase 3). Build clean. `cabinet.json` upgraded 1 → 2 on start (logged; written to disk on the next save).
- Games tab → Import from LEDBlinky → `C:\LEDBlinky`: 37,891 games in `config/games.db`. MAME's `cfg/default.cfg`
  was found through `settings.ini` (`MameFolder=D:\CoinOPS Collections\emulators\Mame02XX`).
- `default.cfg` copied (read-only) to `import-samples/mame/cfg/default.cfg`, keeping MAME's own `cfg/` layout so the folder
  can be passed straight to the importer.

## What we found
- **The cabinet's `default.cfg` swaps MAME's P1 buttons 7 and 8**: `P1_BUTTON7 = KEYCODE_V`, `P1_BUTTON8 = KEYCODE_C`
  (MAME's defaults are the other way round). With the real file, MAME's button 7 is the button labelled 7.
- **Test was machine-dependent**: `ControlMapFollowsTheCabinetsInputMap` passed on the dev PC (no MAME folder there, so
  MAME's built-in keys) and failed here (the real `default.cfg` was found). Fix: `LedBlinkyImporter.Import` takes an
  optional `mameFolder` (null = settings.ini, as before; the app is unchanged). The tests now pass it explicitly: a
  folder without `default.cfg` for the built-in keys, and `import-samples/mame` for the real file (new test
  `CabinetMameCfgReplacesMameDefaults`).
- **UI_SELECT / P3 button 3, decided: LEDBlinky's behaviour was a bug.** Every MAME platform default in
  `LEDBlinkyControls.xml` has `UI_SELECT alwaysActive="1" color="Green"`. P3 button 3 sends `KEYCODE_ENTER`, which
  is both MAME's `P3_BUTTON3` and `UI_SELECT`, so LEDBlinky (and our first import) lit P3 button 3 green in every
  game. Fix in `GameLightResolver`: always-active controls with no player (`UI_SELECT`, `UI_CANCEL`, `UI_PAUSE`…)
  light only shared (player 0) buttons; a player's button follows the game. Always-active *player* controls are
  unchanged. **LED behaviour change**: P3 button 3 now lights only in games that use it, in the game's colour.
  Confirmed at the panel.

## Game previews (Hardware mode)
- 34 games previewed by the user, including 10-Yard Fight, 1000 Miglia, 1945k III, Act Raiser, Alien Syndrome,
  Alien vs. Predator (+ `avspu`), Altered Beast, APB, Arabian Magic, Arkanoid bootlegs, Arrow Flash, Astro Fantasia,
  Astro Wars, B-Wings, Bass, Battle Circuit.
- Result: **all matched what LEDBlinky lights**, apart from the P3 button 3 fix above.
- Not specifically covered: a trackball game and a Neo Geo game (the unit tests cover `centiped` and `mslug`).

## Files brought back
- [x] `import-samples/mame/cfg/default.cfg`

---

# Visit 3: LaunchBox data, templates, editing (2026-11-10)

## Setup
- Pulled `c46e0c3` (Phase 4a). Build clean, 91 tests pass. Hardware mode, both boards found as before.
- Config upgraded 2 → 3 (logged). Game library: the log says "upgraded to schema 3", but `meta.schema_version` is
  written as 4 (and `GameDatabase.SchemaVersion` is still the constant 2). **Log wording only**; worth tidying.
- Re-imported from `C:\LEDBlinky`: 37,891 games on 173 platforms, 6,274 with their own or a parent's lights, 59 MAME
  keys from the real `default.cfg`. One warning: unknown colour `Whie` (a typo in LEDBlinky's data, `AAE/demon`).
- Read LaunchBox `D:\LaunchBox`: **41,633 games** on 109 LaunchBox platforms; 37,367 added as new library games.

## Platform mapping
All mappings looked right; nothing was changed by hand. Notable:
| LaunchBox platform | LED-Next platform | How | Games | Matched |
|---|---|---|---|---|
| Arcade | MAME | alias | 3,407 | 3,391 rom name, 1 title, 15 new |
| Arcade Clones | ARCADE_CLONES (uses MAME data) | same name | 7,242 | 1 rom name, 7,241 new |
| SNK Neo Geo MVS | SNK_Neo_Geo_MVS (uses MAME data) | same name | 146 | 1 rom name, 145 new |
| SNK Neo Geo AES | SNK_Neo_Geo_MVS | alias | 118 | |
| Sega Mega Drive | Sega_Genesis | alias | 522 | |
| Tekno Parrot | TEKNO_PARROT | same name | 722 | 153 rom name |
- "new" on the MAME-based platforms is expected: they find MAME's data by rom name in the resolver. In the library,
  ARCADE_CLONES has 3,856 of 7,206 games lit from MAME data (lighting 2), SNK_Neo_Geo_MVS 143 of 155.
- SNK Neo Geo AES runs in RetroArch here, not MAME, but the MVS lights fit the same games, so the alias is fine.
- About 60 LaunchBox platforms have no LEDBlinky platform (PS2/PS3, Switch, Xbox, Arcade PC, IGS PC, Cave, ZiNc,
  Sega Model 1, Naomi 2, Casino Arcade…) and are "new" with no lights until set up. **Cave and ZiNc are arcade
  platforms** (Cave runs FBNeo, ZiNc runs MAME): they could be mapped to a MAME-data platform by default.
- Quirks seen in the data:
  - `Laser Disc / Inter Stellar Zangus` got the game name `mame` (its LaunchBox entry probably points at the
    emulator, not a rom); the matcher should ignore an application path that is an emulator exe.
  - `Arcade / Wangan Midnight Maximum Tune 4` is a TeknoParrot game filed under Arcade (`rom` "Wangan Midnight
    Maximum Tune 4 (TeknoRaven WMMT4)"): user data, not an app bug.
- LaunchBox `DateAdded` range: 2025-03-31 to 2026-11-08.

## Smart list "New in LaunchBox, needs setup (30 days)"
- 57 games, newest first: Euro League, ATV Track (Gaelco G4), Smashing Drive ×2, WMMT4, Galaxian 3 ×2 (8 Nov), then
  TeknoParrot, IGS PC, Arcade PC, Arcade Duck, Laser Disc additions back to 11 Oct. Guardians of the 'Hood (added 28 Oct)
  is correctly absent because LEDBlinky already lights it.
- **The user confirmed the list matches the games they added recently.** None missing or wrongly matched.

## Templates, previews and live editing (Hardware mode)
- **6-button fighter template: previewed, not applied.** "6-button games" lists over 5,000 games and the user did
  not want to change that many. So "kicks (buttons 4-6) are the bottom row" is **not verified on the panel**.
  Suggest: let a template preview on the selected game's panel before applying, and make the 6-button list narrower
  (e.g. hide games already lit by LEDBlinky, or fighters only), so applying is a real choice.
- Trackball game: Golden Tee Golf II (Trackball) lit the trackball. Correct.
- Neo Geo: 3 Count Bout, Aggressors of Dark Kombat, Eight Man, Fatal Fury 2. Correct (4 buttons per player).
- Live edit: Fatal Fury 2, P1_BUTTON5 → Lime, then → off. The panel followed immediately. Correct.
  (Fatal Fury 2 stays "customized" in the cabinet's `games.db`; same lights as imported.)

## For phase 4b (the plugin)
- `D:\LaunchBox\Core\Unbroken.LaunchBox.Plugins.dll`: file version **14.1.0.5**, product version
  `14.1.0+2cca3715be24d6aedb0449d3a9f0c745f018322e`, 367,928 bytes. Not copied.
- `LaunchBox.runtimeconfig.json` and `BigBox.runtimeconfig.json`: **`net10.0`**, Microsoft.NETCore.App and
  Microsoft.WindowsDesktop.App **10.0.14**. LaunchBox runs on .NET 10, not .NET 8: an in-process plugin must target
  a framework .NET 10 can load (net8.0 class library loads fine; it cannot be built against `net10.0` APIs with the
  .NET 8 SDK installed here). Only .NET SDK 8.0.427 is installed on this machine.

## Files brought back
- [x] `import-samples/launchbox/Data/Platforms.xml` (1.4 MB) and `Data/Emulators.xml` (0.3 MB)
- [x] `import-samples/launchbox/Data/Platforms/SNK Neo Geo MVS.xml` (1.0 MB, runs MAME here) and
      `Data/Platforms/Sega Genesis.xml` (4.6 MB, console)
- `Arcade.xml` is 25.8 MB (and `Arcade Clones.xml` 48.5 MB): **not copied** (over the ~20 MB limit). Neo Geo MVS is
  the MAME-based sample instead. Checked the copies: no user paths, e-mail addresses or keys.
- [x] `cabinet/led-next.log` updated
