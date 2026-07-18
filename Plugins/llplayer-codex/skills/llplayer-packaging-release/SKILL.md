---
name: llplayer-packaging-release
description: Use when changing LLPlayer_ru publish profiles, GitHub release workflows, or Windows exe packaging.
---

# LLPlayer Packaging Release

Release packaging source of truth is `.github/actions/build-package/action.yml`.
Use `scripts/codex/ship.ps1` as the local offline packaging smoke; keep it in sync with the action.

## Preserve Flow

1. Dispatch the trusted workflow from the default branch with an exact lowercase 40-character commit SHA that equals the workflow run's trusted `${{ github.sha }}`; Stable also requires a strict `vMAJOR.MINOR.PATCH` tag.
2. Fail closed unless the dispatch input, workflow control SHA, run `head_sha`, and selected commit are the same immutable commit; resolve it while the job still has only `contents: read`.
3. Use the immutable `setup-dotnet` v5.4.0 action to install the repository's `10.0.x` SDK channel.
4. Run the canonical full `scripts/codex/verify.ps1` preflight with no skip switch.
5. Invoke the shared packaging action only after the preflight succeeds.
6. Restore and publish `LLPlayer`.
7. Clean unused Whisper/Tesseract runtime folders.
8. Copy `FFmpeg`.
9. Restore and publish `Plugins/YoutubeDL`.
10. Copy `YoutubeDL.dll` and `YoutubeDL.pdb`.
11. Download `yt-dlp.exe` in CI release only and retain its resolved version, size, and SHA-256 as evidence.
12. Archive with 7-Zip, run `7z t`, and retain archive size and SHA-256.
13. Transfer the archive through a trusted read-only verification job before any job receives `contents: write`.

Keep the full gate in both Stable and Testing caller workflows, not only in the local composite action. The selected
commit is deliberately bound to the trusted default-branch workflow commit, and a missing canonical verifier or
preflight-order regression must fail closed before packaging. The composite action remains the source of truth for
the publish/cleanup/download/archive tail and repeats the same immutable `setup-dotnet` setup so it stays self-contained.

Both workflows use four isolated jobs: trusted `prepare`, selected-code `build`, trusted `verify`, and narrow
write-only publication. The write job must not checkout repository code, call local actions, parse or execute the
archive, or accept an unverified artifact. Stable creates a new immutable version tag only after verification and
creates a draft Release; it never publishes. Testing uses a non-SemVer per-commit `testing-<12sha>` tag and draft
prerelease, and may clobber only the exact expected asset when the existing tag still points directly to the same
commit and the Release is still a draft. Never force, move, or delete either tag from a release workflow.

Publish steps must keep warnings fatal (`/warnaserror`) for both app and `Plugins/YoutubeDL`.
The package must include committed runtime source/assets (`FFmpeg`, `LLPlayer/lib/7z.dll` and its license,
`LLPlayer/Assets/silero_vad.onnx`, `onnxruntime.dll` plus the ONNX provider, SQLite, Tesseract, and Whisper native DLLs,
`dub_sidecar/` source including `uv.lock`)
and must reject generated/runtime data (`DubEngine/`, `dubmodels/`,
`*.ru.dub.*`, `*.ru.voices.json`, downloaded local `yt-dlp.exe`).

Local Codex verification should stay offline unless explicitly shipping. Do not push a Stable release tag, dispatch
Testing Release, or upload/overwrite release assets without separate explicit owner approval.

## Review

For packaging changes, run `scripts/codex/ship.ps1` when feasible and spawn `packaging_release_reviewer`.
Before final handoff, satisfy `/review` with a spawned review subagent; if the tool is unavailable, say so.
