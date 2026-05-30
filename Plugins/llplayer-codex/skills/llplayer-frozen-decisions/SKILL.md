---
name: llplayer-frozen-decisions
description: Use when a change might alter LLPlayer_ru stack, packaging, runtime assets, or verification policy.
---

# LLPlayer Frozen Decisions

Treat these as protected unless the user explicitly changes them:

- Windows-only WPF app targeting `.NET 10`.
- `LLPlayer` app + `FlyleafLib` media core + `Plugins/YoutubeDL` plugin split.
- FFmpeg DLLs and `LLPlayer/lib/7z.dll` are intentional tracked native assets.
- Release packaging is defined by `.github/actions/build-package/action.yml`.
- Codex gates are PowerShell/.NET-first.
- No global `package.json` or web quality stack.

Run `scripts/codex/verify-frozen.ps1` after touching these surfaces.
