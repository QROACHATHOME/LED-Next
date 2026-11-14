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

---

# Visit 4: visit-3 fixes and the fighter template (2026-11-10)

## Setup
- Pulled `bf797c9`. Build clean, 98 tests pass (99 after the fix below). Hardware mode, both boards found.
- Game library upgraded schema 4 → 5 (logged, wording now right). LaunchBox read at start-up: 41,633 games.

## Window position
- First start (no `window.json`): opened at x 240–1680, y 110–970 on the 1920×1080 screen, fully visible.
- User moved/resized it to the top-left and closed the app: `window.json` saved `Left -4, Top 4, 1000×600`.
  Next start: restored at x 0, y 4, 1000×600, logged "Window was partly off-screen (x -4–996 …); moved to x 0–1000".
  **Correct.** (−4 is the usual invisible window border of a window snapped/dragged to the edge; nudging it by
  4 px is harmless.)

## Cave / ZiNc
- Log: "Platforms Cave, ZiNc now use MAME's game data: 34 of 63 games lit from it." **Correct.**
- "4,040 LaunchBox games took their button count and control types from MAME's data."

## Inter Stellar Zangus — not fixed by `bf797c9` alone; fixed here
- After the pull, `Inter Stellar Zangus` (and `Cave / Mushihime-Sama Matsuri`, `SNK Neo Geo MVS / Bang Bang
  Busters 2`, all launched with a `mame.exe`) still matched game `mame`, now **by title**. Cause: visit 3 had
  added library rows `(Laser_Disc, mame, "Inter Stellar Zangus")` etc. (sources `LaunchBox`), and `TitleIndex`
  included LaunchBox-added rows, so the new title fallback found the stale row.
- Fix (`src/LedNext.Data/GameDatabase.cs`):
  - `TitleIndex` only uses imported games (`sources <> 'LaunchBox'`).
  - New `RemoveStaleLaunchBoxGames` (first step of `MergeLaunchBoxGames`): deletes LaunchBox-added games no
    `launchbox_games` row points at, unless the user customized them. **Behaviour change:** games removed from
    LaunchBox now also leave LED-Next's list (customized ones stay).
  - Test `AnOldWronglyNamedLaunchBoxGameIsReplacedNotMatchedByTitle` (fails without the fix).
- At the cabinet: "Removed 8 games added from LaunchBox earlier…", "Added 8…"; all three now listed under their
  titles, no game named `mame` left.
- Also fixed: `FillFromMameData` matched `games.players = 0` even when MAME's row also has 0 players, so it
  rewrote 3,706 rows (and logged it) on every start. Now `(games.players = 0 AND m.players > 0)`. Next start was
  quiet (no fill/remove/add lines).

## Fighter template
- Smart list **6-button fighting games: 402 games** ("6-button games": 472; the "5,000+" seen in visit 3 was
  before button counts were filled properly).
- 6-button fighter on **Street Fighter Zero 2 Alpha** (`sfz2al`), Hardware mode: **kicks (buttons 4-6) are the
  bottom row** on the real panel for P1 and P2. Confirmed by the user.
- Note: the log shows "Pasted all lights of template 6-button fighter onto 1 games", so it was applied to the
  selected game (saved), not only tried. `sfz2al` and `fatfury2` (visit 3) are the cabinet's two customized games.

## Files brought back
- [x] `cabinet/led-next.log` updated

---

# Visit 5: live lighting from LaunchBox / BigBox (2026-11-10)

## Setup
- Pulled `9bdad71`. LED-Next built and 107 tests pass. Hardware mode, both boards found.
- **The plugin did not build against the real DLL at first:** `CS1705`, because `Unbroken.LaunchBox.Plugins.dll`
  14.1.0.5 references System.Runtime 10.0. Fix: installed the **.NET 10 SDK (10.0.404)** next to 8.0.427. Now
  `LedNext.LaunchBox.csproj` targets `net10.0-windows` when it finds the real DLL and `net8.0-windows` with the
  stand-in (on the dev PC, no .NET 10 needed). The install script reads `bin\Release\net10.0-windows`. The LED-Next
  app stays on .NET 8. There is no global.json, so `dotnet` now defaults to SDK 10 here; the app still builds and
  its tests still pass.

## Real plugin API (14.1.0.5, read by reflection)
- `ISystemEventsPlugin`, `IGameLaunchingPlugin`, `IBigBoxThemeElementPlugin`, `PluginHelper.StateManager`: names and
  members as the stand-in had them.
- `SystemEventTypes`, `FilterType` and `IStateManager` are in **`Unbroken.LaunchBox.Plugins.Data`**, not the root
  namespace.
- Nullable: `OnBeforeGameLaunching(IGame? game, IAdditionalApplication? app, IEmulator? emulator)`,
  `OnAfterGameLaunched(IGame game, IAdditionalApplication? app, IEmulator? emulator)`.
