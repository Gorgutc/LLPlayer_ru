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
- Batch subtitles for local folders: scan video files, run ASR per video, translate completed subtitle sets to Russian through the configured Translate provider, and save `video.ru.srt` beside each video. A batch run keeps running if the main video window is closed (the app minimizes to the system tray and shows overall progress there + on the batch window's taskbar button); a video row can be double-clicked to open it in the player. Closing the batch window itself while a run is in progress also minimizes it to the tray and keeps processing (re-open from the tray menu); the run is stopped only via the Cancel button or the tray's Quit. Background-friendliness options (default on) keep the machine responsive during long runs: "smooth" mode never runs ASR and translation at the same time (so a GPU ASR engine and a local-LLM translator don't both saturate the GPU), and "run ASR on CPU while I work" transcribes the next audio chunk on CPU (faster-whisper) instead of the GPU while the keyboard/mouse are active, switching back to GPU after a configurable idle period — the chunk in flight finishes on its current device, so nothing already computed is lost.
- Clear subtitle-track switching: every available track — embedded (in the video), external files auto-detected beside the video or downloaded, and ASR — is presented with its language and source and can be assigned to the primary (1st) or secondary (2nd) slot from the right-click Subtitles ▸ Subtitle Tracks menu (both slots) or the sidebar "Subtitle tracks" quick switcher.
- OCR for bitmap subtitles through Tesseract and Microsoft OCR paths.
- Translation through Google V1, Bing, Azure, DeepL, DeepLX, Ollama, LM Studio, KoboldCpp, OpenAI, OpenAI-like, Claude, and LiteLLM settings.
- Context-aware translation for LLM-like providers through chat/context configuration.
- Word lookup and browser/search actions from subtitle text.
- Text subtitle word actions: left-click word lookup, left-drag phrase lookup, middle-click sentence lookup, right-click word actions, modifier-triggered last search, pause-on-selection, popup close-on-play, and optional copy-on-selection.
- Subtitle download through OpenSubtitles provider and subtitle export to SRT.
- Online video integration through the `YoutubeDL` plugin and release-downloaded `yt-dlp.exe`.
- Fully customizable keyboard shortcuts and mouse controls.
- Built-in CheatSheet for keyboard and mouse actions, plus a `Ctrl+K` command palette to search and run any bound action.
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
- The Command Palette (`Ctrl+K`) is an additive, optional surface over the same enabled key bindings; it does not replace CheatSheet or the context menu.
- Error routing: only the recoverable missing-ASR-model case is a non-blocking actionable snackbar (download deep-link, plus a one-time first-run onboarding hint). All other known errors — including translation/OCR configuration failures — remain blocking modals so they are not missed. The word-translation config error keeps its existing one-shot in-popup snackbar. ASR completion shows a non-blocking confirmation in addition to the completion sound.
- The empty (no-media) state offers Open File plus Settings and keyboard-shortcuts entries, and the right-click context menu includes a Settings entry.
- Preserve explicit user-selected export path for SRT export.
- Preserve batch subtitle output as user runtime files beside source videos. Files that already have a non-empty `video.ru.srt` are detected at scan time, shown as `Completed`, and excluded from the default run; at run time an existing non-empty `video.ru.srt` is likewise marked `Completed` rather than reprocessed. Both are overridden when overwrite is explicitly enabled. Each scanned file carries an include checkbox (with a select-all/none header) controlling whether it is processed, and failed files can be retried individually (per row) or in bulk; an explicit retry forces reprocessing regardless of an existing output.
- LLM translation guards against degenerate (looping) replies: a reply detected as a repetition loop, or cut off by the token cap, triggers one anti-loop retry (non-zero temperature + frequency penalty); a reply that still loops falls back to the source text for that line rather than returning or caching the loop. Degeneration detection is source-aware: when the source subtitle line is itself legitimately repetitive (e.g. a shouted "quick, quick, quick…"), a faithful repetitive translation is not flagged as a loop — the reply's allowed repetition scales with the source's own repetition. In batch subtitle generation a per-line content failure (a degenerate/looping reply, truncation, or empty/null content) likewise falls back to the untranslated source text for that single line and the run continues, matching interactive playback; network/timeout failures, configuration/auth errors and cancellation still fail the file rather than silently emitting all-source output. Frequency/presence penalties are tunable in Settings → Translate. (Frozen-default flip 1.3: new configs no longer force `temperature=0`; existing configs keep their saved value. Local LLM backends apply a non-persisted default `max_tokens` cap so a runaway loop fails fast instead of running to the request timeout.)
- Whisper ASR (whisper.cpp) defaults to `NoContext` on (`condition_on_previous_text` off) so a hallucinated/looping window at the start of a video does not propagate a repetition loop across the transcript. (Frozen-default flip 1.5: new configs default on; existing configs are migrated once — see `config-data-contract.md`.)

## Manual Checks When Touched

Use `docs/agent/manual-smoke-matrix.md` for smoke scenarios. Unit tests do not cover WPF rendering, real media playback, external translators, ASR/OCR engines, or `yt-dlp.exe`.
