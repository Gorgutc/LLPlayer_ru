---
name: llplayer-frozen-decisions
description: Use when a change might alter LLPlayer_ru stack, packaging, runtime assets, or verification policy.
---

# LLPlayer Frozen Decisions

Treat these as protected unless the user explicitly changes them:

- The shipped Windows WPF app targets `.NET 10` and remains supported until explicit replacement acceptance.
- F-13 adds one Windows/Linux `LLPlayer.Avalonia` host, shared `LLPlayer.Core`, and portable FlyleafLib `net10.0`
  alongside the WPF Windows target. Keep one Views/ViewModels/Themes tree for both Avalonia RIDs.
- The Windows Avalonia build/package baseline is implemented using the portable engine. Record fresh native
  desktop acceptance separately; Windows ASR/OCR and GPU require their own evidence. See
  `docs/agent/architecture.md` and `docs/agent/f13-status-2026-10-18.md`.
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
- WPF release packaging is defined by `.github/actions/build-package/action.yml`; additive Windows Avalonia ZIP
  packaging uses `scripts/windows/publish-avalonia.ps1`. Linux packaging uses `scripts/linux/publish.sh` and
  `.github/workflows/build-linux.yml`.
- Windows PowerShell/.NET gates remain required for WPF; portable/Core/Avalonia changes also require
  `scripts/windows/verify-avalonia.ps1` and `scripts/linux/verify.sh`.
- No global `package.json` or web quality stack.

Run `scripts/codex/verify-frozen.ps1` after touching these surfaces.
