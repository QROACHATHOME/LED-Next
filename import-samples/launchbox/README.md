# LaunchBox samples

Read-only copies from the arcade machine's LaunchBox (`D:\LaunchBox`), used to test the LaunchBox
reader against real data. The reader was written from LaunchBox's documented layout and has only
been tested with hand-made files so far.

Copy (keep LaunchBox's own folder layout so this folder can be read like a LaunchBox install):

- `Data\Platforms\Arcade.xml` (or whatever the MAME platform is called) and one or two console
  platform files, e.g. `Data\Platforms\Sega Genesis.xml`. If a file is very large (> ~20 MB), copy a
  smaller platform instead and note it in `TASKS/CABINET_RESULTS.md`.
- `Data\Platforms.xml` and `Data\Emulators.xml` (platform list and emulator setup, for the plugin).

Do **not** copy images, videos, manuals or the rest of `Data\`.
