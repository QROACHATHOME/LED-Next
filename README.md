# LED-Next

A modern replacement for LEDBlinky: visual arcade panel layout, node-graph LED wiring,
multiple LED boards per PC, and (coming) per-game lighting for LaunchBox/BigBox.

## Build and run
```
dotnet build LedNext.sln
dotnet run --project src/LedNext.App
dotnet test
```

The app starts in **Simulator** mode, so it works without boards attached. Switch to
**Hardware** mode on the cabinet. See `TASKS/00_ACTIVE_PRIORITIES.md` for hardware setup.

Config is stored in `config/cabinet.json` next to the exe; logs in `logs/led-next.log`.
