# Cabinet session brief (for Claude on the arcade machine)

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
