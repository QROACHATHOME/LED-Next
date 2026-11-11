# Architecture

## Projects

| Project | Depends on | Role |
|---|---|---|
| `src/LedNext.FrontEnd` | nothing | Front-end pipe protocol (`FrontEndMessage`, JSON lines) and `FrontEndClient`; all a plugin carries |
| `src/LedNext.LaunchBox` | FrontEnd, LaunchBox's plugin API | LaunchBox / BigBox plugin (thin: events → pipe). Built against `<LaunchBox>\Core\Unbroken.LaunchBox.Plugins.dll`, else against `tools/LaunchBoxPluginStubs` (compile-only stand-in) |
| `src/LedNext.Core` | FrontEnd | Data model (panel, wiring, config), JSON store, lighting engine, test patterns, board interfaces, logging, game model + light resolver (`Games/`), LEDBlinky importer (`Import/LedBlinky/`) |
| `src/LedNext.Data` | Core, Microsoft.Data.Sqlite | The game library in SQLite (`GameDatabase`, implements `IGameLibrary`) |
| `src/LedNext.Hardware` | Core | Board drivers: Ultimarc PacLED64 (via PacDrive SDK DLL) and a simulator |
| `src/LedNext.App` | Core, Data, Hardware | WPF desktop app: panel designer, games browser, Nodify wiring graph, board test view |
| `tests/LedNext.Tests` | Core, Data | xUnit tests; the importer tests run on the real files in `import-samples/` and `cabinet/` |

Decided (phase 4b): the **LED-Next app** owns the boards while LaunchBox/BigBox run (no separate service).
It is as fast (same engine, sub-millisecond pipe) and much less to install; a headless host could later
take the same pipe without changing the plugin.

## The three layers of data

1. **Panel layout** (`PanelLayout`, `PanelButton`) — where each control sits and what it is
   (`P1_B1`, `P2_START`…). Button IDs are the names game profiles will refer to.
2. **Wiring** (`WiringMap`) — which board pin drives each button's R, G and B channel.
   Rules: a pin is driven by at most one channel, a channel drives at most one pin.
3. **Game lights** (phase 3) — per game, which *logical* controls are lit and in what colour
   (`P1_BUTTON1` Red, `P1_START` White…), plus the **control map** that ties logical controls to
   panel buttons (`P1_BUTTON7` → `P1_B8`).

Panel + wiring + control map + settings are stored together in `config/cabinet.json` next to the
exe (portable, like LEDBlinky). Game data lives in `config/games.db` (SQLite).

## Games (phase 3)

```
LEDBlinky folder ──► LedBlinkyImporter ──► InMemoryGameLibrary ──► GameDatabase (games.db)
                         │                                              │ IGameLibrary
                         └──► ControlMap (cabinet.json)                 ▼
                                                         GameLightResolver.Resolve(platform, game)
                                                                        │ ResolvedLights
                                                                        ▼
                                                          GameLights (ILightSource) ──► engine
```

- **Logical controls** (`ControlNames`): LEDBlinky/MAME names, normalised. `START1` → `P1_START`,
  the four `P1_JOYSTICK_*` directions → `P1_JOYSTICK`, `P1_TRACKBALL_X…` → `P1_TRACKBALL`.
  `CONTROL_*` entries are descriptions ("8-Way Joystick"), not lit controls.
- **Inheritance** (`GameLightResolver`): game → parent ROM (clones) → MAME's data for MAME-based
  platforms (Neo Geo…) → platform default → parent platform default (usually MAME). The first level
  that lists any controls decides *which* controls are lit; unlisted controls are off, as in
  LEDBlinky. A control without a colour takes it from further up the chain, else the platform's
  default-active colour. Always-active controls of platform defaults (`UI_SELECT`…) are added to
  every game.
- **Control map** (`ControlMap`, built by `ControlMapBuilder`): matches LEDBlinky. A digital
  control's MAME input codes (from LEDBlinky's MAME defaults, or MAME's `cfg/default.cfg` when
  LEDBlinky's MAME folder has one) are matched against the codes LEDBlinky recorded for each LED
  port in `LEDBlinkyInputMap.xml`; the port is followed through LED-Next's wiring to a button.
  Anything unmatched falls back to the naming convention (`P1_BUTTON3` → `P1_B3`,
  `P1_TRACKBALL` → the Trackball-kind control). Joysticks/analog controls match by name only.
  The map depends on the wiring: re-import after re-wiring.
- **Colours**: LEDBlinky intensities are 0–48; they are scaled to 0–255 on import so brightness
  and gamma apply as usual. Names come from `Color-RGB.ini` (built-in copy if missing).
- An import replaces the imported tables only. **User edits** live in `user_profiles` /
  `user_lights` (schema 2) and are never touched by an import. A customized profile is a full
  replacement for that level (a game, or a platform default), so it wins over the imported data at
  that level and still inherits like any other (a customized platform default reaches every game
  without its own lights). An empty customized profile means "all off", not "inherit".
