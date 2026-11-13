# Active priorities

- [x] **Samples out of the public repo** (2026-11-12): `import-samples/ledblinky` and `launchbox` are git-ignored (LEDBlinky's files aren't ours to publish; the LaunchBox files are personal). Tests needing them use `[LedBlinkySamplesFact]` and skip without them. **Dev PC:** pulling deletes your copies; get them back with `git restore --source=4a3a776 -- import-samples/ledblinky import-samples/launchbox`

## Phase 1 — Hardware core
- [x] Solution scaffold, docs
- [x] Core model: panel, wiring, config store
- [x] Lighting engine + test patterns
- [x] PacLED64 driver (PacDrive SDK) + simulator
- [x] WPF app: panel designer, wiring graph, boards view, log
- [x] Cabinet-visit prep: wiring wizard, panel photo background, undo/redo, duplicate/copy/paste, arrow-key nudge
- [x] Default layout includes Coin + joystick per player and a shared trackball (41 controls, 123 of 128 pins)
- [x] **Verify on the arcade machine** (below). Done 2026-11-10; see `TASKS/CABINET_RESULTS.md`
- [x] Replace the default 4-player layout with the real panel layout and wiring (`cabinet/cabinet.json`)
- [x] Main window position saved in `config/window.json` and moved onto the nearest monitor at start
      (dev PC: checked with off-screen and two-monitor positions; cabinet visit 4: restored, correct)

## Phase 3 — Game database
- [x] Game model, logical control names, inheritance resolver (game → parent → platform → MAME)
- [x] LEDBlinky importer: LEDBlinkyControls.xml, Colors.ini, Controls.ini, minimized MAME xml,
      Color-RGB.ini, settings.ini, input map → control map (tested on the real LEDBlinky 8.3 files)
- [x] SQLite library (`config/games.db`), search
- [x] Games tab: import, platform filter, search, live preview on the panel and the boards
- [x] **Verify at the cabinet**: done 2026-11-10 (visit 2). 34 games matched LEDBlinky; `default.cfg` found.
      See CABINET_RESULTS "Visit 2".
- [x] Decided: always-active UI controls (`UI_SELECT`…) light only shared (player 0) buttons. LEDBlinky lit
      P3 button 3 green in every game (it sends Enter); that was a bug, and it now follows the game.
- [x] Visit 3: trackball game (Golden Tee Golf II) and Neo Geo games previewed on the panel; correct
- [x] Editing: per-game / per-platform colour overrides stored apart from imported data
      (Games tab: click a button, pick a colour / Off / #hex, edit "Does"; Revert to imported;
      "Only customized" filter). Kept on re-import. Not part of Ctrl+Z (Revert is the way back).
- [x] Visit 3: edited Fatal Fury 2 in Hardware mode; the real panel followed live
- [x] Bulk assign: Copy all lights / Copy this button, multi-select in the games list (Ctrl/Shift-click,
      Ctrl+A, Ctrl+C / Ctrl+V), Paste to selected or to all search matches (one transaction, confirmation
      with a count), Revert selected
- [x] Named templates (Games tab → Templates): preview, apply to selected / all matches, save the
      lights shown now as a template, delete. Starters: 6-button fighter, Neo Geo 4-button, 3-button,
      2-button, 4-player 3-button, Trackball (seeded once; deleting them sticks)
- [x] Smart lists: saved filters (platform, search, lit/customized, hide clones, buttons min–max,
      players min–max, control type). Starters: 6/4/2-button games, 4-player, trackball, spinner,
      lightgun, my customized games. Button counts and control types are worked out on import
      (re-import an older library to fill them)
- [x] Narrower lists: LaunchBox genre filter + smart list "6-button fighting games"; Templates tab →
      **Try on selected game** shows the result on the panel without saving
- [x] Visit 4: "6-button fighter" on Street Fighter Zero 2 Alpha: kicks (buttons 4-6) are the bottom row on
      the real panel. "6-button fighting games" lists 402 games on the cabinet ("6-button games": 472)
- [ ] Control map editor (today it is rebuilt on import; re-import after re-wiring)
- [x] Platform name mapping LaunchBox ↔ LEDBlinky (`Sega Genesis` ↔ `Sega_Genesis`, `Arcade` → MAME),
      changeable in Games tab → LaunchBox (done in phase 4a)

