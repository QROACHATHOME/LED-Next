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
