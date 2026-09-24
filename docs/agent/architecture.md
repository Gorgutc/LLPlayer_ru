# Architecture

LLPlayer is a desktop media player for language learning. The shipped product is the Windows WPF app; a Linux
front-end on the same engine (Avalonia, backlog F-13) is in progress — see "Linux Port (F-13)" below.

## Fork Relationship (T-06)

This repository is a fork of upstream [`umlx5h/LLPlayer`](https://github.com/umlx5h/LLPlayer). The `_ru` suffix in the repository name denotes a **Russified agent/automation infrastructure layer**, NOT a Russian-localized build of the application:

- **Forked / repository-specific:** agent instructions and frozen contracts (`AGENTS.md`, `docs/agent/`), verification scripts (`scripts/codex/`), the `Plugins/llplayer-codex/` skill plugin, and Russian-language commit messages and infrastructure notes.
- **Inherited from upstream, unchanged:** the player application itself. The WPF UI, view models, and `FlyleafLib` engine track upstream; the app's strings, menus, and settings are **not** localized to Russian and remain in upstream English. Product changes are made narrowly and additively (see the frozen contracts), not as a rewrite or a translation of the app.
- **Possible future direction (not started):** Russian localization of the application's UI resources (e.g. `.resx` / bound strings) is a plausible future evolution that would make the `_ru` suffix describe an actual localized build. No UI-localization work has begun; until it does, treat the app as English-only and upstream-tracking. A full UI port/localization would be a large, owner-initiated effort (comparable in scope to the Avalonia port, F-13), not an incidental change.

The human-facing version of this note lives in the top-level `README.md` ("About this fork").

## Solution Units

- `LLPlayer/`: WPF app, Prism/DryIoc composition root, views, view models, controls, settings, dialogs, app config, and actions.
- `FlyleafLib/`: media engine, FFmpeg integration, DirectX/Vortice rendering, audio, video, subtitles, ASR, OCR, translation, playlists, and plugin interfaces. Targets `net10.0-windows10.0.18362.0` and portable `net10.0`.
- `LLPlayer.Avalonia/`: Linux desktop app (Avalonia 12.1.3, FluentTheme + own shadcn-token theme, CommunityToolkit.Mvvm) on FlyleafLib `net10.0`.
- `LLPlayer.Avalonia.Tests/`: Avalonia.Headless.XUnit tests for the Linux app.
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

## Linux Port (F-13)

The Windows build is untouched: `LLPlayer` (WPF) still compiles FlyleafLib's `net10.0-windows10.0.18362.0` target, and
shared FlyleafLib files only gained `#if WINDOWS` / `#if !WINDOWS` guards and `partial` declarations. Linux adds:

- **Portable FlyleafLib target (`net10.0`).** The D3D11 renderer, SwapChain, WPF controls/themes, MediaFoundation,
  XAudio2, native Windows methods and the Windows OCR are excluded from this target; replacements live in
  `FlyleafLib/Platform/Portable/` (compiled only there).
- **Seams the host implements** (all in the portable target):
  - `IUIDispatcher` (`Utils.UIDispatcher`) — UI-thread marshalling for `Utils.UI*`; `IHostServices`
    (`Utils.HostServices`) — clipboard, file dialog, completion sound.
  - `IVideoSurface` (`Renderer.Surface`) — receives each frame ready to show as BGRA32 from the software path
    (deinterlaced, HDR tone-mapped, cropped, rotated/mirrored and colour-filtered by the renderer) at
    min(native, viewport) size per axis; the host only scales it into `Renderer.Viewport`. `Rotation`/`HFlip`/`VFlip`
    are informational and must not be applied again.
  - `IAudioBackend` / `IAudioSink` (`AudioEngine.Backend`, set before `Engine.Start`) — OpenAL Soft on a desktop,
    `NullAudioSink` (real-time clock, silent) for headless runs and CI; `AudioBackendFactory.CreateDefault()` picks
    one and `LLPLAYER_AUDIO_BACKEND=null|openal|auto` forces the choice (the app calls it through
    `AudioBackendSelector` before `Engine.Start`).
  - `Player.KeyStateProvider`, `BindingOperations.CollectionSynchronizationHandler`,
    `CollectionViewSource.RefreshHandler`, `SubtitlesOCR.ServiceFactory` — WPF-free hooks for modifier keys,
    collection sync, filtered views, and OCR engines.
  - Small WPF stand-ins (`Point`, `Thickness`, the `Key` enum with WPF values) keep shared engine code unchanged.
- **`LLPlayer.Avalonia`** — the Linux app: implements the seams above, locates FFmpeg (`--ffmpeg-dir`,
  `LLPLAYER_FFMPEG_DIR`, else `<app>/FFmpeg`), stores preferences under XDG (`~/.config/LLPlayer`,
  `LLPLAYER_CONFIG_DIR` override) and must stay platform-neutral.
- **Tooling** — `scripts/linux/` (fetch FFmpeg, test media, verify, publish) and `.github/workflows/build-linux.yml`.

Planned direction (backlog F-13): extract shared app logic into a common core, reach feature parity in the Avalonia
UI, and only then consider it for Windows; the WPF app is retired only after an owner smoke on Windows.
