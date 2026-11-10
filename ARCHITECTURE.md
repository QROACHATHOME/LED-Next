# Architecture

## Projects

| Project | Depends on | Role |
|---|---|---|
| `src/LedNext.Core` | nothing | Data model (panel, wiring, config), JSON store, lighting engine, test patterns, board interfaces, logging, game model + light resolver (`Games/`), LEDBlinky importer (`Import/LedBlinky/`) |
| `src/LedNext.Data` | Core, Microsoft.Data.Sqlite | The game library in SQLite (`GameDatabase`, implements `IGameLibrary`) |
| `src/LedNext.Hardware` | Core | Board drivers: Ultimarc PacLED64 (via PacDrive SDK DLL) and a simulator |
| `src/LedNext.App` | Core, Data, Hardware | WPF desktop app: panel designer, games browser, Nodify wiring graph, board test view |
| `tests/LedNext.Tests` | Core, Data | xUnit tests; the importer tests run on the real files in `import-samples/` and `cabinet/` |

Planned (later phases): `LedNext.Service` (background host that owns the hardware),
`LedNext.LaunchBox` (thin plugin talking to the service over a named pipe).

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
