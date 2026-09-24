# LLPlayer for Linux (LLPlayer.Avalonia)

The Linux desktop app of LLPlayer (backlog F-13): an Avalonia 12 UI over FlyleafLib's portable `net10.0` build.
The Windows product is still the WPF app in `../LLPlayer` (unchanged).

- UI: Avalonia 12.1.3 with the built-in FluentTheme plus the "LLPlayer shadcn" theme (shadcn/ui neutral tokens,
  `Themes/`), CommunityToolkit.Mvvm view models, plain constructor wiring (`AppHost`).
- Video: FlyleafLib's software renderer (deinterlace, HDR tone mapping, `sws_scale` → BGRA, rotation/flips, colour
  filters) presented by `Controls/VideoView`, which only scales the ready-to-show frame into the renderer's viewport.
- Audio: `Services/AudioBackendSelector` → FlyleafLib's `AudioBackendFactory.CreateDefault()`, assigned to
  `AudioEngine.Backend` before `Engine.Start`: OpenAL Soft (system `libopenal.so.1`, package `libopenal1`) when its
  default device opens, otherwise the silent real-time `NullAudioBackend` with a warning in `flyleaf.log`.
  `LLPLAYER_AUDIO_BACKEND=null|openal|auto` forces the choice.
- Windowing: X11 (or XWayland) via `UsePlatformDetect()`. Native Wayland (`Avalonia.Wayland`) is a possible future
  opt-in and is intentionally not referenced yet.

## Build and run

```sh
dotnet build LLPlayer.Avalonia
# FFmpeg 8.x shared libraries (libavcodec.so.62, libavformat.so.62, libavutil.so.60, libavfilter.so.11,
# libswscale.so.9, libswresample.so.6):
LLPLAYER_FFMPEG_DIR=/path/to/ffmpeg/lib dotnet LLPlayer.Avalonia/bin/Debug/net10.0/LLPlayer.Avalonia.dll video.mp4
```

FFmpeg folder lookup: `--ffmpeg-dir <dir>`, else `$LLPLAYER_FFMPEG_DIR`, else `<app folder>/FFmpeg`. When the libraries
are missing a window explains what is missing and how to fix it.

Command line: `LLPlayer.Avalonia [--sub <file>] [--theme dark|light] [--ffmpeg-dir <dir>] [media path or URL]`
(`--help` lists everything, including the developer options below).

## Files

| What | Where |
| --- | --- |
| Preferences (volume, subtitle size, sidebar, theme, recent files, last folder, word translation) | `$LLPLAYER_CONFIG_DIR`, else `$XDG_CONFIG_HOME/LLPlayer`, else `~/.config/LLPlayer` → `LLPlayer.Avalonia.json` |
| Crash log | `$XDG_STATE_HOME/LLPlayer/crash.log` (default `~/.local/state/LLPlayer/crash.log`) |
| Engine log (warnings; `LLPLAYER_LOG_LEVEL=Debug` for more) | `$XDG_STATE_HOME/LLPlayer/flyleaf.log` |

`WordTranslationService` in the preferences file selects the word-click translator (a FlyleafLib `TranslateServiceType`
name, default `GoogleV1` as in the WPF app; `Off` disables online lookups). `TranslateTargetLanguage` optionally
overrides the target language (FlyleafLib `TargetLanguage` name, default: system language).

## Features (MVP)

- Transport bar: open, play/pause, seek bar (no feedback loop while dragging), time, speed menu, volume + mute,
  subtitles toggle, sidebar toggle, fullscreen (bar and cursor auto-hide after 2.5 s idle in fullscreen).
- Menus: Media (open media / subtitles, recent, exit), Playback, Subtitles (primary + secondary track for embedded and
  external tracks, size, position, copy), View (sidebar, search, fullscreen, dark/light theme), Help (shortcuts).
