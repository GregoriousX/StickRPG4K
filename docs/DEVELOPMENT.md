# Continue development

Windows x64, .NET Framework 4.8, Git and Python 3.11+ are required. The continuation
archive supplies the current Git repository plus Java 17, JPEXS 26.3.0 and ECJ 3.37.0.
Run its `Restore.ps1` to create a checkout and populate ignored `dev/toolchain/`.
No credential, original game, generated SWF, soundtrack or save is in the archive.

## Prepare your own game

Copy your supported original Steam EXE into the checkout (not its `game/` folder).
Keep Steam's `Soundtrack/` beside it if you want album audio. Run `config.exe --install`
in that checkout to generate `game/`; the original becomes `stick.exe`.
The supported EXE's SHA-256 is in `patches/manifest.json`.

Preserve your real saves separately before moving between machines. The continuation
archive is for development; it does not contain the Steam installation's save data.

## C# launcher

```powershell
& .\launcher\Build.ps1
```

The build uses Windows' .NET Framework compiler; keep C# 5 compatibility.
`EscMenuBridge` maps Escape only for the current foreground Ruffle process.
Do not remove the launcher's wait for Ruffle: Steam relies on its lifetime.

## ActionScript helpers

`src/Preferences.as` owns the menu, offline behavior and SFX controls.
`src/NativeMusic.as` owns native music playback and user gain.
`src/SmoothWide.as` owns widescreen layout and interpolation.

```powershell
& .\dev\Extract-Original.ps1 -OriginalExe .\stick.exe
python .\dev\compile_helpers.py
python .\dev\refresh_helpers.py --version wide4k-your-change-YYYY-MM-DD
# Inspect dev/local/manifest-preview.json, then apply the same command:
python .\dev\refresh_helpers.py --version wide4k-your-change-YYYY-MM-DD --apply
& .\config.exe --install
```

`compile_helpers.py` first runs `dev/ruffle_playerglobal.py`, which builds the
flash.* type library JPEXS needs from the playerglobal embedded in `runtime/ruffle.exe`
(no Adobe/AIR SDK required). Unchanged sources must recompile byte-identical to
`patches/*.abc`; check that before refreshing.

The phone's Minigames.swf classes (XPhone, QualitySlider) are not visible by name
from our dat.swf module: take classes from existing instances (`x.constructor`).
Test UI with real mouse clicks; dispatched events do not catch overlapping panels.

The refresh helper expects the current recognized HQ game and verified original
files. It previews changes by default, preserves album/embedded variants and adds
the previous version's hashes for automatic upgrades. It does not change game logic
deltas or graphic packs. For changes to original methods, use `dev/abc_patch.py`
and JPEXS, generate small COPY/INSERT deltas, and verify both SWF variants.
`dev/patch_offline_ui_reference.py` documents the existing method edits; it is a
disabled historical reference, not a build script requiring old machine folders.

## Upscaled bitmaps

Every AI-upscaled bitmap is made from the original game's bitmap, never from earlier
AI output. `assets/<swf>-hd.bin` holds only rebane2001's hand-remade art (new/, proj/);
the former AI packs `-01/-02` (rebane2001 upsies and re-upscales of them) were dropped.
`dev/upscale_assets.py` (extract, upscale, build) writes `assets/<swf>-03.bin`: item
icons and scenes/backgrounds (400 px or more) with Remacri, other shape-drawn UI with
realesrgan-x4plus-anime, up to 4x and at most 4096 px. All `-03` steps are optional
(`option: upscale`); `Install.ps1 -NoUpscale` / the config checkbox keeps originals.
JPEG3 colour is premultiplied: decode/encode through `dev/swf_bitmaps.py` only. Upscalers and models live only in ignored `dev/toolchain/` (official
Real-ESRGAN ncnn release, Upscayl model repos); never commit them, only the packs.
Run `build` against the committed manifest's HQ installation. Pillow/numpy go in an
ignored venv (`dev/local/venv`). Real-ESRGAN's directory mode corrupts mixed-size
batches, so the script runs one process per image and checks every result.

All generated work belongs in ignored `dev/local/`. Never commit original EXEs,
complete game SWFs, album MP3s, saves, cache, credentials or the toolchain.
No CUA or desktop automation on the original workstation.

## Validation and packaging

Compile `tests/EscMenuTests.cs` with `launcher/EscMenuBridge.cs`, and
`tests/SoundtrackTests.cs` with `installer/Patcher.cs`, using `csc.exe`.
Give SoundtrackTests a new output directory. Inspect the current handoff for the
runtime and installation checks already completed; preserve save compatibility.

Update assembly versions in `launcher/Core.cs` and `launcher/app.manifest` for a new
release, build, then run `launcher/Package.ps1`. It packages an explicit allowlist.
The GitHub repository must remain private. Publishing a release is a separate step.
