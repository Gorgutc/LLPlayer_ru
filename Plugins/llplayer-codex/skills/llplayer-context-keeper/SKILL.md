---
name: llplayer-context-keeper
description: Use when a small read-only context slice is needed before modifying LLPlayer_ru.
---

# LLPlayer Context Keeper

Gather the smallest useful slice before editing.

## Read First

- App startup: `LLPlayer/App.xaml.cs`, `LLPlayer/Services/FlyleafLoader.cs`.
- Main UI flow: `LLPlayer/Views/MainWindow.xaml`, `LLPlayer/ViewModels/MainWindowVM.cs`.
- Media runtime: `FlyleafLib/Engine`, `FlyleafLib/MediaPlayer`.
- Plugins: `FlyleafLib/Plugins`, `Plugins/YoutubeDL`.
- Build/release: `.github/workflows/build.yml`, `.github/actions/build-package/action.yml`.

Report concrete paths and current behavior. Do not infer web conventions.
