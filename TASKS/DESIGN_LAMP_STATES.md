# Design note: emulator lamps as adapters, a lifecycle state machine and mapping tables

Status: proposal written 2026-11-13 on the cabinet, after visits 7–10. **Step 1 (the lifecycle) is built**
(`LampLifecycle`, ARCHITECTURE "Live lamps from emulators"), verified at the cabinet in visit 11. **Step 2 is
built** (2026-11-13): `Started(game, emulator, via)`, `IOutputMap` with `MameOutputMap` and `LampMapTable`
(`cabinet.json` `lamps.maps`, config v7; before MameOutputs.ini for MAME, the only map for other emulators); only
`onOff` values so far. Checked with a fake MAME and a fake Flycast, to verify at the cabinet (visit 12). Steps 3–4
aren't built yet.
Step 1 departs from this note in three places:
- **No launch timeout.** `Launching` lasts until the front end's exit, as before.
- **A selection during a game started by hand is remembered, not shown.** It's shown when the game ends.
- **A user pick no longer gets re-wrapped by the next lamp.** Before, a test pattern picked during a game got
  the lamps drawn on top of it at the next lamp change. Now it stays as it is.

## Problem
Each emulator reports lamps in its own way:

| Source | Game starts | Outputs | Game ends |
|---|---|---|---|
| MAME network (`output network`) | `mame_start = rom` | `name = value` (decimal) | `mame_stop` |
| MAME Windows messages (`output windows`) | register + `MAMEOutputStart` | id → name, then value | `MAMEOutputStop` / unregister |
| Flycast network | `game = id` | `name = value` (hex) | connection closes |
| TeknoParrot "Enable Outputs" | unknown (own channel, not found yet) | ? | ? |
| Supermodel (`Outputs = win`) | untested | MAME-style Windows messages? | ? |
| Front end (BigBox plugin) | `launch` | none | `exit` |

On top of that, game lamps are only one of several light sources. Front-end browsing, the game's base lights
and the lamps all compete for the panel.

## What exists today
- **Adapters, mostly done.** `IEmulatorOutputs` (`Started(rom, via)`, `Output(name, int)`, `Stopped(via)`), with
  `MameWindowOutputs` and `NetworkOutputs`. `OutputLineParser` reads both the MAME and Flycast dialects.
- **The lifecycle is implicit** in `LampController`'s fields: `_game`, `_via`, `_overlay`, `_before`. Ownership is
  decided by comparing references: "if the engine's source is still my overlay, put `_before` back". Front-end
  launch and exit live separately, in `FrontEndController`. The two don't know about each other, except through
  whatever `Engine.Source` happens to be.
- **The mapping is MAME-only.** `MameOutputMap` reads LEDBlinky's `MameOutputs.ini` (`[default]` LED0 → Start1,
  LED1 → Start2). Lookups go through the MAME rom chain, and the map is hard-wired to the platform "MAME"
  (`LampController.Mame`). Values are on/off only (`value != 0`).

## Proposal: three layers

### 1. Adapters (one per protocol), in App because they touch Win32 and sockets
Each adapter turns its protocol into the same events:
`GameStarted(emulator, gameId)`, `Output(name, value)`, `GameEnded`.
- Keep `IEmulatorOutputs`. Add an emulator name to `Started` (`"MAME"`, `"Flycast"`…), so the mapping layer
  knows which table to use instead of assuming MAME.
- An adapter invents no meaning. It only translates. A new emulator (Supermodel, TeknoParrot once its channel is
  known) is one new adapter class.

### 2. Lifecycle state machine, in Core (pure and unit-testable)
One small machine decides **who owns the panel**. It is fed by the front end and by every adapter:

```
            launch(game)                GameStarted(emu, id)          first mapped Output
 Browsing ─────────────────▶ Launching ─────────────────────▶ Playing ───────────────────▶ Playing+Lamps
    ▲                           │  timeout / exit                  │ GameEnded / exit            │
    └───────────────────────────┴──────────────────────────────────┴─────────────────────────────┘
```

- **States:** `Browsing` (front-end selection lights), `Launching` (the game's base lights, waiting for the
  emulator), `Playing` (base lights, emulator connected), `PlayingWithLamps` (base + `LampOverlay`).
- **Inputs:** front-end `launch` / `exit` / selection; adapter `GameStarted` / `Output` / `GameEnded`.
- **Output:** the light source the engine should show. The App applies it through `MainViewModel.SetSource`, so
  no class compares references to `Engine.Source` any more.
- **Rules it makes explicit** (each one is a unit test):
  - Selection changes during `Playing*` don't steal the panel. This is BigBox's behaviour today; keep it.
  - Front-end `exit` ends the game even if the emulator never sent a stop (Flycast closes the connection late or
    never). Lamps are cleared, never left on.
  - `GameStarted` with no front-end `launch` (an emulator started by hand): go straight to `Playing` with the rom's
    own lights, as `LampController.RomLights` does now.
  - Two adapters both report a game (MAME can be set to both network and windows): the first one owns it, the
    other is ignored. This is the `via` check today.
  - A game id that doesn't match the launched game (TeknoParrot names vs LaunchBox titles): keep the front end's
    base lights, still apply the lamps, and log the mismatch once.
- Logging: one `Lamps:` line per transition, so the cabinet logs read as a timeline.

### 3. Output mapping (data, not code), in Core
`(emulator, game chain, output name) → panel controls + how to show the value`.
- **MAME:** keep reading LEDBlinky's `MameOutputs.ini`, read-only as always. Section lookup by rom chain, as now.
- **Other emulators:** LED-Next's own tables in `cabinet.json` (a new `lamps.maps` section, a config-version bump
  with a migration), or a separate `config/lamp-maps.json`. Never write to LEDBlinky's file. Example:
  `Flycast / SEGA MARINE FISHING JAPAN / lamp0 → P1_START`.
- **Display styles:** `onOff` (today's behaviour), `blink` (already handled by `LampOverlay`'s blink window),
  and later `level` (brightness from the value, e.g. tachometer → a row of buttons). Only `onOff` and `blink` are
  needed for the first version.
- The Games tab could later show "outputs this game reported last time", from the per-game list the log
  already prints at game end, and let the user map one with a click. That's out of scope for the first version.

### Optional, later: derived "meaning" states
Things like *waiting for coin* (Start lamp blinking) or *credits in* (Start lamp steady) are not reported by any
emulator. They can be **derived** from the lamp values in layer 3 and used for panel-wide effects (blink every
coin button while a game waits for a coin). Keep this a small rule set on top of layer 3, not part of the
lifecycle machine.

## Suggested order (dev PC)
1. Core: `LampLifecycle` state machine plus its tests (the rules above). App: `LampController` and the front-end
   launch/exit go through it. Behaviour must stay the same as visit 7: Galaga and Dig Dug blink P1 Start,
   Chase H.Q. logs its genouts, nothing is left lit after exit.
2. Add the emulator name to `Started`. The mapping goes through an `IOutputMap` with the MAME implementation
   first, then LED-Next's own table for other emulators (config bump plus migration).
3. Flycast mapping, once a real Flycast connects (visit 8: Flycast Race opened no socket; check its Network Output
   setting first).
4. TeknoParrot and Supermodel adapters, once their channels are known.

## Constraints to keep
- Core stays free of WPF and native code. The state machine and the maps are Core; the adapters and
  `SetSource` are App.
- Hardware I/O stays on the engine output thread. The state machine only chooses a source and never writes to
  the boards.
- Simulator mode: lamps work without boards (the overlay renders into the simulator).
- LEDBlinky and emulator files are read-only input.
