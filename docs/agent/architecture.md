# Architecture

LLPlayer is a Windows desktop media player for language learning.

## Fork Relationship (T-06)

This repository is a fork of upstream [`umlx5h/LLPlayer`](https://github.com/umlx5h/LLPlayer). The `_ru` suffix in the repository name denotes a **Russified agent/automation infrastructure layer**, NOT a Russian-localized build of the application:

- **Forked / repository-specific:** agent instructions and frozen contracts (`AGENTS.md`, `docs/agent/`), verification scripts (`scripts/codex/`), the `Plugins/llplayer-codex/` skill plugin, and Russian-language commit messages and infrastructure notes.
- **Inherited from upstream, unchanged:** the player application itself. The WPF UI, view models, and `FlyleafLib` engine track upstream; the app's strings, menus, and settings are **not** localized to Russian and remain in upstream English. Product changes are made narrowly and additively (see the frozen contracts), not as a rewrite or a translation of the app.
- **Possible future direction (not started):** Russian localization of the application's UI resources (e.g. `.resx` / bound strings) is a plausible future evolution that would make the `_ru` suffix describe an actual localized build. No UI-localization work has begun; until it does, treat the app as English-only and upstream-tracking. A full UI port/localization would be a large, owner-initiated effort (comparable in scope to the deferred Avalonia port, F-13), not an incidental change.

The human-facing version of this note lives in the top-level `README.md` ("About this fork").

## Solution Units

- `LLPlayer/`: WPF app, Prism/DryIoc composition root, views, view models, controls, settings, dialogs, app config, and actions.
- `FlyleafLib/`: media engine, FFmpeg integration, DirectX/Vortice rendering, audio, video, subtitles, ASR, OCR, translation, playlists, and plugin interfaces.
- `Plugins/YoutubeDL/`: runtime plugin that integrates `yt-dlp.exe` for online video.
- `WpfColorFontDialog/`: WPF color/font dialog support.
- `FlyleafLibTests/`: xUnit tests.

## Startup

`LLPlayer/App.xaml.cs` registers Prism services and dialogs, reads one command-line URL/path, starts the Flyleaf engine, and creates `MainWindow`.

`LLPlayer/Services/FlyleafLoader.cs` loads or creates engine/player config and starts `FlyleafLib.Engine`.

## Runtime Boundaries

`FlyleafLib.Engine` owns FFmpeg, audio, video, plugins, and the refresh thread. `FlyleafManager` connects the app UI layer to the player and config.

Detailed frozen boundaries live in:

- `docs/agent/product-behavior-contract.md`
- `docs/agent/wpf-design-contract.md`
- `docs/agent/media-runtime-contract.md`
- `docs/agent/config-data-contract.md`
