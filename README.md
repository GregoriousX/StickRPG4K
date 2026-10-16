# StickRPG4K

Stick RPG 2 mod for Windows x64. **Your original Director's Cut EXE is required.**
Original game code is extracted locally; this repository contains patches,
ready-made upscaled assets and the Ruffle runtime.

## Play

1. Download and extract the ZIP, or clone this repository.
2. Copy your original **Stick RPG 2 Director's Cut.exe** next to `Play.bat`
   (or into an `original/` folder). The installer verifies the supported build.
3. Double-click **Play.bat**, then choose **Play Offline** in the game.

The first launch briefly runs the original EXE in the background, extracts its
files and builds the mod in `game/`. No upscaling tools or Python installation
are needed. The original EXE is left unchanged. Saves are kept in `saves/`.
Press **Alt+Enter** to toggle fullscreen.

Optional modes: `Play.bat 30`, `Play.bat 60 windowed`, `Play.bat 120`.

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
