# Testing NanoLED, PacDrive, LED-Wiz and LED strips

LED-Next drives these boards since version 0.1.9. The support was written from the makers' documentation and
open-source drivers (Ultimarc's PacDrive SDK, LEDSpicer, DirectOutput Framework), but **nobody has run it on a real
board yet**. If you have one, a 15-minute test helps a lot. LED-Next only sets LED brightness; it never writes a
board's flash, its key setup or its unit number.

Close LEDBlinky, DOF / DirectOutput, WLED's own app and anything else that drives the LEDs while testing.

## Which board is which
| Board | Pins | Dimming | Needs |
|---|---|---|---|
| Ultimarc **NanoLED** | 60 (20 RGB) | 256 levels | `PacDrive64.dll` next to `LED-Next.exe` |
| Ultimarc **PacDrive** | 16 | **on / off only** (on at 25 % and above) | `PacDrive64.dll` |
| **LED-Wiz** (GroovyGameGear), **Pinscape** in LED-Wiz mode, LED-Wiz clones | 32 | 49 levels | nothing (plain USB HID) |
| **LED strip**: WS2812 / SK6812 LEDs on an Arduino or Teensy with an Adalight sketch, or on **WLED** over USB | 3 per LED | 256 levels | the controller's COM port |

## Steps (all boards)
1. Start LED-Next 0.1.9 or newer. In the **Boards** tab, under "Add a board", pick the model and the ID:
   - NanoLED / PacDrive: the board ID set with Ultimarc's tool (1-4, usually 1).
   - LED-Wiz / Pinscape: the **unit number** (1-16). A Pinscape's default unit is **8**.
   - LED strip: any ID; also the number of **LEDs**, the **Port** (Device Manager → Ports) and the **Baud**: the same
     speed as your sketch, or WLED's serial speed (115200 by default).
2. Click **Add**, tick **Hardware mode** (top), then **Rescan boards**. The board's badge should say **Online**.
3. Open the **Log** tab and copy the lines about your board:
   - Ultimarc: `PacDrive SDK reports …` and the `device …` lines (e.g. `VID D209 PID 1481 type 4 … → NanoLED #1`).
   - LED-Wiz: `LED-Wiz USB device: VID FAFA PID 00F7, output report 9 bytes → LED-Wiz #8`.
   - Strip: `LedStrip#1: COM5 at 115200 baud, 30 LEDs (Adalight)` or the reason it couldn't open the port.
4. In **Boards**, click pins **1**, **2** and **3** one at a time (click again to turn off). Note which LED and colour
   lights. On a strip the pins are labelled `1 R`, `1 G`, `1 B` (LED 1's red, green, blue).
5. Click **Pin walk** (top bar): every pin lights in turn. Watch for pins that skip, stay on, or light the wrong LED.
6. Click **White**, then **Rainbow**, then **Off**. Note flicker, lag, and whether **Off** turns everything off.
   (A PacDrive can't dim: Rainbow just switches outputs on and off.)
7. LED strips only: under "Runs of LEDs", add a run (e.g. LEDs 1-10 follow P1_B1) and click **Rainbow**: all ten
   should show P1_B1's colour. Leave it on **White** for a minute: it must stay lit (LED-Next resends the frame
   every second, so WLED doesn't fall back to its own effect).

## What to send back
Open an issue with the **"Board test report"** template on GitHub (or paste into any issue):
- The board, its firmware version (Pinscape: its version; strip: the sketch or WLED version, LED type and count).
- The log lines from step 3, the `… write(s) so far` line, and any red error lines.
- Steps 4-7: what lit, anything odd.
- LEDBlinky users: the `<ledController …>` line for the board from `LEDBlinkyInputMap.xml` (LED-Next guesses the
  names `NANOLED`, `PACDRIVE`, `LEDWIZ`).

## What LED-Next sends (for the curious)
- **NanoLED**: found through the PacDrive SDK (vendor `D209`, product `1481` + ID − 1, SDK type 4). Same calls as the
  PacLED64 with 60 bytes; the all-pins command only when more than half the LEDs change (Ultimarc's readme calls it
  slow). Fades and the board's own flashing are set to 0 when it opens.
- **PacDrive**: product `1500` + ID − 1, SDK type 1. `PacSetLEDStates` with one bit per output.
- **LED-Wiz**: vendor `FAFA`, product `00F0` + unit − 1; only the interface with 9-byte output reports (report ID + 8) (a Pinscape
  also lists its keyboard and joystick). Each changed frame is SBA `64, FF, FF, FF, FF, 2, 0, 0` (all outputs on,
  and back to output 1), then four PBA reports of 8 levels (0-48).
- **LED strip**: Adalight: `Ada`, LED count − 1 (2 bytes), a checksum, then R, G, B per LED. The port opens with DTR
  and RTS off. Resent every second while nothing changes.
