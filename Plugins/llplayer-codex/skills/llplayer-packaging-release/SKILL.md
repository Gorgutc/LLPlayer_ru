---
name: llplayer-packaging-release
description: Use when changing LLPlayer_ru publish profiles, GitHub release workflows, or Windows exe packaging.
---

# LLPlayer Packaging Release

Release packaging source of truth is `.github/actions/build-package/action.yml`.
Use `scripts/codex/ship.ps1` as the local offline packaging smoke; keep it in sync with the action.

## Preserve Flow

1. Setup .NET 10.
2. Restore and publish `LLPlayer`.
3. Clean unused Whisper/Tesseract runtime folders.
4. Copy `FFmpeg`.
5. Restore and publish `Plugins/YoutubeDL`.
6. Copy `YoutubeDL.dll` and `YoutubeDL.pdb`.
7. Download `yt-dlp.exe` in CI release only.
8. Archive with 7-Zip.

Publish steps must keep warnings fatal (`/warnaserror`) for both app and `Plugins/YoutubeDL`.
The package must include committed runtime source/assets (`FFmpeg`, `LLPlayer/lib/7z.dll`, `dub_sidecar/`
source including `uv.lock`) and must reject generated/runtime data (`DubEngine/`, `dubmodels/`,
`*.ru.dub.*`, `*.ru.voices.json`, downloaded local `yt-dlp.exe`).

Local Codex verification should stay offline unless explicitly shipping.

## Review

For packaging changes, run `scripts/codex/ship.ps1` when feasible and spawn `packaging_release_reviewer`.
Before final handoff, satisfy `/review` with a spawned review subagent; if the tool is unavailable, say so.
