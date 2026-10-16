# StickRPG4K

Stick RPG 2 mod for Windows x64. **Your original Steam / Director's Cut EXE is required.**
Original game code is extracted locally; this repository contains patches,
ready-made upscaled assets and the Ruffle runtime.

## Setup and play

1. Download and extract the ZIP, or clone this repository.
2. Open **config.exe**. It finds your Steam copy; use **Choose EXE...** if needed.
3. Choose motion smoothness and display mode, then click **Save & install**.
4. Start **Stick RPG 2** normally from Steam. Choose **Play Offline** in the game.

Setup extracts your original EXE locally and builds the mod in `game/`. It renames
the original to **stick.exe** and puts our launcher under Steam's expected name,
**Stick RPG 2 Director's Cut.exe**. That launcher starts Ruffle with your saved
settings and waits until the game closes, keeping Steam's launched process alive
for playtime tracking. No Steam launch options are needed.

Open **config.exe** in the mod or game folder to change settings. **Restore original
EXE** switches Steam back to the original game. Steam file verification can also
replace the launcher; run config.exe again to reinstall it.

Keep this mod folder in place after setup. Saves stay in `saves/`; reinstalling or
restoring the EXE preserves them. **StickRPG4K.exe** also starts the mod directly
and opens setup on its first run. Press **Alt+Enter** to toggle fullscreen.
Tested with Steam app 307640, build 297016. Windows .NET Framework 4.8 is required;
Python and upscaling tools are not needed.

## Features

- 16:9 gameplay without stretching; menus and minigames keep their original aspect ratio.
- Upscaled UI and textures up to 4096 pixels, supplied prebuilt.
- Motion interpolation targeting 60 FPS; game logic stays at 30 FPS.
- Fixed Ruffle music playback, looping, fades and music controls.

120 mode is experimental and limited to about 100 interpolation updates/s by
the bundled Ruffle build. Some rooms' original music filtering is unavailable.
Original EXEs, generated game files, saves and caches are excluded from Git.

## Credits

Original game: XGen Studios. Based on [StickRPG2-HD](https://github.com/rebane2001/StickRPG2-HD).
Runtime: [Ruffle](https://github.com/ruffle-rs/ruffle), nightly 2026-10-09
([license](runtime/LICENSE.md)). UI upscaling: [Upscayl](https://github.com/upscayl/upscayl).
