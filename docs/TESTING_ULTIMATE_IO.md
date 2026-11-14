# Testing the I-PAC Ultimate I/O

LED-Next drives Ultimarc's **I-PAC Ultimate I/O** (96 LED pins, 32 RGB LEDs) since version 0.1.4. The support
was written from Ultimarc's PacDrive SDK documentation and two open-source drivers (LEDSpicer, Ultimarc-linux),
but **nobody has run it on a real board yet**. If you have one, a 15-minute test helps a lot. Nothing here writes
to the board's flash or changes its key setup: LED-Next only sets LED brightness, like LEDBlinky does.

## What you need
- An I-PAC Ultimate I/O in **keyboard** mode (the default) or **DirectInput** mode. In **XInput** mode the board
  shows up as an Xbox 360 pad and has no LED interface; switch it back by holding P1 button 1 while plugging in USB.
- LED-Next 0.1.4 or newer from the [releases page](https://github.com/QROACHATHOME/LED-Next/releases).
- `PacDrive64.dll` from Ultimarc's PacDrive SDK, copied next to `LED-Next.exe` (LED-Next does not ship it).
- Close LEDBlinky (and anything else that drives the LEDs) while testing.

## Steps
1. Start LED-Next. In the **Boards** tab, under "Add a board", pick **Ultimate I/O**, ID **1**, and click **Add**.
   (ID 2 only if your board runs Ultimarc's "ID #2" firmware.)
2. Tick **Hardware mode** at the top, then click **Rescan boards**. The board's badge should say **Online**.
3. Open the **Log** tab. Copy the lines starting with `PacDrive SDK reports` and the `device …` lines under it.
   An Ultimate I/O should read like `device 0: VID D209 PID 0410 type 6 version … → Ultimate I/O #1, keyboard mode`.
4. Back in **Boards**, click pins **1**, **2** and **3** one at a time (click again to turn each off). Note which
   LED and which colour lights. On a standard harness pin 1-3 should be the R, G and B of the first RGB LED.
5. Click **Pin walk** (top bar): every pin lights in turn. Watch for pins that skip, stay on, or light the wrong LED.
6. Click **White**, then **Rainbow**, then **Off**. Note any flicker, lag, or LEDs that stay lit after **Off**.
7. If you use LEDBlinky: open `LEDBlinkyInputMap.xml` in the LEDBlinky folder and copy the `<ledController …>`
   line for your Ultimate I/O (its `name` and `type`). LED-Next guesses that name, so the real one matters.
8. Optional: wire your buttons (**Wiring** tab or the wiring wizard) and try a game from LaunchBox / BigBox.

## What to send back
Open an issue with the **"Ultimate I/O test report"** template on GitHub (or paste into any issue):
- The `device …` log lines (step 3) and any red error lines.
- The log line `UltimateIO#1: … write(s) so far, … as one all-pins command`.
- Which LED and colour pins 1, 2 and 3 lit (step 4); anything odd in the pin walk (step 5).
- Flicker or lag in step 6, and whether **Off** turned everything off.
- The LEDBlinky `<ledController …>` line (step 7), if you have one.
- Your board's firmware version and mode (keyboard / DirectInput), and Windows version.

## What LED-Next does with the board (for the curious)
- Finds it through the PacDrive SDK: vendor `D209`, product `0410` + (DirectInput ? 2 : 0) + (board ID − 1), SDK
  device type 6.
- Sets brightness with `Pac64SetLEDIntensity` (one pin) or `Pac64SetLEDIntensities` (all 96 at once). Ultimarc's
  readme says the all-pins command is slow and should only be used when more than half the LEDs change, so LED-Next
  sends smaller changes pin by pin.
- Sets the fade time to 0 at start (`Pac64SetLEDFadeTime`), as it does on the PacLED64.
