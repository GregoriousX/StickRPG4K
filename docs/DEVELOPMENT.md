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

The refresh helper expects the current recognized HQ game and verified original
files. It previews changes by default, preserves album/embedded variants and adds
the previous version's hashes for automatic upgrades. It does not change game logic
deltas or graphic packs. For changes to original methods, use `dev/abc_patch.py`
and JPEXS, generate small COPY/INSERT deltas, and verify both SWF variants.
`dev/patch_offline_ui_reference.py` documents the existing method edits; it is a
disabled historical reference, not a build script requiring old machine folders.

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
