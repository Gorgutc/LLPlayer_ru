---
name: llplayer-frozen-decisions
description: Use when a change might alter LLPlayer_ru stack, packaging, runtime assets, or verification policy.
---

# LLPlayer Frozen Decisions

Treat these as protected unless the user explicitly changes them:

- Windows-only WPF app targeting `.NET 10`.
- `LLPlayer` app + `FlyleafLib` media core + `Plugins/YoutubeDL` plugin split.
- Product behavior, WPF design, media runtime, config/data, dependencies, manual smoke expectations, and review ownership documented in:
  - `docs/agent/product-behavior-contract.md`
  - `docs/agent/wpf-design-contract.md`
  - `docs/agent/media-runtime-contract.md`
  - `docs/agent/config-data-contract.md`
  - `docs/agent/dependency-baseline.md`
  - `docs/agent/manual-smoke-matrix.md`
  - `docs/agent/subagent-review-matrix.md`
  - `docs/agent/dubbing-contract.md`
- FFmpeg DLLs and `LLPlayer/lib/7z.dll` are intentional tracked native assets.
- Release packaging is defined by `.github/actions/build-package/action.yml`.
- Codex gates are PowerShell/.NET-first.
- No global `package.json` or web quality stack.

Run `scripts/codex/verify-frozen.ps1` after touching these surfaces.
