# Architecture

## Projects

| Project | Depends on | Role |
|---|---|---|
| `src/LedNext.Core` | nothing | Data model (panel, wiring, config), JSON store, lighting engine, test patterns, board interfaces, logging |
| `src/LedNext.Hardware` | Core | Board drivers: Ultimarc PacLED64 (via PacDrive SDK DLL) and a simulator |
| `src/LedNext.App` | Core, Hardware | WPF desktop app: panel designer, Nodify wiring graph, board test view |
| `tests/LedNext.Tests` | Core | xUnit tests for model, wiring rules and engine mapping |

Planned (later phases): `LedNext.Service` (background host that owns the hardware),
`LedNext.LaunchBox` (thin plugin talking to the service over a named pipe), `LedNext.Import.LedBlinky`.

## The three layers of data

1. **Panel layout** (`PanelLayout`, `PanelButton`) — where each control sits and what it is
   (`P1_B1`, `P2_START`…). Button IDs are the names game profiles will refer to.
2. **Wiring** (`WiringMap`) — which board pin drives each button's R, G and B channel.
   Rules: a pin is driven by at most one channel, a channel drives at most one pin.
3. **Game profiles** — phase 3. Global → platform → control template → game override.

Panel + wiring + settings are stored together in `config/cabinet.json` next to the exe
(portable, like LEDBlinky). Game data will live in SQLite.

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
