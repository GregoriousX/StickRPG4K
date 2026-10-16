# StickRPG4K

Stick RPG 2 mod for Windows x64. **Your original Steam / Director's Cut EXE is required.**
Original game code is extracted locally; this repository contains patches,
ready-made upscaled assets and the Ruffle runtime.

## Setup and play

1. Download **StickRPG4K-v1.3.0.zip** from [Releases](https://github.com/GregoriousX/StickRPG4K/releases/latest).
2. Extract **all ZIP contents directly into your game folder**, beside the original EXE.
3. Open **config.exe**. It detects compatible EXEs in that folder; use **Choose EXE...** if needed.
4. Choose motion smoothness and display mode, then click **Save & install**.
5. Start the game normally, through Steam, or with **StickRPG4K.exe**. Choose **Play Offline** in the game.

Setup extracts your original EXE locally and builds the mod in `game/`. It renames
the original to **stick.exe** and puts our launcher under the original EXE's name
(**Stick RPG 2 Director's Cut.exe** for Steam). That launcher starts Ruffle with your saved
settings and waits until the game closes, keeping Steam's launched process alive
for playtime tracking. No Steam launch options are needed.

Any folder works: compatibility is checked against the EXE's SHA-256, independently
of Steam's location or registry. All assets, settings, runtime and saves stay in
the game folder. Stored paths are relative, so the folder can be moved together.

Open **config.exe** to change settings. **Restore original
EXE** switches Steam back to the original game. Steam file verification can also
replace the launcher; run config.exe again to reinstall it.

Saves stay in `saves/`; reinstalling or restoring the EXE preserves them.
**StickRPG4K.exe** opens setup on its first run. Press **Alt+Enter** to toggle fullscreen.
Tested with Steam app 307640, build 297016. Windows .NET Framework 4.8 is required;
Python and upscaling tools are not needed.

Upgrading an earlier install: extract this ZIP into the game folder and run
config.exe again. The original `stick.exe` backup is reused.

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
