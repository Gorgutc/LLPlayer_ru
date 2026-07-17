---
name: llplayer-packaging-release
description: Use when changing LLPlayer_ru publish profiles, GitHub release workflows, or Windows exe packaging.
---

# LLPlayer Packaging Release

Release packaging source of truth is `.github/actions/build-package/action.yml`.
Use `scripts/codex/ship.ps1` as the local offline packaging smoke; keep it in sync with the action.

## Preserve Flow

1. Checkout the exact tag or immutable selected commit.
2. Use the immutable `setup-dotnet` v5.4.0 action to install the repository's `10.0.x` SDK channel.
3. Run the canonical full `scripts/codex/verify.ps1` preflight with no skip switch.
4. Invoke the shared packaging action only after the preflight succeeds.
5. Restore and publish `LLPlayer`.
6. Clean unused Whisper/Tesseract runtime folders.
7. Copy `FFmpeg`.
8. Restore and publish `Plugins/YoutubeDL`.
9. Copy `YoutubeDL.dll` and `YoutubeDL.pdb`.
10. Download `yt-dlp.exe` in CI release only.
11. Archive with 7-Zip.

Keep the full gate in both Stable and Testing caller workflows, not only in the local composite action: Testing can
select an older commit whose copy of `.github/actions/build-package/action.yml` predates the preflight. A missing
canonical verifier must fail closed before packaging. The composite action remains the source of truth for the
publish/cleanup/download/archive tail and repeats the same immutable `setup-dotnet` setup so it stays self-contained.

Publish steps must keep warnings fatal (`/warnaserror`) for both app and `Plugins/YoutubeDL`.
The package must include committed runtime source/assets (`FFmpeg`, `LLPlayer/lib/7z.dll`,
`LLPlayer/Assets/silero_vad.onnx`, `onnxruntime.dll`, `dub_sidecar/` source including `uv.lock`)
and must reject generated/runtime data (`DubEngine/`, `dubmodels/`,
`*.ru.dub.*`, `*.ru.voices.json`, downloaded local `yt-dlp.exe`).

Local Codex verification should stay offline unless explicitly shipping. Do not push a Stable release tag, dispatch
Testing Release, or upload/overwrite release assets without separate explicit owner approval.

## Review

For packaging changes, run `scripts/codex/ship.ps1` when feasible and spawn `packaging_release_reviewer`.
Before final handoff, satisfy `/review` with a spawned review subagent; if the tool is unavailable, say so.