## Phase 4 — Front ends
### 4a — LaunchBox data (dev PC, done 2026-11-10; written from LaunchBox's documented XML layout)
- [x] Read LaunchBox `Data\Platforms\*.xml` (read only) at start-up and on demand; match each game to an
      LED-Next platform (same name / alias / your mapping / new) and game (ROM file name, then title)
- [x] LaunchBox games missing from the LEDBlinky data are added to the library (platform default until
      set up) and survive LEDBlinky re-imports
- [x] Filters: added to LaunchBox in the last N days (presets or typed), only in LaunchBox, needs setup.
      Smart lists: "New in LaunchBox, needs setup (30 days)", "New in LaunchBox (7 days)",
      "In LaunchBox, needs setup"
- [x] **Verified at the cabinet** (visit 3): 41,633 games, mappings right, recent list confirmed by the
      user; samples in `import-samples/launchbox/` (Arcade.xml too big; Neo Geo MVS instead)
- [x] Cave, ZiNc, Final Burn Neo/Alpha platforms use MAME data (existing libraries upgraded); an emulator
      exe (Emulators.xml + built-in list) is never a rom name; schema log line fixed (schema 5: genre)
- [x] LaunchBox-only games lit from MAME data get MAME's button count / control types / players, so the
      button and control smart lists find them (they were 0 before: Neo Geo MVS, Arcade Clones)
- [x] Visit 4 fixes (cabinet): title matching ignores games added from LaunchBox (an old `mame` row was found
      again by title); stale LaunchBox-only games are removed on each read unless customized; the MAME
      fill-in no longer rewrites games whose MAME row has 0 players on every start

### 4b — Live lighting from LaunchBox / BigBox (needs the arcade machine)
- [x] Brought back (visit 3): `Unbroken.LaunchBox.Plugins.dll` 14.1.0.5; LaunchBox and BigBox run on
      **.NET 10.0.14** (`net10.0` in both runtimeconfig files). Plan the plugin's target framework for that.
- [x] Decided: the LED-Next app owns the boards and listens on the pipe `LedNext.FrontEnd` (same speed
      as a service, less to install). Single instance; `--minimized`; previews paused while minimized
- [x] Built on the dev PC (net8.0 plugin, loads in .NET 10): pipe protocol + client + server, session rules
      (select / launch / exit / closing), game lookup by LaunchBox id, plugin classes (system events, game
      launching, selection poll, BigBox theme element), install script. Checked end to end with a fake
      LaunchBox host calling the real plugin DLL (it started LED-Next, lit by LaunchBox id, launch/exit,
      closing → off); 107 tests
- [x] **Visit 5** (2026-11-10): plugin built against the real 14.1.0.5 DLL (targets `net10.0-windows` there,
      needs the .NET 10 SDK; `net8.0-windows` with the stand-in), installed, and live lighting **works in
      LaunchBox and BigBox**: selection, launch, exit, close → off, all games matched by LaunchBox id
- [x] BigBox selection: the `SelectionChanged` system event + `GetAllSelectedGames()` work in BigBox; the theme
      element is not needed (kept as an option)
- [x] Slight selection lag in LaunchBox (instant in BigBox): LED-Next now logs a `Timing` line per change
      (pipe / wait / lookup / boards), and the engine shows a new source at once instead of at the next frame
      tick (up to 33 ms sooner). Dev PC, fake host: 1–14 ms from the plugin to the boards
- [x] Visit 6: `Timing` shows pipe 0–1 ms, lookup 1–4 ms, **boards ~206 ms**. A direct test found
      **PacLED64 #1 ~20× slower than #2** (198 ms vs 9 ms for 64 LEDs; 6.1 vs 0.3 ms per LED): #1 sits two
      hubs deep on a hub shared with another Ultimarc device. The user felt no lag this time
- [ ] **At the cabinet (no code):** move PacLED64 #1's USB cable to a port on the PC (or #2's hub) and re-run
      the timing test (CABINET_RESULTS visit 6). Per-pin diff writes would not help on the slow board
- [x] Visit 6b: P3 button 3 no longer green in games lit by a platform default; LaunchBox games without
      lights use the same game set up under another emulator in LEDBlinkyControls.xml (80 games:
      TeknoParrot → Model 2 / MAME, Arcade → AAE…). Verified at the panel
- [x] Extra colors.ini (Games → "Extra colors.ini…", config v4): lights MAME games LEDBlinky leaves unlit;
      23 more at the cabinet from a downloaded file
