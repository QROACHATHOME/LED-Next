# Import samples

Read-only copies of real data used to build and test importers. Never edit these in place;
the importers must treat them as input only.

## `ledblinky/`
Copy from the LEDBlinky install folder on the arcade machine (names vary by version; copy what
exists and list it in `TASKS/CABINET_RESULTS.md`):

- Controls / colours data: `LEDBlinkyControls.xml` or `LEDBlinkyControlsFile.xml` (per-game controls and colours)
- `Colors.ini` (named colour definitions)
- Input map: `LEDBlinkyInputMap.xml` (which LED port is which control)
- Main settings: `LEDBlinky.ini` / any `*Config*.xml`
- A **few** animation files (`*.lwax`) — not the whole collection
- Any per-emulator or per-front-end folders that hold colour profiles (a sample, not everything)

If something is very large (> ~20 MB), copy a representative subset and note it in the results.
