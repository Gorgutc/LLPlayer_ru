---
name: llplayer-context-keeper
description: Use when a small read-only context slice is needed before modifying LLPlayer_ru.
---

# LLPlayer Context Keeper

Gather the smallest useful slice before editing. Read `AGENTS.md`, `docs/agent/architecture.md`, and `docs/agent/f13-status-2026-10-18.md` for current/target platform boundaries.

## Read First

- Shipped WPF startup: `LLPlayer/App.xaml.cs`, `LLPlayer/Services/FlyleafLoader.cs`.
- WPF UI flow: `LLPlayer/Views/MainWindow.xaml`, `LLPlayer/ViewModels/MainWindowVM.cs`.
- Shared Windows/Linux Avalonia startup/UI: `LLPlayer.Avalonia/Program.cs`, `LLPlayer.Avalonia/AppHost.cs`,
  `LLPlayer.Avalonia/Views`, `LLPlayer.Avalonia/ViewModels`, `LLPlayer.Avalonia/Themes`; shared services: `LLPlayer.Core/`.
- Media runtime: `FlyleafLib/Engine`, `FlyleafLib/MediaPlayer`; portable `net10.0` implementations: `FlyleafLib/Platform/Portable/`.
- Plugins: `FlyleafLib/Plugins`, `Plugins/YoutubeDL`.
- Windows WPF build/release: `.github/workflows/build.yml`, `.github/actions/build-package/action.yml`; Windows
  Avalonia: `scripts/windows/verify-avalonia.ps1`, `scripts/windows/publish-avalonia.ps1`, additive `build.yml` job;
  Linux: `.github/workflows/build-linux.yml`, `scripts/linux/verify.sh`, `scripts/linux/publish.sh`.

The Windows Avalonia build/package baseline is implemented using the same portable `net10.0` host/UI as Linux.
Report fresh verification and native desktop acceptance separately; Windows ASR/OCR and GPU remain separate work.
Historical receipts and headless tests are not current desktop acceptance. Do not infer web conventions.