- [x] Button banners (Games → "Button banners…", config v5, schema 7): a folder of "6 button layout" pictures
      (`<rom>.png`) is shown next to the panel, and MAME games without lights of their own light as many
      buttons as their banner uses (596 banners on the dev PC; 226 have no LEDBlinky colours). Only the count is
      used, not the positions
- [x] LaunchBox pictures in the Games tab: the game's "Arcade - Control Panel" and "Arcade - Controls Information"
      images (read only, from the folders in LaunchBox's Platforms.xml), next to the panel; click opens full size.
      Dev PC: checked with a stand-in LaunchBox folder only
- [x] At the cabinet (2026-11-11): LaunchBox's real pictures show for 10-Yard Fight (Arcade), Kizuna Encounter
      (Neo Geo MVS) and 1000 Miglia (Arcade Clones). Most platforms have none; Arcade Clones has ~2,000 of each kind
- [ ] At the cabinet: choose the banners folder, check a few games lit only by the MAME default (e.g. 64th
      Street, Batsugun, Armed Formation) light buttons 1..n. **Skipped 2026-11-11: the banners are not on the
      cabinet** (looked in D:\CoinOPS Collections\collections and D:\LaunchBox, the only places they would be; the
      596 banners seem to be on the dev PC only). Copy them to the cabinet first
- [x] Minimize to the system tray (tray icon: open / exit; the plugin's `--minimized` starts in the tray)
- [x] **Each MAME install's own keys** (built at the cabinet; Downhill Bikers in Mame02XX Race verified at the panel): the cabinet
      has 33 MAME installs; the control map came from `Mame02XX` only. A game now lights by the keys of the
      emulator LaunchBox runs it with (game's own emulator, else the platform default; `launchbox_games.emulator`,
      DB schema 6): `ctrlr` file from mame.ini → `cfg/default.cfg` → `cfg/<rom>.cfg`, each key followed to the
      panel button that sends it (LEDBlinky input map). Only player buttons/start/coin the install sets to a key,
      and that land elsewhere than the cabinet's map, change; a key no button sends = not lit.
      Dry run on the cabinet: Mame02XX Race (19) / Flight (12) remap P1–P2 (button 1 = P1_B4…); mame Current
      Race (133) and Spin (48) swap buttons 7/8; Gun installs and bemani: buttons not on the panel go dark;
      Mame02XX changes only 12 games (their own cfg), mame0251 1. Shown in the Games tab "Lit from …" line and
      the front-end log ("keys of …")
- [x] **TeknoParrot profiles' keys** (built at the cabinet; Virtua Fighter 5 R verified at the panel): LaunchBox
      launches a TeknoParrot game by its profile (`Teknoparrot2024\UserProfiles\<game>.xml`); its keyboard bindings
      (DirectInput `BindName` "Keyboard Button LeftControl", RawInput `KeyboardKey` "ControlKey" on the I-PAC 4X)
      are turned into MAME key codes and applied like a MAME install's keys. Gamepad/wheel/gun/XInput bindings keep
      the cabinet's map. Dry run: 758 LaunchBox games launch a profile, 287 light differently (VF5 R: button 4 =
      P1_B7; Project DIVA: buttons 1/2 swapped…), 8 profiles missing
- [ ] ~15 games not matched because LEDBlinky's title differs ("Virtua Fighter 2.1"…); a manual link or
      looser title rule could cover them
- [ ] BigBox playlists on the wheel (most of the cabinet's wheel: Cinematronics, Gaelco, Taito B System…)
      keep the last lights. Idea: light a playlist from its games' platform (e.g. MAME default)
- [x] BigBox platform wheel lights the highlighted platform's default lights (plugin sends the selected
      platform when no game is selected). Playlists / categories (no platform) keep the current lights
- [x] Visit 6: BigBox's platform wheel lights each platform (IGS PC, Laser Disc, Atomiswave, Naomi, Neo Geo
      MVS…; user: looked right), no flicker between games in a list, back to the wheel shows the platform.
      The **Arcade category** reports "Arcade" and lights the MAME default (user confirmed); the plugin logs
      each wheel change ("Wheel: …")
- [x] The plugin's `started` message is kept while LED-Next starts; the client connects as soon as it listens
      (visit 6: connected ~1 s after LED-Next listened, before any scrolling)
