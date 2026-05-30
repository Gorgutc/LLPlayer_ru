---
name: llplayer-packaging-release
description: Use when changing LLPlayer_ru publish profiles, GitHub release workflows, or Windows exe packaging.
---

# LLPlayer Packaging Release

Release packaging source of truth is `.github/actions/build-package/action.yml`.

## Preserve Flow

1. Setup .NET 10.
2. Restore and publish `LLPlayer`.
3. Clean unused Whisper/Tesseract runtime folders.
4. Copy `FFmpeg`.
5. Restore and publish `Plugins/YoutubeDL`.
6. Copy `YoutubeDL.dll` and `YoutubeDL.pdb`.
7. Download `yt-dlp.exe` in CI release only.
8. Archive with 7-Zip.

Local Codex verification should stay offline unless explicitly shipping.
