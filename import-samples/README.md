# Import samples

Read-only copies of real data used to build and test importers. Never edit these in place;
the importers must treat them as input only.

**Not in the repository.** `ledblinky/` and `launchbox/` are git-ignored: LEDBlinky's files are not ours to
publish, and a LaunchBox library is personal data. Each machine copies its own as described below. The tests
that need LEDBlinky's files (`[LedBlinkySamplesFact]`) are skipped without them; with them, they run as before.
Only `mame/cfg/default.cfg` (the cabinet's MAME key settings) is kept in the repository.

They were in the repository until 2026-11-12. To get them back on a machine that had them, run
`git restore --source=4a3a776 -- import-samples/ledblinky import-samples/launchbox`. They then come back as
ignored local files.

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
