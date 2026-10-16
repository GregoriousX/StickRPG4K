# StickRPG4K — continuation state

Current implementation: **1.4.0**, 16 October 2026. See `DEVELOPMENT.md` first.
The continuation archive's `CONTINUATION.json` records its exact Git commit.

- Windows portable mod; every runtime resource, generated game, setting and save
  lives beside the user's own original EXE. Never restore absolute machine paths.
- Steam's normal game EXE becomes our launcher; the original remains `stick.exe`.
  The launcher waits for Ruffle and inherits Steam's environment.
- 800×450 widescreen gameplay, original menu/minigame proportions, prebuilt 4K UI.
  Logic stays at 30 FPS; 60 targets interpolation, 120 is experimental (~100 updates/s).
- 22 music excerpts use matching local Steam Soundtrack MP3 frames without re-encoding.
  Piano/casino keep originals. Missing album files preserve existing valid HQ audio
  during upgrades; otherwise the game retains embedded music.
- Escape opens/closes the in-game phone without leaving fullscreen. A scoped native
  launcher hook maps real Escape to F13; the SWF handles that menu key.
- Music/SFX volume sliders reuse original QualitySlider artwork and persist separately
  from scene attenuation. Quality defaults to Ultra. Saves/load/Continue use the
  original local format without account prompts. Account service callbacks are disabled.
- Manifest data version: `wide4k-offline-ui-2026-10-16`. Installer upgrades only recognized
  generated files; unknown files and edited data block replacement. Saves are separate.
- Original EXE SHA-256: `2bde20d37e45ea5ac1d517d55a89241250718f213f615982171aad28b9b56a06`.
  Steam app 307640, tested original build 297016. Ruffle nightly 2026-10-09.
- GitHub: `GregoriousX/StickRPG4K`, must remain private. No original EXE, complete
  game code, soundtrack, saves or machine-specific settings may be published.
- Completed checks: 21 menu/offline/audio checks in hidden Ruffle, 21 native Escape
  routing/lifecycle checks, 71 portable launcher checks, full fresh install and 1.3.1
  upgrade with preservation of settings/saves/HQ audio, all 75 game-file hashes.
  22 album clips were independently decoded and played to completion previously.
- Native key routing was tested without injecting input into the desktop; SWF menu
  tests dispatch events internally. Visual QA rendered the control using BitmapData.draw.
- Known Ruffle details: volume rounds to whole percentage points; loading PlayerData
  logs a benign read-only HEALTH_BAR_WIDTH warning while restoring the save correctly.
  Some original indoor music EQ remains unavailable. Steam playtime accumulation and
  achievements/overlay were not empirically verified.

The previous machine had its installed Steam game at `D:\SteamLibrary\steamapps\common\Stick RPG 2`.
That path is historical context only. The latest update preserved its existing save.
The long historical handoff and compact validation reports are in the transfer archive,
not required as build inputs. No original workstation analysis folders are needed.