- `SystemEventTypes` has more than the docs list: BigBoxThemeChanged, BigBoxLocked/Unlocked, GameStarting,
  GameExited, **SelectionChanged**, MameGameSessionStarted/Ended, MameCoinInserted, MamePlayerStarted.
- `IStateManager`: IsBigBoxLocked, IsBigBoxInAttractMode, IsBigBox, IsPremium, BigBoxCurrentTheme,
  GetAllSelectedGames(), GetSelectedPlatform(). No interface named "Selection".
- `FilterType`: Genre, PlatformOrCategoryOrPlaylist, Developer, Publisher, Rating, PlayMode, Region, Series, Status,
  Source, MaxPlayers, RecentlyPlayed, Favorites, Local, Cloud, None.

## Plugin changes
- `tools/LaunchBoxPluginStubs/Stubs.cs` rewritten to match (namespaces, all event names, nullability).
- Plugin handles the **`SelectionChanged`** system event (reads `GetAllSelectedGames()`).
- `PluginHost.SendSelection`: the event, the 150 ms poll and the theme element share one send, de-duplicated by game
  id. The first selection from each source is logged ("First selection via …").
- Build against the real DLL: 0 warnings, 0 errors.

## Installed
- `tools\install-launchbox-plugin.ps1` (with the user's OK): only `D:\LaunchBox\Plugins\LED-Next\` (LedNext.LaunchBox
  and LedNext.FrontEnd dll/pdb, `LedNext.LaunchBox.json`). No LaunchBox DLL copied.

## LaunchBox — works
- LED-Next started minimized by the plugin, found both boards, "Front end connected".
- Panel followed the selection: 49 changes (Namco System 357, MAME, Cave, TeknoParrot, PC, …), **all matched by
  LaunchBox id**. Launch (Powerball) kept its lights (MAME default), exit went back to the selection, closing
  LaunchBox → all off.
- `SelectionChanged` fires first; the poll saw the same game ~125 ms later and sent nothing.
- User: **slight lag, no slowdown in LaunchBox.** Plugin → LED-Next is ~15 ms in the logs, so the lag is either
  LaunchBox raising the event late or LED-Next between "select" and the boards (not timed in the log).

