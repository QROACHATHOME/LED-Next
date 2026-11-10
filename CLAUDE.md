# CLAUDE.md

## What this project is
LED-Next is a C#/.NET 8 Windows replacement for LEDBlinky. It drives RGB LED buttons on arcade
control panels through LED controller boards, shows the panel visually, and (later phases)
lights the panel per game for front ends such as LaunchBox/BigBox.

The reference cabinet is a **4-player panel driven by two Ultimarc PacLED64 boards**.

## Read these files first
1. `ARCHITECTURE.md`
2. `PROJECT_SPEC.md`
3. `TASKS/00_ACTIVE_PRIORITIES.md`
4. If you are on the **arcade machine** (LED boards attached, LEDBlinky/LaunchBox installed):
   `TASKS/01_CABINET_SESSION.md` — it is your brief for the session.
5. If `TASKS/CABINET_RESULTS.md` has been filled in, read it: it is the latest real-hardware truth.

## Two machines
Work happens on two PCs that share this repo through git (private GitHub repo):
- **Dev PC** — no LED hardware; simulator mode only.
- **Arcade machine** — two PacLED64 boards, LEDBlinky, LaunchBox. Hardware testing happens here.

Claude's own memory does not travel between them, so **this repo is the memory**: put anything the
other machine needs in `TASKS/` (results, decisions, open questions), commit, and push. Pull before
starting work. Never commit `PacDrive*.dll` (Ultimarc's file; git-ignored).

## Hard rules
- C# 12 / .NET 8, WPF for the UI. Do not switch UI frameworks without being asked.
- `LedNext.Core` has **no** WPF and **no** native/hardware dependencies. It must stay unit-testable.
  SQLite lives in `LedNext.Data`; parsing/importing and game-light resolution stay in Core.
- Hardware I/O happens **only on the engine output thread** (inside `LedEngine`'s output lock).
  Board discovery goes through `LedEngine.ReplaceBoards` so it never races a write.
- Pins are **1-based** everywhere in config, UI and logs (they match the board's silkscreen).
  Convert to 0-based only at the native call.
- Boards are identified by a stable key (`PacLED64#<boardId>`), never by USB enumeration order.
- `cabinet.json` carries a `version` field. Bump it and add a migration when the schema changes.
- LEDBlinky files are **read-only input**. Never write to a LEDBlinky install.
- Keep the app usable with no hardware attached (Simulator mode). Every feature must work there.

## Fragile / unverified areas
- PacDrive SDK P/Invoke signatures and port indexing (see `TASKS/00_ACTIVE_PRIORITIES.md`,
  "Verify on the arcade machine"). These were written from memory of the SDK and have not been
  run against real boards yet.
- Nodify graph bindings (anchors, pending connections) — check after any Nodify upgrade.
- The LEDBlinky control map (`ControlMapBuilder`) and MAME `cfg/default.cfg` reading: tested on the
  real LEDBlinky files, but MAME's own `default.cfg` has not been seen yet. Compare the Games tab
  with what LEDBlinky lights before trusting it.

## Expected working style
- Say which subsystem you are changing.
- Keep changes small and localized; add logging through `AppLog` when debugging.
- Run `dotnet build` and `dotnet test` before calling work done.
- Call out behavior changes explicitly, especially anything that changes what the LEDs do.
