# Front ends other than LaunchBox

LED-Next lights the panel for the game a front end selects and plays. LaunchBox / BigBox has its own plugin
(installed by "Install LED-Next.cmd"). Since version 0.2.0 these work too:

| Front end | How | Selection | Start / exit | Front end start / quit |
|---|---|---|---|---|
| **HyperSpin 2** (HyperHQ) | LEDBlinky stand-in | yes | yes | yes |
| **HyperSpin 1.x** | LEDBlinky stand-in | yes | yes | yes |
| **RetroFE / CoinOPS** | LEDBlinky stand-in | yes | yes | yes |
| **RocketLauncher** | LEDBlinky stand-in | (from the front end) | yes | – |
| **Attract-Mode / Attract-Mode Plus** | LED-Next's plugin | yes | yes | yes |
| **ES-DE** | event scripts | yes | yes | yes |
| **RetroBat** | event script | yes | yes | yes |
| **Playnite** (desktop and fullscreen) | LED-Next's extension | yes | yes | yes |
| **PinballX** | LED-Next's plugin | yes | yes | yes |
| **PinUP Popper** | launch / close scripts | no | yes | – |
| anything that can run a program | `LedNextCmd.exe` | | | |

**None of these has been tried on a real cabinet yet** (only with LED-Next's own tests and test commands). If you
use one, please try it and tell us (see the end).

Not supported: **Batocera** (a Linux system; LED-Next is a Windows program) and **GameEx** (its LEDBlinky
connection is a GameEx plugin; ask if you need it).

## Setting one up
Open LED-Next's **Front ends** tab. Each front end has a card with its steps:

- **Install** buttons (Attract-Mode, ES-DE, RetroBat, Playnite, PinballX) copy LED-Next's own files into the front
  end's folder. **Remove** takes them out again. LED-Next never changes a front end's own settings: where a setting
  is needed, the card says which one.
- **LEDBlinky stand-in** (HyperSpin, RetroFE / CoinOPS, RocketLauncher): front ends that support LEDBlinky run
  `LEDBlinky.exe` with a command for each event. LED-Next makes `LEDBlinky-stand-in\LEDBlinky.exe` in its folder:
  it is LED-Next's command tool under LEDBlinky's name, so you point the front end's LEDBlinky setting at it.
  It is **not** LEDBlinky. Close the real LEDBlinky: only one program can drive the boards.
- **PinUP Popper**: copy the two lines from the card into each emulator's Launch and Close scripts.

### Notes per front end
- **HyperSpin 2**: HyperHQ > HyperSpin > Lights: "Enable LEDs" on, "LEDBlinky Executable Path" = the stand-in.
  Leave "Auto-Sync LEDBlinky Config" and "MAME LED Triggers" off.
- **RetroFE / CoinOPS**: `settings.conf`: `LEDBlinkyDirectory = <LED-Next>\LEDBlinky-stand-in` (a folder, not the
  exe). RetroFE names a game's emulator by its launcher; when that isn't a platform ("retroarch"), LED-Next uses the
  collection selected before. A launcher's `.conf` can say `LEDBlinkyEmulator = MAME`.
- **ES-DE**: after Install, in ES-DE's Main menu > Other settings: turn on **Enable custom event scripts** and
  **Browsing custom events**. (ES-DE waits for its scripts while you browse; LED-Next's take a few tens of ms.)
- **RetroBat**: one script for every event, run in the background. Its game start doesn't say the system, so
  LED-Next takes it from the rom's folder (`roms\<system>\…`).
- **Playnite**: restart Playnite after Install. A game's first platform names its LED-Next platform.
- **PinballX**: close PinballX, Install, then tick **LED-Next** in PinballX's Plugin Manager.
- **Pinball and DOF**: on a pinball cabinet, DOF (DirectOutput) usually drives an LED-Wiz or Pinscape during play.
  Don't give LED-Next the same board.
- **Start-up and shut-down shows** (Animations tab) play for all of these, as for BigBox; for desktop LaunchBox and
  Playnite's desktop mode only with the "Also when desktop LaunchBox or Playnite…" box.

## How games are matched
These front ends don't share LaunchBox's game ids. LED-Next gets a rom (or table) name and a system / emulator
name, and matches them like a LaunchBox game it hasn't seen before:
- the system name to a LED-Next platform: by name (`Sega Genesis` = `Sega_Genesis`), by EmulationStation's short
  names (`snes`, `megadrive`, `psx`, `arcade`, `vpx`…), or by your own mapping (Games tab);
- the rom name (the file name without its extension: `sf2.zip` → `sf2`) to a game on that platform, else the
  title, else the platform's default lights.
- No system at all (LEDBlinky's command line allows that): the last one the front end named, else MAME.

## LedNextCmd.exe
`LedNextCmd.exe` (in LED-Next's folder) passes one event to the running LED-Next and exits at once. It takes
LEDBlinky's command line:

| Command | Event |
|---|---|
| `LedNextCmd.exe <rom> [<emulator>]` | game start (`3 <rom> <emulator>` too, as RetroFE sends it) |
| `LedNextCmd.exe 4` | game end |
| `LedNextCmd.exe 9 <rom> [<emulator>]` | game selected (`15 …` too) |
| `LedNextCmd.exe 8 <emulator>` | list / system selected |
| `LedNextCmd.exe 1` / `2` | front end start / quit (`1` also starts LED-Next if it isn't running) |
| `LedNextCmd.exe 5` / `6` | screensaver start / stop (logged; LED-Next runs its own idle show) |

LEDBlinky's other commands (`.lwax` animations, `11`–`14`, `16`, `17`) are logged and ignored. A `~` in a value is a
space, as in LEDBlinky. `--source NAME` in front names the front end in LED-Next's log (otherwise it is the program
that ran the tool). `--es-de <event> <args>` and `--retrobat <event> <args>` read EmulationStation's script
arguments. Every command is written to `logs\led-next-commands.log`, with what was done with it.

## Trying one and reporting back
1. Set it up from the Front ends tab, start LED-Next, then the front end.
2. Browse a few games, start one, quit it, close the front end.
3. Look at `logs\led-next-commands.log` (Front ends tab > "Open the command log") and LED-Next's Log tab.
4. Open an issue on GitHub with: the front end and its version, what lit (or didn't), and the log lines.
