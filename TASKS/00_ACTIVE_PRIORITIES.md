# Active priorities

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
- [ ] Restore/clamp the main window position (it can open partly off-screen; see CABINET_RESULTS §5)

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
- [ ] Next visit: preview a trackball game and a Neo Geo game specifically (not covered in visit 2)
- [ ] Editing: per-game / per-platform colour overrides stored apart from imported data
- [ ] Control templates (e.g. "6-button fighter") and bulk assign; smart lists
- [ ] Control map editor (today it is rebuilt on import; re-import after re-wiring)
- [ ] Platform name mapping LaunchBox ↔ LEDBlinky (`Sega Genesis` ↔ `Sega_Genesis`, `Arcade` → MAME)
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
