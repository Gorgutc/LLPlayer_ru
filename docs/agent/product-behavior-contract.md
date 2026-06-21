# Product Behavior Contract

This document freezes the current user-facing behavior from `main`. Future work should change only the requested surface and preserve unrelated behavior.

## Product Position

LLPlayer is a specialized Windows media player for language learning. It is not a general replacement for VLC/mpv. The core value is video playback plus subtitle-centric learning workflows.

## Core Functions To Preserve

- Play local media files and online video URLs.
- Accept one command-line path/URL and open it at startup.
- Drag/drop and context-menu based media/subtitle workflows through the Flyleaf host.
- Dual subtitles: primary and secondary subtitle tracks, both text and bitmap.
- Subtitle overlay on the video surface and optional subtitles sidebar.
- Subtitle seeking, sidebar search, current/previous/next subtitle context, and per-item seek.
- ASR subtitle generation through Whisper.net/whisper.cpp and faster-whisper.
- Batch subtitles for local folders: scan video files, run ASR per video, translate completed subtitle sets to Russian through the configured Translate provider, and save `video.ru.srt` beside each video.
- OCR for bitmap subtitles through Tesseract and Microsoft OCR paths.
- Translation through Google V1, Bing, Azure, DeepL, DeepLX, Ollama, LM Studio, KoboldCpp, OpenAI, OpenAI-like, Claude, and LiteLLM settings.
- Context-aware translation for LLM-like providers through chat/context configuration.
- Word lookup and browser/search actions from subtitle text.
- Text subtitle word actions: left-click word lookup, left-drag phrase lookup, middle-click sentence lookup, right-click word actions, modifier-triggered last search, pause-on-selection, popup close-on-play, and optional copy-on-selection.
- Subtitle download through OpenSubtitles provider and subtitle export to SRT.
- Online video integration through the `YoutubeDL` plugin and release-downloaded `yt-dlp.exe`.
- Fully customizable keyboard shortcuts and mouse controls.
- Built-in CheatSheet for keyboard and mouse actions.
- Dark theme with configurable colors and app settings.

## User-Facing Invariants

- Preserve dual subtitle model unless the user explicitly requests a redesign.
- Preserve primary/secondary distinction in overlay, sidebar, copy actions, sizing, and translation.
- Preserve text and bitmap subtitle support; do not regress bitmap positioning or OCR flow when touching text subtitle code.
- Preserve app-level actions through `LLPlayer/Services/AppActions.cs`; do not bypass this layer for user-visible commands.
- Preserve `Save & Close` versus `Close` behavior in settings: only the save path writes config files.
- Preserve CheatSheet discoverability for shortcuts and mouse behavior when actions change.
- Preserve Settings Keys as an editable shortcut workflow with Add/Load/Apply, clone/delete, duplicate blocking, grouped actions, custom actions, key capture, and Enter commit.
- Preserve CheatSheet as a searchable, executable action surface with Keyboard/Mouse tabs and enabled-binding filtering.
- Preserve explicit user-selected export path for SRT export.
- Preserve batch subtitle output as user runtime files beside source videos. The default batch collision policy skips existing `video.ru.srt` files unless overwrite is explicitly enabled.

## Manual Checks When Touched

Use `docs/agent/manual-smoke-matrix.md` for smoke scenarios. Unit tests do not cover WPF rendering, real media playback, external translators, ASR/OCR engines, or `yt-dlp.exe`.
