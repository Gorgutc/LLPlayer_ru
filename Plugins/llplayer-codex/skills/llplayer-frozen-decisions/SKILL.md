---
name: llplayer-frozen-decisions
description: Use when a change might alter LLPlayer_ru stack, packaging, runtime assets, or verification policy.
---

# LLPlayer Frozen Decisions

Treat these as protected unless the user explicitly changes them:

- The shipped Windows WPF app targets `.NET 10` and remains supported until explicit replacement acceptance.
- F-13 adds the current Linux `LLPlayer.Avalonia` host, shared `LLPlayer.Core`, and portable FlyleafLib `net10.0` alongside the WPF Windows target.
- The owner-confirmed target is the same Avalonia UI/Core on native Windows and Linux. The proposed Windows Avalonia baseline uses the portable engine; it is not implemented or accepted. See `docs/agent/architecture.md` and `docs/agent/f13-status-2026-10-18.md`.
- Keep the WPF app, Avalonia host, shared Core, media engine, and `Plugins/YoutubeDL` plugin boundaries explicit.
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
- Windows release packaging is defined by `.github/actions/build-package/action.yml`; Linux packaging uses `scripts/linux/publish.sh` and `.github/workflows/build-linux.yml`.
- Windows PowerShell/.NET gates remain required for the WPF product; portable/Core/Avalonia and Linux runtime changes also require `scripts/linux/verify.sh`.
- No global `package.json` or web quality stack.

Run `scripts/codex/verify-frozen.ps1` after touching these surfaces.
