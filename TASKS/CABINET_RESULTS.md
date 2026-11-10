# Cabinet results

Filled in on the arcade machine. Read by the development PC afterwards.

**Date:**
**Machine notes** (Windows version, .NET SDK version, LaunchBox version):

## 1. Boards
- PacDrive SDK version / where the DLL came from:
- Log lines from discovery (VID/PID/type/version per device):
- Keys detected (`PacLED64#1`, `PacLED64#2`?):
- Port indexing correct? (pin 1 in the Boards tab = LED 1 on the board?):
- `Pac64SetLEDIntensities` available, or per-pin fallback?:
- Performance during Rainbow (smooth / stutters):
- LEDs off on exit?:

## 2. Driver changes made
<!-- What was changed in src/LedNext.Hardware and why. Include commit hashes. -->

## 3. Panel layout
- Number of controls per player (buttons / start / coin / joystick):
- Trackball lit? Spinner? Admin buttons? Anything else lit:
- Controls deleted from the default (unlit):
- Photo used? (copied to `cabinet/`):

## 4. Wiring
- Wizard completed for all pins?:
- Channel order learned (R,G,B / G,R,B / …):
- Pins used per board:
- Pins skipped / unused:
- RGB cycle result (any buttons showing wrong colours?):
- Chase result:

## 5. Problems and app bugs
<!-- Anything that crashed, looked wrong, or was awkward to use. -->

## 6. LEDBlinky and LaunchBox (for phase 3)
- LEDBlinky install path and version:
- Files copied to `import-samples/ledblinky/`:
- LaunchBox / BigBox version:

## 7. Files brought back
- [ ] `cabinet/cabinet.json`
- [ ] `cabinet/panel-background.*`
- [ ] `cabinet/led-next.log`
- [ ] `import-samples/ledblinky/…`
