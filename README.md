# StickRPG4K

Stick RPG 2 mod for Windows x64. **Your original Steam / Director's Cut EXE is required.**
Original game code is extracted locally; this repository contains patches,
ready-made upscaled assets and the Ruffle runtime.

## Setup and play

1. Download the ZIP from [Releases](https://github.com/GregoriousX/StickRPG4K/releases/latest).
2. Extract **all ZIP contents directly into your game folder**, beside the original EXE.
3. Open **config.exe**. It detects compatible EXEs in that folder; use **Choose EXE...** if needed.
4. Choose motion smoothness and display mode, then click **Save & install**.
5. Start the game normally, through Steam, or with **StickRPG4K.exe**. Saves work locally without an account.

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
Press **Esc** to open or close the game menu; it keeps fullscreen active.
Tested with Steam app 307640, build 297016. Windows .NET Framework 4.8 is required;
Python and upscaling tools are not needed.

Upgrading an earlier install: extract this ZIP into the game folder and run
config.exe again. The original `stick.exe` backup is reused.

## Features

- 16:9 gameplay without stretching; menus and minigames keep their original aspect ratio.
- AI-upscaled icons, interface and backgrounds (up to 4x, max 4096 px), made from the original game's art and supplied prebuilt. Untick **AI-upscaled icons, interface and backgrounds** in config.exe to keep the original bitmaps.
- Hand-remade textures from StickRPG2-HD by rebane2001.
- Motion interpolation targeting 60 FPS; game logic stays at 30 FPS.
- Fixed Ruffle music playback, looping, fades and music controls.
- Higher-quality Steam album excerpts for 22 music clips, without re-encoding.
- Independent music and sound effects sliders, saved between sessions.
- Local saves without XGen account or login prompts; Ultra quality by default.
- Working street fights: the combat engine's missing settings file is installed, shown 1:1 and smooth.

Keep Steam's original `Soundtrack/` folder beside the EXE when running config.exe.
Setup uses its matching MP3 files to replace the corresponding embedded music,
preserving each clip's length, loops and time-of-day transitions. Album files are
not included in the mod. Missing or unrecognized album files keep the embedded
audio; casino and piano clips retain their original versions. Run config.exe and
click **Save & install** after adding the soundtrack to an existing installation.

120 mode is experimental and limited to about 100 interpolation updates/s by
the bundled Ruffle build. Some rooms' original music filtering is unavailable.
Original EXEs, generated game files, saves and caches are excluded from Git.

Development and transfer instructions: [development guide](https://github.com/GregoriousX/StickRPG4K/blob/main/docs/DEVELOPMENT.md).

## Credits

Original game: XGen Studios. Based on [StickRPG2-HD](https://github.com/rebane2001/StickRPG2-HD).
Runtime: [Ruffle](https://github.com/ruffle-rs/ruffle), nightly 2026-10-16
([license](runtime/LICENSE.md)). Upscaling: [Real-ESRGAN](https://github.com/xinntao/Real-ESRGAN) with Remacri and x4plus-anime models
([Upscayl](https://github.com/upscayl/upscayl) model collection).