## BigBox — works, no theme change needed
- `SelectionChanged` and `GetAllSelectedGames()` work in BigBox too, so the theme element is **not needed**
  (theme not touched). 83 selection changes, all by LaunchBox id. Launch (Rim Rockin' Basketball, game `rimrockn`),
  exit back to the selection, closing BigBox → all off.
- User: **instant, no slowdown.** The lights changed about as fast as BigBox switched each game's preview video.
- While browsing **platforms / playlists** (before opening a game list) the panel was **off**; lights only came
  on once a list was opened and a game was selected. (No game is selected there, so nothing is sent.)

## Open
- LED-Next only connected on the first selection (~26–38 s after it started), so the plugin's `started` message
  was sent before LED-Next listened and was dropped. Harmless now (nothing depends on it), but the client could
  retry/connect when LED-Next comes up.
- The LaunchBox lag: add timing (select received → written to boards) to find where it is.

## Files brought back
- [x] `cabinet/led-next.log` updated

---

# Visit 6: timing, platform wheel, early connect (2026-11-10)

## Setup
- Pulled `a2c746f`. Build clean, 111 tests pass. Plugin re-installed (user's OK), same five files in
  `D:\LaunchBox\Plugins\LED-Next\`.

## Early connect — works
- LaunchBox: LED-Next listened at 16:57:11.6, the plugin connected at 16:57:12.8, **before any scrolling**, and
  LED-Next logged "Front end: LaunchBox started". BigBox: connected 1.4 s after LED-Next listened.
- In LaunchBox the plugin also sent the selected platform at start-up (Arcade → MAME default lit).

## Timing (LaunchBox, 59 changes) — the lag is board #1's USB connection
- `Timing` lines: pipe 0–1 ms, wait 0 ms, lookup 1–4 ms, **boards ~206 ms** on every change that altered the
  lights (0 ms when the lights didn't change). The first one had pipe 1,009 ms (the connect retry, once).
- User: **the lag felt gone** this time.
- Direct test with LED-Next closed (`tools/pacled64-timing.ps1`, PacDrive64.dll from the app's bin):

  | | PacLED64 #1 (PID 1401) | PacLED64 #2 (PID 1402) |
  |---|---|---|
  | `Pac64SetLEDIntensities`, 64 LEDs | **198 ms** | 9 ms |
  | `Pac64SetLEDIntensity`, one LED | **6.1 ms** | 0.3 ms |
  | 64 single-LED calls | 385 ms | 18 ms |

- Same firmware (version 25). USB topology: **#2** is on a Genesys hub (05E3:0610) directly under the root hub;
  **#1** is two hubs deep, on a 34D1:0609 hub shared with another Ultimarc device (PID 0512). The cabinet has
  seven other Ultimarc devices (PIDs 0430, 0460 ×2, 0511, 0512, 15A1, 15A2).
- Conclusion: the 200 ms is board #1's USB path, not LED-Next. Writing only changed LEDs would not help (6 ms per
  LED on #1). **Fix at the cabinet:** move #1's cable to a port on the PC or onto #2's hub, then re-run
  `pwsh -File tools\pacled64-timing.ps1 -Dll <app bin>\PacDrive64.dll` (LED-Next closed; it flashes the LEDs).
  The user chose not to move it this visit.

## BigBox platform wheel — works
- Platforms lit their default: IGS PC, Konami Bemani Python 1, Laser Disc (MAME default), Sammy Atomiswave,
  Sega Naomi (their own defaults), SNK Neo Geo MVS (MAME default). **User: looked right.**
- Most entries on this cabinet's wheel are **playlists** (All Platforms, Cinematronics, Gaelco, Comad, Irem M72,
  Namco System 1, Sega G80, SSV Sammy Seta Visco, Taito B System…; files in `Data\Playlists`): logged
  "no LED-Next platform for '…'; lights unchanged" (a Warn each time — noisy; could be Info). Games inside them
  light normally (e.g. SSV → Vasara 2 → ARCADE_CLONES/vasara2a).
- Game list (Neo Geo MVS, 20 games): lights followed, **no flicker** to the platform between games (user and log).
  Back to the wheel → platform lights again. Launch Samurai Shodown II (game samsho2, MAME data), exit back to
  it, closing BigBox → all off.

## Files brought back
- [x] `cabinet/led-next.log` updated
- [x] `tools/pacled64-timing.ps1` (the board timing test)

## Addendum: the Arcade category on the BigBox wheel (same day)
- Added a plugin log line for each wheel change with no game selected ("Wheel: platform:<name>", or "no game
  and no platform selected"). Re-installed (user's OK).
- On the **Arcade platform category**, `GetSelectedPlatform()` reports **"Arcade"**, so LED-Next lights it
  through the existing platform lookup: "platform 'Arcade' → MAME (LaunchBox games); MAME default".
  **User: lit, and it was the category.** No LED-Next change needed. (BigBox reports a category the same way as
  a platform of that name; the plugin can't tell them apart, and doesn't need to here.)
- Other entries on that wheel level: Taito B System, Taito Air System, Samurai Showdown Collection, American
  Laser, Atari (playlists, lights unchanged), Arcade Duck and Arcade PC (MAME default).
- Board #1 still on its slow hub: 206 ms when the lights change.

---

# Visit 6b: P3 button 3 and LEDBlinky colours under another emulator (2026-11-10)

## P3 button 3 still green — fixed
- The visit-2 rule (always-active UI controls such as `UI_SELECT` light only shared buttons) only ran for
  platform defaults *added to* a game's own lights. When the platform default itself decides the lights (a game
  with no lights of its own, e.g. Powerball → MAME default, and the platform wheel), `UI_SELECT` (Enter) still lit
  P3 button 3 green. Fix in `GameLightResolver`: the same rule applies to the deciding level when it is a platform
  default. Test `AlwaysActiveUiControlsDoNotLightPlayerButtonsFromAPlatformDefault` (failed before).
- Now 300 of 41,633 LaunchBox games light P3 button 3 (298 use P3_BUTTON3, 2 list UI_SELECT themselves).
  **User: P3 button 3 off in Powerball. LED behaviour change.**

## LEDBlinky colour edits "not showing" — games set up under another emulator
- LED-Next reads `LEDBlinkyControls.xml` correctly: for 1,103 of its 1,107 games LED-Next shows exactly its colours
  (the 4 others are listed twice in the file: quartet2play, rampart2p2play, rampart2pxbox, quartetxbox).
  `Colors.ini` is LEDBlinky's stock 2007 file; the user's edits are in `LEDBlinkyControls.xml` (saved 10/16).
- Cause: games whose colours are under a different emulator than their LaunchBox platform — TeknoParrot games under
  Sega_Model_2 / MAME, Arcade vector games under AAE, Arcade Clones under Model 2 / Model 3, Laser Disc under Daphne.
  LED-Next only looked under the LaunchBox platform and showed its default.
- Fix (`GameDatabase.UseLightsFromAnotherEmulator`, first step after a LaunchBox read or LEDBlinky import): a
  LaunchBox game whose own entry has no lights uses the same ROM name under another platform when that entry comes
  from `LEDBlinkyControls.xml`, has coloured lights, and its title matches (`GameMatcher.SameTitle`: ignores
  brackets, a leading "The", punctuation; "Star Wars: Battle Pod" ≠ "Star Wars"). Not for customized games, and
  never MAME for a platform that already uses MAME's data. `launchbox_games.matched_by` = "rom name on <platform>".
  New index `games(name)`. Test `AGameWithoutLightsUsesTheSameGameUnderAnotherEmulator`.
- At the cabinet: **80 games** re-pointed (TeknoParrot → MAME 17, → Sega_Model_2 14; Arcade → AAE 9; Arcade Clones →
  Model 2/3 12; Laser Disc → Daphne 3; …). A first, broader version moved 466 (incl. Arcade Clones / Neo Geo → MAME
  and consoles → MAME's general data); narrowed, and the next read restored them.
- ~15 are not matched because LEDBlinky's title differs ("Virtua Fighter 2.1", "Gun Blade New York"…).
  "Rampage" (Arcade Clones, Atari 800/ST) now uses the TeknoParrot "Rampage" entry (same title) — check if wrong.
- **User: Virtua Cop and Gauntlet Legends (Tekno Parrot) show their LEDBlinky colours.**
- 114 tests pass.

---

# Visit 6c: extra colors.ini (2026-11-10)
- No official update of `colors.ini` since LEDBlinky's 2007-11-12 file (the cabinet has that one); newer
  community versions are shared on the BYOAC and LaunchBox forums.
- New: **Games → "Extra colors.ini…"** (`GameSettings.ExtraColorsIni`, `cabinet.json` **version 4**, migration
  3 → 4). The file is read with every LEDBlinky import, after LEDBlinky's own files, and only lights MAME games
  LEDBlinky has no lights for (source "extra colors.ini"). Cancelling the dialog offers to remove it.
  Tests `AnExtraColorsIniOnlyLightsGamesLedBlinkyLeavesUnlit`, `AMissingExtraColorsIniIsAWarningNotAFailure`.
- First try: the button stayed greyed out (not refreshed when the start-up LaunchBox read finished); fixed.
- At the cabinet the user chose a downloaded `Colors.ini`: "23 MAME games lit from it; 1,399 already lit by
  LEDBlinky were left as they are". 116 tests pass.

---

# Visit 6d: minimize to the system tray (2026-11-10)
- New `src/LedNext.App/TrayIcon.cs` (Windows Forms `NotifyIcon`; `UseWindowsForms` on in the app project, with the
  global `System.Windows.Forms` / `System.Drawing` usings removed so WPF names stay unambiguous). Icon drawn in code.
- Minimizing hides the window to the tray (lights keep running, preview paused). Double-click / "Open LED-Next"
  restores it (maximized if it was); "Exit" closes it. `--minimized` (the LaunchBox plugin) starts in the tray.
  The window's ✕ still exits. No setting to keep it on the taskbar.
- **User: works.** 116 tests pass.

# Visit 6e: each MAME install's own keys (2026-11-10)

The cabinet has 33 MAME installs (D:\CoinOPS Collections\emulators\…, E:\Emulators\mamePC, F:\Collections\…);
LEDBlinky and the cabinet's control map read only `Mame02XX`. Comparing each install's `ctrlr` file + `cfg/default.cfg`
with the keys the panel sends (LEDBlinkyInputMap.xml):
- `Mame02XX` and `mame0251` (MAME (Clones)) match the panel exactly (~11,000 LaunchBox games).
- `Mame02XX Race` / `Mame02XX Flight` remap P1/P2: game button 1 = P1_B4, 2 = P1_B5, 3 = P1_B6, 6 = P1_B3, 7 = P1_B1,
  8 = P1_B2, 5 = the coin key (no panel button).
- Most others leave P3/P4 buttons 1–2 at MAME's built-in keys (Right Ctrl/Shift, keypad), which the panel doesn't send;
  buttons 7/8 swapped. 53 games in Mame02XX have their own cfg with key changes.

LED-Next now lights each game by the keys of the emulator LaunchBox runs it with (see 00_ACTIVE_PRIORITIES).
**Verified at the panel:** Downhill Bikers (Mame02XX Race) lights the right buttons.

# Visit 6f: TeknoParrot profiles' keys (2026-11-10)

TeknoParrot (`D:\CoinOPS Collections\emulators\Teknoparrot2024`, 639 user profiles) keeps each game's bindings in
`UserProfiles\<game>.xml`, which LaunchBox launches directly (758 games). The panel appears there as the
"Ultimarc I-PAC 4X" keyboard. Of 1,146 keyboard button bindings, 391 were on the same-named panel button, 451 on
another button, 304 on a key no panel button sends. LED-Next now lights TeknoParrot games from their profile's keys
(287 games change). **Verified at the panel:** Virtua Fighter 5 R (button 4 = P1_B7).

# Visit 7: live lamps from emulators (2026-11-11)

LED-Next started both listeners: MAME Windows messages and network output on port 8000.

**`output` in each MAME install's mame.ini:**
- `network`: `Mame02XX` and `ddr573-mame`.
- `windows`: `mame Current Race`.
- `auto`: the other 28, including `mame0251`, `Mame02XX Race`, `Mame02XX Flight`, `mame Current Spin`, `MameGun` and the AES install.
  `auto` sends nothing unless a plugin asks for outputs.

**At the panel:**
| Game | Emulator / path | Result |
|---|---|---|
| Galaga | Mame02XX, network | Connected. `led0 → P1_START`. **P1 button 1 blinked (user confirmed).** |
| Dig Dug | Mame02XX, network | The same: `led0 → P1_START`. |
| Chase H.Q. | mame Current Race, windows | Connected. Reported `genout0`–`genout3` and `Orientation(\.\DISPLAY7)`. None are in MameOutputs.ini, so nothing changed (as expected). |
| Terminator 2 | MameGun, auto | No lamps (as expected for `auto`). |
| Chase H.Q. 2 | TeknoParrot, profile "Enable Outputs"=1 | **No lamps.** Nothing connected and nothing lit in game. |
| Sega Marine Fishing | Sega Naomi (Flycast), per-game `network.NetworkOutput = yes` | **No lamps.** Nothing connected on port 8000 and nothing lit in game. It ran for about 70 s and exited normally. |

**Read-only checks of other emulators:**
- Supermodel (`D:\CoinOPS Collections\emulators\Sega Model 3\Config\Supermodel.ini`) has `Outputs = none`, so it sends nothing.
- Flycast (`D:\CoinOPS Collections\emulators\Flycast\emu.cfg`) has `[network] NetworkOutput = no` globally. Only
  `[SEGA MARINE FISHING JAPAN]` turns it on. Every other Flycast folder is `no`.
- TeknoParrot has no Output Blaster install in either TeknoParrot folder. 34 profiles have "Enable Outputs"=1 (ChaseHQ2,
  Daytona3, ID6–ID8, WMMT5/6, SR3, Transformers…).

**Open questions (dev PC):**
- TeknoParrot's "Enable Outputs" probably sends outputs through its own channel (Output Blaster/shared memory), not
  MAME's Windows messages, so LED-Next would need a separate reader for it.
- Sega Marine Fishing: check whether Flycast's per-game section name matches the game that LaunchBox launches, and
  whether Flycast's network output is a server LED-Next connects to (like MAME) or a client.
- Not tried: LEDBlinky's MAMEOutputTest, Supermodel with `Outputs = win`, and changing any `mame.ini` from `auto`.
  Changing an emulator file is the user's call.

# Visit 8: Flycast lamps again (2026-11-11)

Built 22ecbd6 (155 tests pass) and played **Sega Marine Fishing** twice. **No lamps:** LED-Next never got a connection.

- **Visit 7 read the wrong Flycast.** LaunchBox runs this game with the emulator **"Flycast Race Atomiswave_Naomi"**
  (`D:\CoinOPS Collections\emulators\Flycastrace\flycast.exe`, ROM `collections\Sega Naomi Race\roms\smarinef.zip`).
  The Naomi platform default (the main `Flycast` folder, 97 games) is used by most Naomi games, but not this one.
- Flycast Race's `emu.cfg` already has `[network] NetworkOutput = yes` for every game (no per-game section). It is
  Flycast `v2.7-1-g44e4c7b50`. Its exe contains the string "NetworkOutput". The folder also has ReShade and an FFB plugin.
- `flycast.log` reports the game id as `SEGA MARINE FISHING JAPAN`, and says nothing about network output.
- **While the game was running, flycast.exe had no TCP socket at all** (checked with `Get-NetTCPConnection` and
  `netstat -ano`). Nothing listened on port 8000. Windows has not reserved port 8000 (only 27339), and MAME used it
  earlier the same day.
- Flycast's source (v2.7 and master): `naomi.cpp` calls `networkOutput.reset()` on every hard reset. `init()` binds
  `INADDR_ANY:8000`, and returns early only if `config::NetworkOutput` is off or the board is a slave. A bind failure
  only goes to `perror` (stderr), not to the log.

**So:** at run time Flycast Race doesn't have NetworkOutput on, or this build differs from upstream v2.7. LED-Next's
new Flycast reader is still untested against a real Flycast.

**Next (user's call, emulator settings):** open Flycast Race's own settings (Settings → Network) and see whether
"Network Output" shows as on. Or try a Naomi game that uses the main `Flycast` folder after turning its global
`NetworkOutput` on (back up `emu.cfg` first, change one line).

# Visit 9: installed to D:\LED-Next (2026-11-12)

With LED-Next, LaunchBox and BigBox closed, `tools\install.ps1` (no options) ran cleanly:
- `D:\LED-Next\LED-Next.exe` (3.2 MB, single file). `PacDrive64.dll` copied from the Debug build. `config\` (cabinet.json
  v6, games.db 35 MB, window.json) copied from the Debug build. Hardware mode. `pacDriveDllPath` null, so no warning.
- Start menu shortcut `LED-Next.lnk`.
- Plugin rebuilt against the real `Unbroken.LaunchBox.Plugins.dll`. `LedNext.LaunchBox.json` names
  `D:\LED-Next\LED-Next.exe`.

**BigBox:**
- Started the installed exe in the tray. Confirmed by `D:\LED-Next\logs\led-next.log`, config path
  `D:\LED-Next\config\cabinet.json`, and the process path.
- Both boards were found (PacLED64#1, PacLED64#2). The game library loaded (75,202 games).
- Live lighting followed the wheel and the selection.
- **Galaga:** "MAME on network port 8000 reports game 'galaga'", then `led0 → P1_START`, "Galaga + lamps", and the end on exit,
  the same as visit 7.

"No LED-Next platform for 'Bally / Midway' / 'Data East' / …" warnings for BigBox wheel categories also appear in the
Debug build's log (118 times), so they are not new. The Debug build folder and its config are left in place as a fallback.
It now has a separate config, so changes made in one aren't seen by the other.

Flycast (visit 8) not retried: Flycast Race's Network Output setting has not been checked yet.

# Visit 10: first GitHub release, v0.1.0 (2026-11-12)

- `git checkout v0.1.0`, then `tools\build-plugin-release.ps1` built `dist\LED-Next-LaunchBox-plugin-0.1.0.zip`
  (34,670 bytes) against LaunchBox 14.1.0.5. It holds `LaunchBox plugin\` with `LedNext.LaunchBox.dll`,
  `LedNext.FrontEnd.dll`, their pdbs, `LedNext.LaunchBox.buildinfo` and `BUILT-WITH.txt`. None of LaunchBox's DLLs.
- **Installed the release as a user would** (with the user's OK):
  - downloaded `LED-Next-0.1.0-win-x64.zip` (1,706,402 bytes) from the release and unzipped it to a new folder;
  - unzipped the plugin zip into that folder;
  - closed LED-Next and ran the release's `tools\install.ps1`.

  It reported "Installing LED-Next 0.1.0 from this release folder", "Kept your settings in D:\LED-Next\config", and
  installed the plugin into `D:\LaunchBox\Plugins\LED-Next` (`ledNextExe` = `D:\LED-Next\LED-Next.exe`).
  The installed exe and both plugin DLLs are byte-for-byte the release's files.
  Note: Expand-Archive keeps the zip's file times, so the exe's date is the build time, not the install time.
- **BigBox:**
  - started the release exe in the tray;
  - both boards found (PacDrive SDK: VID D209 PID 1401/1402) and 75,202 games loaded;
  - lights followed the selection (11 changes, plus the Arcade platform), with no errors.
- **Plugin zip attached to the release** (with the user's OK) through Claude in Chrome: Edit release, upload,
  Update release. The GitHub API lists all three downloads as "uploaded". The built-in browser can't upload local
  files; Claude in Chrome can, once the extension is installed and signed in.

# Visit 11: the lamp lifecycle state machine (2026-11-13)

Installed the build from 7cfb3a0 (`LampLifecycle`) with `tools\install.ps1` (settings kept, plugin rebuilt).
The user played three games in BigBox. **Every step went through the expected states, and nothing stayed lit after an exit.**
No errors in the log.

Galaga (MAME network output), as the log shows it:
```
15:55:20.952 Lamps: Browsing → Launching (front end launched a game).
15:55:24.656 Lamps: galaga started (network port 8000); outputs mapped by C:\LEDBlinky\MameOutputs.ini [default].
15:55:24.656 Lamps: Launching → Playing (galaga started (network port 8000), lights from the front end).
15:55:45.510 Lamps: galaga reports led0 → P1_START
15:55:45.510 Lamps: Playing → PlayingWithLamps (first lamp of galaga).
15:55:49.780 Lamps: galaga ended (network port 8000 reported the end); 1 lamp on, all cleared.
15:55:49.780 Lamps: PlayingWithLamps → Launching (the front end still shows the game until its exit).
15:55:49.899 Lamps: Launching → Browsing (front end exit).
```
- **Dig Dug** (network): the same timeline. The user quit with the Start lamp still blinking ("1 lamp on, all cleared"),
  then "Light source: BigBox (playing): Dig Dug" without lamps, then Browsing on exit.
- **Chase H.Q.** (Windows messages): Browsing → Launching → Playing. It reported genout0–3 and Orientation (none
  mapped, as in visit 7), so it never went to PlayingWithLamps. Then the end, Launching, and Browsing on exit.
- The end of a game arrives about 0.1–0.2 s before BigBox's exit. The panel goes back to the game's base lights
  in between, then to browsing.
- BigBox sent no selection changes while a game was running, so "the panel stays on the game" wasn't tested by a
  real event here. The unit tests cover it.
- Not yet checked at the cabinet: a game started by hand (outside BigBox), and MAME set to both outputs at once.
- One "no LED-Next platform for 'Recently Played'" warning (a BigBox playlist; playlists are still to do).

# Visit 12: lamp maps per emulator, spoken button labels (2026-11-13)

Installed the build from 707418c with `tools\install.ps1` (settings kept, plugin rebuilt). Built, and 173 tests pass.
- **Config upgrade:** "Config upgraded from version 6 to 7 (adds lamps.maps…)", then "7 to 8 (adds speaking button labels, off)".
- **Galaga** (BigBox, MAME network): `galaga started (MAME, network port 8000); outputs mapped by
  C:\LEDBlinky\MameOutputs.ini [default]`, then `led0 → P1_START`. It went through the same states as visit 11
  (Browsing → Launching → Playing → PlayingWithLamps → Launching → Browsing), and everything was cleared on exit.
- **Lamp map row (step 3): works.** Done later, with the user's OK. With LED-Next closed, this row was added
  to `lamps.maps` in D:\LED-Next\config\cabinet.json:
  `{ "emulator": "MAME", "game": "galaga", "output": "led0", "controls": ["P1_BUTTON2"] }`.
  - The log said `outputs mapped by cabinet.json, then C:\LEDBlinky\MameOutputs.ini [[galaga], then [default]]`
    and `galaga reports led0 → P1_BUTTON2`.
  - P1 button 2 blinked **white**, the overlay's colour for a button the game leaves unlit. It was cleared on exit.
  - Pitfalls found on the way:
    - A first run had no lamps ("outputs it reported: none"). The user exited about 4 s in, before the coin.
      That made it look as if button 2 needed a colour, so the user gave it one in the Games tab (blue); the next
      run blinked blue.
    - The user then set P1_BUTTON2 back to "off" (not a revert, so it stays a per-game edit that matches the
      imported lights). The next run blinked white.
  - Afterwards the row was removed (`"maps": []`).
  - Note: LED-Next rewrites cabinet.json in its own layout when it closes, so a one-line row comes back split over
    several lines. Edit the file only while LED-Next is closed.
- **Flycast (step 4):** not tried. Flycast Race's Network Output setting is still unchecked.

## Spoken button labels
- LEDBlinky's `settings.ini` (read only) has `GameStartSpeakControls=1`, `GameStartSpeakName=1` and
  `GamePauseSpeakControls=1`, but `AbortSpeechAnyKey=0`, so LEDBlinky doesn't stop on a key press. LED-Next's
  `stopOnInput` is on.
- Installed voices: Microsoft David Desktop, Microsoft Zira Desktop.
- Saved settings (cabinet.json `speech`): enabled, Microsoft David Desktop, rate 1, volume 75, delay 1 s, game name
  on, start/coin off, joysticks off, blink on, speak on pause on, stop on input on.
- **"Test with the game shown" works.** The user said "it works!". From the log:
  ```
  Speech (test): 1942p · Fire [P1_B1] · Loop [P1_B2]
  Speech: done (3.7 s).
  Speech (test): openice2play · Turbo [P1_B1,P2_B1,P3_B1,P4_B1] · Shoot / Block [...] · Pass / Steal [...]
  Speech: stopped (a key was pressed).
  ```
  There were 6 tests: 1942 three times (3.6–4.0 s) and Open Ice 2 four times. Two of the Open Ice 2 tests were
  stopped by a key press and two finished (4.9–5.1 s). It never stopped by itself.
- **Speech on a BigBox launch works.** In the later Galaga runs, `Speech (launch): Galaga · Fire [P1_B1]` came
  about 1 s after the launch, then `Speech: done (1.4 s)`, before the game was running.
- **The user's report:** moving the joystick stops it. The delay and the blink speed feel right; settings unchanged.
  (The joystick stop is the user's report; the log only names key presses.)
- **Not checked:** speech on MAME pause, and Street Fighter Alpha 2.

## Pause after the game's name (added on the cabinet, d458cf9)
The user asked for a setting for the gap between the game's name and its labels. There is now a Speech tab slider,
"Pause after the name (s)", 0–5 s, saved as `speech.pauseAfterNameSeconds`, with config version 9.
0 keeps the voice's own short pause.
- Installed. The log said "Config upgraded from version 8 to 9 (adds a pause after the game's name, 0 s)".
- The user set it to **1 s**. With a BigBox launch of Galaga, `Speech (launch): Galaga · Fire [P1_B1]` took 2.1 s,
  against 1.4 s at 0 earlier. The extra 0.7 s is the 1 s pause in place of the voice's own short one.

## Windows' newer voices, and the title in the Test button (added on the cabinet, b18b1ee and 53d7f6a)
- The user asked for a better-sounding voice. The cabinet has Windows' newer "OneCore" voices installed: Microsoft
  David, Zira and **Mark**. System.Speech only reaches the old SAPI "Desktop" voices (David Desktop, Zira Desktop).
  The Windows 11 "natural" voices need 22H2 or later; the cabinet runs 21H2 (build 22000).
  - LED-Next now lists the newer voices first and speaks them through Windows.Media.SpeechSynthesis (`OneCoreSpeech`).
    The blinking, speed, volume, pause and stop-on-input all work with them.
  - The App targets `net8.0-windows10.0.19041.0`, so the installed exe grew from 3.9 MB to 28.2 MB.
  - Tested: Microsoft David, Zira and Mark, e.g. `Speech (test, Microsoft Mark): ssf2ta · Light Punch [P1_B1,P2_B1] …`,
    "done (13.7 s)". A key press stopped Zira. **The user likes Mark.**
- **"Test with the game shown" said the rom name** ("sf2ce", "10yard"): it only had the game's lights. Now
  `GameLights` carries the title. Bracketed extras are left out when speaking ("10-Yard Fight (World, set 1)" →
  "10-Yard Fight"). After the fix: `Speech (test, Microsoft Mark): 10-Yard Fight · Pass / Hike [P1_B1] · Lateral [P1_B2]`.
  BigBox launches already said LaunchBox's title.

# Visit 13: more emulators' lamps, the reworked board driver (2026-11-14)
v0.1.4 installed with no config change.
- **Board driver** (reworked for the Ultimate I/O): `device 0: VID D209 PID 1401 … → PacLED64 #1`, `device 1 … PID 1402
  → PacLED64 #2`, both Online, one all-pins command per change. LaunchBox scrolling: boards ~206 ms (write ~206 ms),
  the same as before (board #1's USB path, visit 6).
- **Galaga (MAME)**: `galaga started (MAME, network port 8000); outputs mapped by C:\LEDBlinky\MameOutputs.ini [default]`.
  The user: "worked perfect".
- **Keisho** (`F:\Collections\keisho\keisho.exe`, not in LaunchBox yet, run by hand): `%APPDATA%\Keisho\keisho.ini` already
  has `outputs_network = true` (port 8000) and `outputs_windows = true`. Daytona USA: `outputs come from keisho.exe →
  Keisho`, game `daytona93`, `LampStart → P1_START`. Also sends lamp0–lamp5, LampView1–4, LampLeader, RawLamps,
  RawDrive. Both channels report the game; the second is ignored as it should be. Started outside the front end, the
  panel kept BigBox's last game as the base lights, and its pause had nothing to say.
- **Odin** (`%APPDATA%\odin\odin.ini`: `[outputs] network = on`): Brave Firefighters → `odin.exe → Odin`,
  `1p_start_lamp → P1_START`, `2p_start_lamp → P2_START`. Works.
- **Supermodel** (`Config\Supermodel.ini`: `Outputs = win`) and **TeknoParrot** (Operation G.H.O.S.T., OutputBlaster.dll
  in its folder; `gs2.exe (… OutputBlaster.dll loaded) → TeknoParrot`): both named right, but every name had junk after
  it (`scudplus do`, `LampStart _o`, `LampStart ÍÍ`), so nothing mapped. Their WM_COPYDATA reply is bigger than the
  name; LED-Next read up to the block's end. **Fixed** (`MameCopyData`: the name ends at the first NUL). After the fix:
  Scud Race Plus `LampStart → P1_START`; G.H.O.S.T. `LampStart → P1_START`, `2pLampStart → P2_START`.
  Other outputs: Scud Race LampView1–4, LampLeader, RawDrive, RawLamps, BillDigit1–5; G.H.O.S.T. 1p/2pRecoil,
  1p/2pHolderLamp, BillboardLamp. G.H.O.S.T. restarts its gs2.exe a few times on its own; followed fine.
- **Flycast**: every copy has `NetworkOutput = yes` now (Flycast Race too). Crazy Taxi: game id
  `CRAZY TAXI --------------` (padding is Flycast's), only `lamp7`. No built-in Flycast start lamps, so nothing lit;
  a `lamps.maps` row would need that exact game name.
- **Model 2** (ElSemi's EMULATOR.EXE in the CoinOPS collections): no output setting in EMULATOR.INI. Keisho covers it.
- TeknoParrot with "Enable Outputs" but no OutputBlaster.dll: not checked (only 2 games have the dll).
- Not checked: speech on MAME pause.