- [x] Live lamps from emulators (dev PC 2026-11-11, config v6): MAME's Windows output messages and network
      output (port 8000; Flycast), mapped by LEDBlinky's MameOutputs.ini, drawn over the game's lights; a Start
      lamp also blinks that player's button 1 (Start/Coin have no LEDs yet). Checked with a fake MAME speaking both
      protocols (written from MAME's source); not yet with real MAME
- [x] **Visit 7**: MAME lamps work on the panel (Galaga, Dig Dug over network; Chase H.Q. over Windows messages).
      28 of 33 mame.ini still `output auto` (no lamps). Supermodel `Outputs = none`. TeknoParrot: no Output Blaster
      installed, no lamps. Flycast sent nothing LED-Next understood
- [x] Flycast's dialect (found in its source): `game = id` instead of `mame_start`, hex values, no stop message.
      Now read (`OutputLineParser`); checked with a fake Flycast
- [ ] Visit 8: Sega Marine Fishing (Flycast) again; note the outputs it reports
- [ ] TeknoParrot lamps: find how "Enable Outputs" sends them without Output Blaster (shared memory? its own
      protocol?) before writing a reader
- [ ] **Lamps design** (proposal, `TASKS/DESIGN_LAMP_STATES.md`): per-emulator adapters → one lifecycle state machine in
      Core (who owns the panel: browsing / launching / playing / playing + lamps) → mapping tables per emulator
      (MameOutputs.ini for MAME, LED-Next's own tables for the others). Do it before adding Flycast/TeknoParrot maps
- [ ] After Start/Coin LEDs are wired: re-run the wiring wizard for them; consider turning `lamps.startLampsOnButton1` off
- [ ] Attract mode / idle lighting in the front end (with animations, phase 5)
- [ ] Animations (`*.lwax`) — phase 5

## At the arcade machine — suggested order

### 1. Get the boards talking (the driver has never run on real hardware)
1. Get the Ultimarc **PacDrive SDK** and put `PacDrive64.dll` next to `LED-Next.exe`
   (or set `hardware.pacDriveDllPath` in `config/cabinet.json`).
   If only a 32-bit `PacDrive32.dll` is available, build the app with `PlatformTarget=x86`.
2. Tick **Hardware mode**. The log should list both boards as `PacLED64#1` and `PacLED64#2`.
   If both report the same ID, set unique IDs with Ultimarc's tool (or add `Pac64SetDeviceId` support).
3. Boards tab: click pin 1 on each board. Confirm an LED lights on the right board
   (checks 0- vs 1-based port indexing in `PacLed64Board.Write`).
4. Check the log for `Pac64SetLEDIntensities not available` — if it appears, the per-pin fallback
   is in use; confirm it is not visibly slow during Rainbow.

### 2. Lay out the real panel
5. Take a straight-on photo of the panel. Panel tab → **Set photo…**.
6. Drag the default controls onto the photo; Add / Duplicate (Ctrl+D) / Delete to match the real
   panel. The default has, per player, buttons + Start + Coin + joystick (`P1_STICK`…), and one
   `TRACKBALL`. Delete any control that has no LED (e.g. an unlit joystick) so the wizard and
   tests don't expect it; set Kind for anything new (Joystick, Trackball, Spinner, Admin…).
   Pin budget: two PacLED64s = 128 pins = 42 RGB controls.

### 3. Map the wiring with the wizard
7. Panel tab → Wiring wizard → **All boards**, "Start from empty wiring" ticked → Start.
8. For each pin: click the button that lit, press R/G/B (or Enter to accept the suggestion).
   Space skips unused pins, Backspace goes back, Esc finishes. Ctrl+Z afterwards undoes the whole run.
9. Check: **RGB cycle** — every button red, then green, then blue, then white. **Chase** — buttons
   light one at a time in the expected order.
10. Exit the app: all LEDs should go off.

## Gather for Phase 3 (from the arcade machine) — done 2026-11-10
- LEDBlinky 8.3 files are in `import-samples/ledblinky/`; LaunchBox is 14.1
- Still useful next visit: MAME's `cfg/default.cfg` from `D:\CoinOPS Collections\emulators\Mame02XX`
  (copy into `import-samples/mame/`) so the control map can be tested with the real key assignments

## Known limitations
- Panel designer: single selection only (no multi-select / group move yet).
- Graph node positions in the Wiring tab are not part of undo.
- The ComboBoxes use the default light Windows style.
- Game lights: front-end (`frontEnd`) sections, per-emulator input codes, `.lwax` animations and
  LEDBlinky output options (`LightPlayerStartCoin`…) are not imported yet. Non-PacLED64 controllers
  in the input map are skipped.