- Drag & drop of media and subtitle files; one command-line path / URL opens at start.
- Dual subtitle overlay (primary + secondary, outlined text with shadow, hidden when empty); bitmap subtitles are drawn
  at their position in the picture.
- Subtitles sidebar: cues with start times, current cue highlighted and scrolled into view, click to seek, filter box
  (Ctrl+F, Esc clears).
- Click a subtitle word: playback pauses and a popup shows the word, copy buttons and the translation (FlyleafLib
  translation stack); it closes on Esc, a click on the video or when playback resumes.
- Keyboard: FlyleafLib's default key bindings (Space, arrows, A/S/D, F, M, Ctrl+O, ...) go through `Player.KeyDown/KeyUp`;
  app shortcuts keep the WPF defaults (Ctrl+B sidebar, Ctrl+F search, F1 cheat sheet, Shift+arrows subtitle size /
  position, Ctrl+C copy) plus `?` (cheat sheet), Ctrl+Shift+O (open subtitles), Ctrl+Shift+T (theme). F1 / `?` open the
  cheat sheet with key caps.
- Screensaver / sleep is inhibited while playing (when the platform supports it).

## File dialogs

Native dialogs come from xdg-desktop-portal (DBus) or GTK 3. On minimal systems without either, start with
`LLPLAYER_MANAGED_DIALOGS=1` to use Avalonia's built-in file chooser (the app also explains this in a toast when no
picker is available). Drag & drop and the command line always work.

## Theme

- `Themes/ShadcnTheme.axaml`: FluentTheme (palettes generated from the tokens) + token dictionary + control styles.
- `Themes/ShadcnTokens.axaml`: `Shadcn.<Token>Color` / `Shadcn.<Token>Brush` for Dark (default) and Light, radius,
  spacing, typography, Fluent resource-key overrides. The coloured regions are generated — edit and run
  `python3 Themes/tools/oklch_to_hex.py` (`--check` verifies they are up to date).
- `Themes/Controls/*.axaml`: shadcn variants (Button classes `secondary outline ghost destructive link`, sizes
  `sm lg icon icon-sm`; ToggleButton; TextBox; Slider (`thin`); CheckBox; ToggleSwitch; ComboBox; ListBox; Menu /
  ContextMenu / MenuFlyout; ToolTip; ScrollBar; `Border.card` / `Border.popover`; Separator; `Window.dialog`; Toast;
  `Border.badge` / `Border.kbd`).
- Developer gallery: `--theme-gallery` shows every styled control in both themes.

## Developer options

`--theme-gallery`, `--sidebar`, `--seek <s>`, `--screenshot <file.png> [--screenshot-delay <s>]` (renders the window to a
PNG and exits), `--dev-word-popup` (opens the word popup on the first subtitle word before the screenshot). Example
under Xvfb:

```sh
xvfb-run -s "-screen 0 1600x900x24" dotnet LLPlayer.Avalonia.dll test-720p.mp4 --screenshot shot.png --screenshot-delay 15
```

The first open of a video with an unlabelled subtitle file next to it takes several seconds because FlyleafLib detects
the subtitle language with Lingua (all language models are loaded once per process, as in the WPF app).

## Tests

`dotnet test LLPlayer.Avalonia.Tests` — headless Avalonia (Skia) UI tests, view-model tests and service tests. The
end-to-end test (real engine, media and subtitles) runs when `LLPLAYER_FFMPEG_DIR` and `LLPLAYER_TEST_MEDIA`
(`test-720p.mp4` with `test-720p.srt` next to it) are set and is skipped otherwise. `LLPLAYER_EVIDENCE_DIR=<dir>` makes
the cheat-sheet test save a rendered PNG for visual review.

## Manual smoke (not covered by automated tests)

Real X11 window manager behaviour (fullscreen, focus), real audio output (after the OpenAL backend), native file
dialogs, clipboard with other applications, HiDPI scaling (`AVALONIA_GLOBAL_SCALE_FACTOR=2`), drag & drop from a file
manager, and long playback / 4K CPU load.