- **Editing** (`GameLightEditor`): the first edit copies what the panel shows now (`Materialize`,
  leaving out controls that only come from the platform's always-lit set), then applies the change.
  Clicking a panel button edits the control that lights it, else the control its name or the
  control map points to (`ControlForButton`: `P1_B3` → `P1_BUTTON3`; with MAME's built-in keys
  `P1_B8` → `P1_BUTTON7`; mahjong/UI keys that share a button are never picked).
- **Bulk paste** (`LightClipboard`, `GameLightEditor.Paste`): copies are in logical controls, so they
  mean the same buttons in every game. A whole-layout copy replaces each target's lit controls but
  keeps the target's own labels; a one-button copy changes only that control (including "off").
  Each target starts from its own customized profile or a materialized copy of what it shows, and
  all targets are written in one transaction (`SaveUserLightsMany`).
- **Templates** (`LightTemplate`, tables `templates` / `template_lights`): a named whole layout in
  logical controls, colours only (no game labels). Applying one is a whole-layout paste.
  `GameLightResolver.ResolveProfile` previews it with no inheritance. Starters live in `BuiltIns`
  and are seeded once (meta `seeded_builtins`), so deleting one is permanent.
- **Smart lists** (`GameFilter`, table `smart_lists` as JSON): a saved filter, evaluated when opened.
  Filters use `games.buttons` (highest `Pn_BUTTONm` lit by the game or its parent, else Controls.ini
  `P1NumButtons`) and `games.tags` (`ControlTags`: from lit controls and the control description),
  both computed on import. Schema 3 added these columns; an older file gets them empty until re-import.
- **Button banners** (`ButtonBanners`, Games → "Button banners…", `GameSettings.ButtonBannersFolder`,
  config v5, table `button_banners`, schema 7): "6 button layout" pictures, `<rom>.png` (800 x 480, two rows
  of three buttons, used ones in colour, names under them). The app decodes them (`ButtonBannerReader`,
  WPF) on a worker thread when the folder is chosen and at start-up when the folder changed (meta
  `banner_stamp`); Core only sees pixels. The picture is shown next to the panel (the game's, else its
  parent's). A MAME (or MAME-data) game whose lights come from a platform default lights buttons
  1..n for each player (n = buttons the banner uses, its own or its parent's) in the default-active
  colour, keeping the rest of the default (stick, always-active). Only the count is used: the pictures
  place functions on a 6-button panel, not by MAME button number (JoJo's 4th button is bottom-left).
  Games with lights of their own and customized platform defaults are left alone; such games still count
  as "needs setup".
- **LaunchBox pictures** (`LaunchBoxImages`, read only): under the banner the Games tab shows the game's
  "Arcade - Control Panel" and "Arcade - Controls Information" pictures from the LaunchBox folder, for setting
  colours by hand. Folder per platform and media type from `Data\Platforms.xml` (else
  `Images\<platform>\<type>`), region subfolders included; files are matched to the LaunchBox title by letters
  and digits only (LaunchBox replaces `:` `'`… in file names; `-01` and a `.<guid>` are dropped). Titles come from
  `launchbox_games` (the game's, else its parent's; a MAME rom also through MAME-data platforms such as Neo Geo).
  Listings are cached per folder and dropped when LaunchBox is read again. Clicking a picture opens it in
  Windows' viewer.

## LaunchBox (phase 4a)

- `LaunchBoxLibrary` (Core, read only) reads `<LaunchBox>\Data\Platforms\*.xml`: one `<Game>` per game
  with Title, Platform, ApplicationPath, DateAdded, Genre. Checked against the cabinet's LaunchBox 14.1
  (41,633 games). A game whose ApplicationPath is an emulator exe (any in `Data\Emulators.xml`, plus a
  built-in list: `mame.exe`, `retroarch.exe`…) has no rom name and is matched by title.
- `PlatformMatcher`: your mapping (`cabinet.json` → `games.launchBoxPlatformMap`, config v3) → same name
  ignoring spaces/underscores/case → alias (`Arcade` → MAME…) → a new platform key.
  `GameMatcher`: ROM file name (`sf2.zip` → `sf2`) → title → new.
- `GameDatabase.ImportLaunchBox` stores the matches in `launchbox_games` (schema 4) and adds missing
  games/platforms to the library (new platforms inherit the MAME default, like LEDBlinky's
  `UseMameDefaultForOtherEmulators`). This merge also runs after every LEDBlinky import.
  - New platforms for arcade sets with MAME rom names (`PlatformMatcher.UsesMameDataByDefault`: Cave,
    ZiNc, Final Burn Neo/Alpha) use MAME's game data; libraries made before this get upgraded on the
    next merge (only platforms made from LaunchBox, never one you mapped yourself).
  - LaunchBox-only games lit from MAME's data take the MAME game's button count, control tags and
    players (`FillFromMameData`), so button/control smart lists find Neo Geo MVS, Arcade Clones, Cave….
- Filters: `AddedWithinDays` (newest first), `InLaunchBoxOnly`, `NeedsSetupOnly` (lighting from the
  platform default only and not customized), `Genre` (part of LaunchBox's genre; a parent ROM matches
  through its clones' LaunchBox entries). Schema 5 added `launchbox_games.genre`.
- Templates can be tried on the selected game (Templates tab → Try on selected game): the same result as
  applying, shown on the panel, not saved.

## Live lighting from LaunchBox / BigBox (phase 4b)

```
LaunchBox / BigBox process                      LED-Next app (owns the boards)
┌──────────────────────────────┐   named pipe   ┌──────────────────────────────────────────────┐
│ LedNext.LaunchBox plugin     │  "LedNext.     │ FrontEndServer (Core) ─► FrontEndController  │
│  ISystemEventsPlugin         │   FrontEnd"    │   FrontEndSession: select / launch / exit /  │
│  IGameLaunchingPlugin        │  JSON lines ─► │   closing → what to light                    │
│  SelectionWatcher (poll)     │                │   GameDatabase.FindFrontEndGame (LaunchBox id│
│  LedNextThemeElement (BigBox)│                │   → title → match now) → GameLightResolver   │
│  FrontEndClient (bg thread)  │                │   → GameLights → engine                      │
└──────────────────────────────┘                └──────────────────────────────────────────────┘
```

- **Plugin** (thin, never throws into LaunchBox): start-up → connect, or start LED-Next with `--minimized`
  (path in `LedNext.LaunchBox.json` next to the DLL); game launching/exited; selection from LaunchBox 14.1's
  `SelectionChanged` system event (works in LaunchBox and BigBox), backed by a 150 ms `StateManager` poll and
  the optional BigBox theme element (`LedNextThemeElement`). All three share one de-duplicated send. With no
  game selected (BigBox platform wheel) the plugin sends the selected platform (`GetSelectedPlatform()`).
  Logs to `LedNext.LaunchBox.log` in its folder.
- **Client**: background thread; a new "select" replaces one not sent yet (fast scrolling sends the last
  game only); while LED-Next is away it keeps the newest "started" and "select" and retries every 2 s, so
  it connects as soon as LED-Next listens. Each message carries `sentAtMs` for the timing log.
- **Session rules**: select → that game; a select with only a platform → that platform's default lights
  (`GameDatabase.FindFrontEndPlatform`: where its LaunchBox games were matched, else the platform matcher;
  unknown platform → unchanged); launch → that game (selections ignored while playing); exit → the
  selection again (game or platform); front end closing → all off; a select with neither (a playlist)
  keeps the lights. The last front-end event and the user's own clicks in the app (test patterns, Games tab) take
  turns: whichever came last is shown.
- **Single instance**: a named mutex; a second LED-Next sends `show` over the pipe (the first one comes
  to the front) and exits before touching the boards. The pipe is created with `FirstPipeInstance`.
- **Timing**: the engine renders a new source at once (it doesn't wait for the next frame tick) and raises
  `SourceShown` when its first frame is on the boards. LED-Next logs one line per front-end change:
  `Timing '<game>': pipe, wait (UI thread), lookup (game + lights), boards (write); total`.
- **Idle cost**: while minimized the panel/boards previews aren't redrawn (`MainViewModel.PreviewVisible`);
  the engine writes boards only when a value changes. Measured on the dev PC: ~0 % CPU, ~146 MB.

## Live lamps from emulators (phase 5, first part)

```
MAME / Supermodel / Output Blaster ──Windows messages──► MameWindowOutputs ┐
MAME (output network) / Flycast ──TCP 8000 lines───────► NetworkOutputs    ┴► LampController (App, UI thread)
                                                            MameOutputMap (LEDBlinky MameOutputs.ini) ──► MameOutputSession
                                                            LampOverlay(game lights + lamps) ──► engine (Refresh = render now)
```

- **Windows messages** (`Lamps/MameWindowOutputs`, MAME's `win32_output.h`): a hidden *top-level* window (MAME's
  start/stop are broadcasts, which message-only windows don't get). On "MAMEOutputStart" (or a "MAMEOutput" window
  already open) it registers, asks for id 0 (the rom) and each new output id's name ("MAMEOutputGetIDString",
  answered as WM_COPYDATA, `dwData` 1, `{uint id; char name[]}`), and turns "MAMEOutputUpdateState" (id, value)
  into named outputs. Id 1 is "pause" and is ignored. Asks wait at most 500 ms (`SMTO_ABORTIFHUNG`); a rom MAME
  was too busy to name is asked again on the next update, and updates wait for it.
- **Network** (`Lamps/NetworkOutputs`, MAME's `network.cpp`): TCP to `[::1]` then `127.0.0.1` port 8000 every 2 s
  while nothing listens; lines `name = value` read by `OutputLineParser` (Core, tested), two dialects. MAME:
  `mame_start = rom` first, decimal values, CR endings, `mame_stop = 1` at the end. Flycast (`core/network/output.h`,
  IPv4 only): `game = <id>` before the first value (Naomi's name, e.g. "SEGA MARINE FISHING JAPAN", not a MAME rom),
  **hexadecimal** values, LF endings, no stop message (the game ended when the connection closes).
- **MAME needs `output windows` (or `network`) in mame.ini.** `auto`, the default, picks MAME's "none" module, so
  nothing is sent (LEDBlinky's `UpdateMameIniOutput` sets it). LED-Next never edits emulator files.
- **Mapping** (`MameOutputMap`): LEDBlinky's `MameOutputs.ini` in the LEDBlinky folder (re-read for every game):
  `[rom]` (or a parent's), else `[default]`; control names only (`LED0=Start1` → `P1_START`); hardware ports,
  Pixelcade and web requests are skipped. No file → LED0 → P1_START, LED1 → P2_START.
- **Session** (`MameOutputSession`, pure): 0 = off, anything else = on; a control is on while any of its outputs
  is. **Overlay** (`LampOverlay`): wraps what the panel shows (the front end's playing game; else the rom's own
  lights from the MAME library when the shown game isn't that rom). An on lamp shows the game's colour for that
  button, else white; off = dark. `lamps.startLampsOnButton1` (default on, for Start buttons without LEDs): while
  a player's Start lamp is blinking (changed in the last 1.5 s) that player's button 1 blinks with it, then shows
  the game's colour again. When the game ends the previous lights come back, unless something replaced them.
  The log lists each output a game reports once, with what it drives (like LEDBlinky's MAMEOutputTest), and
  all outputs at the end.
- Settings: `cabinet.json` `lamps` (config v6): `enabled`, `windowMessages`, `network`, `networkPort`,
  `startLampsOnButton1`. Status bar: "Lamps: galaga · 1 on".

## Window placement
`config/window.json` (per machine, not in cabinet.json) keeps the main window's position. On load the
window is moved/shrunk into the work area of the nearest monitor, in physical pixels
(`WindowPlacement.Clamp` + `MonitorFromRect`), so scaling and several monitors are handled.

## Lighting engine (`LedEngine`)

```
ILightSource.Render(ctx)  ──►  button colors + raw pin overrides
          │
          ▼
CompiledMapping (immutable snapshot of panel + wiring, swapped atomically on config change)
          │  brightness + gamma
          ▼
per-board byte[PinCount]  ──►  write only if changed  ──►  ILedBoard.Write
          │
          └──► FrameRendered event (UI preview, throttled on the UI side)
```

- One background thread, `RefreshHz` (default 30) frames per second.
- The UI never touches boards. It changes the `ILightSource` or calls `ApplyConfig`.
- Raw pin overrides (pin walk, manual pins) bypass wiring, gamma and brightness — they are for
  finding out how the panel is physically wired.
- On stop, all boards are written to zero.

## Hardware layer

- `ILedBoard` / `ILedBoardProvider` are defined in Core; drivers implement them.
- `PacLed64Provider` loads `PacDrive64.dll` (or `PacDrive32.dll` in a 32-bit process) from the
  configured path or the exe folder, enumerates devices and keeps those with
  VID `0xD209`, PID `0x1401`–`0x1404` (board ID 1–4).
- `SimulatedBoardProvider` creates a virtual board for every board in the wiring config, so wiring
  designed on a dev PC carries over unchanged to the cabinet.

## Editing support

- **Undo/redo** (`UndoHistory` in Core, used by `AppSession`): snapshot-based. Before any panel or
  wiring change the app calls `AppSession.Checkpoint(key)`, which stores the serialized
  panel + wiring. Same key within 1.5 s merges (typing a label = one step). Output and hardware
  settings are excluded on purpose. Undo is disabled while the wizard runs; the whole wizard run
  is one undo step afterwards.
- **Wiring wizard** (`WiringWizard` in Core, `WizardViewModel` in the app): walks board pins in
  order, lights the current pin with the `SinglePin` source, and binds it to the selected button
  and the pressed channel. Back restores whatever that pin and channel were bound to before.
- **Panel photo**: copied into the config folder as `panel-background.<ext>` and stored by file
  name, so the config folder stays portable.

## Threading summary
- UI thread: view models, config edits, autosave (debounced 1 s).
- Engine thread: rendering and all native calls.
- `ReplaceBoards` takes the output lock, so rescans never overlap a write.
