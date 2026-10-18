# Architecture

LLPlayer is a desktop media player for language learning. The shipped product is the Windows WPF app. F-13 adds
Avalonia and shared Core services, currently packaged for Linux, with the same Avalonia UI on native Windows and
Linux as the owner-confirmed target. The next bounded native Windows baseline may precede full Linux parity;
WPF remains supported until explicit acceptance. See the [2026-10-18 status](f13-status-2026-10-18.md).

## Fork Relationship (T-06)

This repository is a fork of upstream [`umlx5h/LLPlayer`](https://github.com/umlx5h/LLPlayer). The `_ru` suffix in the repository name denotes a **Russified agent/automation infrastructure layer**, NOT a Russian-localized build of the application:

- **Forked / repository-specific:** agent instructions and frozen contracts (`AGENTS.md`, `docs/agent/`), verification scripts (`scripts/codex/`), the `Plugins/llplayer-codex/` skill plugin, and Russian-language commit messages and infrastructure notes.
- **Upstream foundation with additive fork work:** the WPF app and `FlyleafLib` retain their upstream architecture,
  with the documented product additions and F-13 portability/Core changes. Their source is not byte-identical to
  upstream. The app's strings, menus, and settings remain in English; `_ru` does not imply UI localization.
  Product changes remain narrow and subject to the frozen contracts.
- **Possible future direction (not started):** Russian localization of the application's UI resources (e.g. `.resx` / bound strings) is a plausible future evolution that would make the `_ru` suffix describe an actual localized build. No UI-localization work has begun; until it does, treat the app as English-only and upstream-tracking. A full UI port/localization would be a large, owner-initiated effort (comparable in scope to the Avalonia port, F-13), not an incidental change.

The human-facing version of this note lives in the top-level `README.md` ("About this fork").

## Solution Units

- `LLPlayer/`: WPF app, Prism/DryIoc composition root, views, view models, controls, settings, dialogs, app config, and actions.
- `LLPlayer.Core/`: shared multi-target app library. Bounded shared services currently include the Whisper model catalog, recognition/translation settings, recognition actions, word-click snapshots, the word library, and Anki/APKG exporters. Full app-config/action/service extraction remains incremental.
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

WPF product behavior, the `net10.0-windows10.0.18362.0` target, and the Windows release flow remain compatibility
requirements. The October import extracts formerly WPF-owned services and serialization into `LLPlayer.Core` and
updates project references; shared source is not unchanged. F-13 also adds conditional platform boundaries,
portable implementations, and bounded shared-code fixes. Keeping WPF shippable requires fresh verification.
Linux uses:

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
- **`LLPlayer.Avalonia`** — the Linux app today: implements the seams above, locates FFmpeg (`--ffmpeg-dir`,
  `LLPLAYER_FFMPEG_DIR`, else `<app>/FFmpeg`; Linux sonames or the Windows `*.dll` names by OS) and stores
  preferences per OS (`AppPaths`: XDG `~/.config/LLPlayer` + `~/.local/state/LLPlayer` on Linux,
  `%APPDATA%\LLPlayer` + `%LOCALAPPDATA%\LLPlayer` on Windows; `LLPLAYER_CONFIG_DIR` overrides the config folder).
  It must stay platform-neutral: no Linux-only assumptions in app code. Known Windows gaps, left for the
  "Avalonia on Windows" stage: `RuntimeIdentifiers` is `linux-x64` only, `publish.sh` builds only the Linux package,
  the software (CPU BGRA) renderer is the only video path, and OpenAL Soft is not bundled on Windows.
- **Tooling** — `scripts/linux/` (fetch FFmpeg, test media, verify, publish) and `.github/workflows/build-linux.yml`.

### Current vs Target ("one core + one UI", owner decision 2026-09-24)

Current source state (stages 1–2 complete; bounded Core, recognition, translation, words/export, Settings Hub and
Recognition Advanced slices implemented; their Linux receipts are historical through 2026-10-07):

```text
Windows:  LLPlayer (WPF, Prism) ──────────────> FlyleafLib net10.0-windows10.0.18362.0 (D3D11, XAudio2) ─> FFmpeg *.dll
          └─> LLPlayer.Core (shared model catalog and Anki/APKG exporters) ─> FlyleafLib shared types
Linux:    LLPlayer.Avalonia (Avalonia 12) ────> FlyleafLib net10.0 (software renderer, OpenAL) ────────> FFmpeg *.so
          └─> LLPlayer.Core (recognition/translation settings, word library/click snapshot, exporters + model catalog)
          (other app logic remains in the UI projects and moves only in later bounded slices)
```

Target:

```text
Windows + Linux:  LLPlayer.Avalonia (single UI) ─> LLPlayer.Core (config, actions, services, view-model logic)
                                                   └─> FlyleafLib (one engine; GPU renderer path on Windows)
                  WPF LLPlayer retired
```

Staged roadmap (backlog F-13; WPF remains shippable until explicit replacement acceptance):

**Priority update (direct owner direction, 2026-10-18):** deliver the same Avalonia UI and shared Core on native
Windows and Linux. A bounded native Windows Avalonia baseline may proceed before full Linux parity. This supersedes
the 2026-10-03 Linux-first deferral for future work; dated plans and receipts retain their original scope.

1. Reconcile the imported October source with fresh Windows WPF and Linux gates; record exact-head results separately
   from October receipts. Keep both platform CI jobs.
2. Establish a native Windows Avalonia playback baseline using FlyleafLib's portable `net10.0` target and the same
   Avalonia views/Core as Linux. Resolve Windows RID selection, FFmpeg/native audio dependencies, apphost/package
   contents, config/log paths, and startup failures. Software video is an acceptable first baseline, not GPU acceptance.
3. Verify Windows ASR/OCR dependencies and behavior in separate bounded slices. Existing Linux Whisper/Tesseract
   receipts do not establish Windows portable-runtime support.
4. Continue shared Core extraction and remaining feature parity. The catalog, recognition/translation settings and
   actions, word library/snapshots, and Anki/APKG exporters already exist; full app-config, service and view-model
   migration, fuller settings, faster-whisper/batch, plugins and dubbing remain open.
5. Add GPU decode/render paths as separately measured and reviewed work. Linux software playback and a proposed
   Windows software baseline do not establish VAAPI, OpenGL, or Windows GPU behavior.
6. Obtain native desktop acceptance on both operating systems for the relevant feature set, including playback,
   audio, subtitles, settings/data preservation, and shutdown. WSL/Linux verification is useful additional evidence;
   it does not substitute for native Windows Avalonia runtime acceptance.
7. Retire the WPF app only after replacement acceptance and the owner's explicit decision.

This is the next-slice proposal and current priority, not evidence that the Windows baseline is implemented.
One repository and feature branch (no Linux fork); CI for both platforms on every PR (`build.yml` + `build-linux.yml`).
