# Project Spec

## Goal
A stable, understandable replacement for LEDBlinky that makes it easy to manage LED setups for
thousands of games.

## Principles
- Visual first: you always see the panel and what it is doing.
- Configure once, inherit everywhere: most games should never need individual editing.
- Hardware failures must never hang or crash the front end.
- Works without hardware (simulator) so setup can be done away from the cabinet.

## Phases

### Phase 1 — Hardware core (done; verified on the cabinet 2026-11-10)
- PacLED64 driver, multiple boards per PC, stable board keys
- Simulator mode
- Panel designer: drag buttons, edit label/player/kind/size, add/delete
- Control kinds drawn distinctly: buttons (circle), Start, Coin (rounded square), Joystick
  (ball-top on a gate), Trackball (large ball). All are RGB-wired the same way
- Default 4-player layout: per player buttons + Start + Coin + joystick, plus one trackball
  (41 RGB controls = 123 of the 128 pins on two PacLED64s)
- Wiring graph (Nodify): button R/G/B outputs → board pins, with auto-wire and clear
- Test patterns: off, white, button chase, RGB cycle, rainbow, pin walk, identify button
- Boards view: live 64-pin intensity grid, click a pin to light it
- Brightness and gamma
- Wiring wizard: one pin lit at a time; click the button that lit, press R/G/B. Learns the
  channel order from the first button and suggests the next channel (Enter to accept)
- Panel photo background (copied into the config folder; panel aspect matches the photo)
- Undo/redo for panel and wiring edits (not for hardware mode / brightness); duplicate,
  copy/paste, arrow-key nudge

### Phase 2 — Panel polish
- Button shapes, joystick/trackball/spinner glyphs, multi-select
- Import/export panel + wiring files

### Phase 3 — Game database (current)
- SQLite store, inheritance (global → platform → control template → game)
- Import from LEDBlinky (controls/colors data, color definitions, input map, animations)
- Bulk assign, smart lists, fast search, live preview while browsing

Done so far: SQLite library, inheritance game → parent ROM → platform default → MAME default,
LEDBlinky import (everything above except animations), control map from the LEDBlinky input map,
Games tab with platform filter, search and live preview, per-game and per-platform editing
(kept on re-import, Revert to imported), bulk copy/paste onto selected games or all search matches,
named templates and smart lists (buttons, players, control type, hide clones). Remaining for phase 3:
LaunchBox ↔ LEDBlinky platform-name mapping, control map editor.

### Phase 4 — Front ends
- `LedNext.Service` owns hardware; app and plugins are clients over a named pipe
- LaunchBox / BigBox plugin: selection changed, game launched, game exited, attract mode

### Phase 5 — Animations
- Attract mode, per-event effects, LEDBlinky animation playback

### Later
- More boards (Ultimate I/O, NanoLED, PacDrive, LED-Wiz, …), more front ends

## Non-goals (for now)
- Non-Windows platforms
- Writing back into LEDBlinky files
