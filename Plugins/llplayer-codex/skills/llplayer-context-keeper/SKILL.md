---
name: llplayer-context-keeper
description: Use when a small read-only context slice is needed before modifying LLPlayer_ru.
---

# LLPlayer Context Keeper

Gather the smallest useful slice before editing. Read `AGENTS.md`, `docs/agent/architecture.md`, and `docs/agent/f13-status-2026-10-18.md` for current/target platform boundaries.

## Read First

- Shipped WPF startup: `LLPlayer/App.xaml.cs`, `LLPlayer/Services/FlyleafLoader.cs`.
- WPF UI flow: `LLPlayer/Views/MainWindow.xaml`, `LLPlayer/ViewModels/MainWindowVM.cs`.
- Current Linux Avalonia startup/UI: `LLPlayer.Avalonia/Program.cs`, `LLPlayer.Avalonia/AppHost.cs`, `LLPlayer.Avalonia/Views`, `LLPlayer.Avalonia/ViewModels`; shared app services: `LLPlayer.Core/`.
- Media runtime: `FlyleafLib/Engine`, `FlyleafLib/MediaPlayer`; portable `net10.0` implementations: `FlyleafLib/Platform/Portable/`.
- Plugins: `FlyleafLib/Plugins`, `Plugins/YoutubeDL`.
- Windows build/release: `.github/workflows/build.yml`, `.github/actions/build-package/action.yml`; Linux: `.github/workflows/build-linux.yml`, `scripts/linux/verify.sh`, `scripts/linux/publish.sh`.

Report concrete paths and current behavior separately from the native Windows/Linux Avalonia target. The Windows Avalonia baseline is not implemented or accepted; Linux headless tests and historical receipts do not establish native desktop acceptance. Do not infer web conventions.
