# WPF Design Contract

This document freezes the current WPF/UI design decisions from `main`.

## Visual Style

- Dark MaterialDesign2 theme is the default. Light and Follow-Windows theme modes and Windows accent-color sync are opt-in (Settings ▸ Themes) and default off, so the dark MaterialDesign2 look is the shipped default. The Win11 Mica backdrop defaults **on** as of 0.3.2 (toggle in Settings ▸ Themes); theme mode applies live via `PaletteHelper`, while Mica is restart-to-apply and, due to the FlyleafHost DirectX child-HWND airspace, only affects chrome/borders (never the video surface) and gracefully no-ops on Windows 10 / non-Win11.
- App colors originate from `App.xaml` and app theme settings.
- An opt-in Material 3 (Material You) colour overlay (`Theme.ShowM3Theme`, Settings ▸ Themes, since 0.3.29, default **off**) re-asserts a rose-tinted dark surface ramp (`M3.Surfaces.xaml`) + primary-container accent (`M3.Accent.xaml`) on top of the live palette via `AppConfigTheme.RefreshM3Overlays`, applied last so it wins `DynamicResource` lookups. It applies only for the dark theme with the default Primary/Secondary colours (Light / Follow-Windows / accent-sync / a custom colour keep the stock palette). It is **colour-only** — control shapes, radii, and templates are unchanged. The overlays are not merged in `App.xaml`; with the default off the look is byte-identical to stock MaterialDesign2. A full per-surface M3 re-skin (shapes/radii) remains a future task.
- MaterialDesign PackIcon is the primary icon language for toolbar and menu actions.
- Resource dictionaries under `LLPlayer/Resources` and `LLPlayer/Themes` are shared UI infrastructure, not per-view decoration.
- Preserve the `App.xaml` merged dictionary order unless a task explicitly changes them together: `CustomColorTheme`, `MaterialDesign2.Defaults`, `MaterialDesignMy`, `Converters`, `PopUpMenu`, `Validators`.
- Do not remove shared converters, popup menus, validators, or MaterialDesign resource defaults as cleanup; many views depend on them indirectly.

## Main Window Layout

- Main surface is media-first: `FlyleafHost` with `FlyleafOverlay`, bottom `FlyleafBar`, and optional `SubtitlesSidebar`.
- Sidebar can be left or right, has configurable width, and collapses with its `GridSplitter`.
- Fullscreen/video focus workflows must not be broken by dialogs or sidebar search.
- Taskbar progress and play/pause thumbnail action are owned by `MainWindowVM`.
- App shutdown is `OnExplicitShutdown`. `MainWindow.OnClosing` diverts to the system tray (hides the window, pauses playback, keeps the process + player alive) instead of quitting **only while a batch is active** (running or its window open); otherwise the main window closes, the player is disposed, and the app shuts down. `AppTrayService` (a WinForms `NotifyIcon`, no extra dependency) and `BatchActivityService` own this: the tray icon appears only when needed (a batch is running or the window is hidden), shows overall batch progress, exposes Open LLPlayer / Batch subtitles… / Quit, restores the player on double-click, and is removed on exit. Explicit quits (tray Quit, App ▸ Exit App) route through `BatchActivityService.RequestQuit` so an in-flight batch is cancelled without a close prompt.
- A single app-wide MaterialDesign `Snackbar` (top-centre, hosted in `FlyleafOverlay`) carries non-blocking notifications and actionable config-error deep-links; ASR completion also enqueues a short confirmation here. It must not overlap the bottom `FlyleafBar` or the subtitle interaction area.

## Dialogs

Registered Prism dialogs are part of the product surface:

- Settings
- Select language
- Subtitles downloader
- Subtitles exporter
- AI Insights (F-07: transcript summary + vocabulary; non-modal `ShowSingleton`, opened from Subtitles ▸ AI Insights and `CustomKeyBindingAction.OpenWindowAiInsights` in the Window group, no default chord)
- Word Manager (F-10: global word list view/edit/delete + TSV / `.apkg` / AnkiConnect export; non-modal `ShowSingleton`, opened from Subtitles ▸ Word Manager and `CustomKeyBindingAction.OpenWindowWordManager` in the Window group, no default chord)
- Batch subtitles
- CheatSheet
- Command palette
- Whisper model download
- Whisper engine download
- Tesseract download
- Error dialog

Dialog opening is mediated by `ExtendedDialogService`; repeated non-modal dialogs should activate existing windows rather than create uncontrolled duplicates.

Dialog registration stays in `App.xaml.cs` through `RegisterDialogWindow<MyDialogWindow>()` and Prism dialog registrations. `MyDialogWindow` owns shared dialog window behavior, including `Topmost` binding to `FL.Config.AlwaysOnTop` and center-owner startup.

`ExtendedDialogService.ShowSingleton` preserves singleton activation for non-modal dialogs. Its orphan-window path intentionally clears `Owner` after show when requested. Download/model dialogs and error dialogs rely on VM-driven sizing and fixed/non-resizable window styles; do not replace this with ad hoc window creation.

## Settings UI

Settings navigation is a left TreeView plus right content area. Current sections:

- Player
- Video
- Audio
- Subtitles
- Subtitles Position / Size
- Subtitles ASR
- Subtitles OCR
- Subtitles Translate
- Subtitles Word Action
- Subtitles Dubbing
- Keys
- Key Offset
- Mouse
- Themes
- Plugins
- About

Do not remove or merge sections unless the user explicitly requests a settings redesign.

A search box above the TreeView filters sections by label/key (hiding non-matches, expanding branches with a match) and is cleared automatically before a deep-link navigation so targets are never hidden; it must not change the page cache or the `SelectedItemChanged`/`LoadPage` flow. The ASR section keeps its advanced whisper.cpp tuning knobs in a collapsed Expander.

The **Subtitles ▸ Dubbing** section (F-16 phase 1, extended in phase 2) surfaces the existing `Config.Subtitles.DubbingConfig` (dub voice, ducking %, atempo min/max, output format). The voice picker binds a GPU-free bank (`VoiceBankResolver` built-in presets plus any user-declared `DubbingConfig.CustomVoiceIds`) and never starts the TTS sidecar; `SelectedValuePath="Id"` round-trips `DefaultVoiceId` (a string). A **Custom voice IDs** list editor (Add/Remove) under the picker lets the user register voice ids they added to the local engine (`dub_sidecar/server.py` VOICES) so they become selectable without hand-editing config; the picker's `ObservableCollection` is mutated surgically (never cleared) so the two-way-bound selection never blanks, and removing the id that is the active dub voice keeps it selectable. Additive — with `CustomVoiceIds` empty and other defaults unchanged the section is byte-identical and writes the same values back. The batch dialog carries the same voice picker beside the "Generate Russian dub (AI)" checkbox (enabled only when dubbing is on).

## Subtitle UI

- Sidebar toolbar includes primary/secondary toggle, a subtitle-tracks quick switcher, font size, spoiler mask, original/translated toggle, download/export/batch subtitles, side swap, and search.
- Subtitle track switching is surfaced in two ways over the same engine streams + `OpenSubtitles`/`SubtitlesOff` commands: (1) the right-click Subtitles ▸ Subtitle Tracks menu exposes BOTH the Primary (1st) and Secondary (2nd) slots (previously only primary), each grouping Embedded (in video) / External files / ASR with source icons; (2) the sidebar toolbar "Subtitle tracks" `PopupBox` lists every available track (embedded, external files auto-detected beside the video or downloaded, and ASR) with its language + source and one-click ① primary / ② secondary assignment plus per-slot Off. Both are additive over the frozen dual-subtitle model and must not bypass `SubtitlesSelectedHelper`.
- Batch subtitles is a non-modal singleton dialog opened through `AppActions`. It owns folder selection, scan, queue progress, cancel, and output-folder access without redesigning existing subtitle settings or sidebar behavior. A batch run is decoupled from the player: it keeps running when the main video window is closed (the app minimizes to the system tray instead of quitting — see Main Window Layout), shows overall progress on its own taskbar button + the tray icon, and a video row can be double-clicked to play that file in the main player (restoring it from the tray if hidden). Closing the batch window itself while a run is in progress also minimizes it to the tray (the window is hidden, the run continues) rather than prompting to cancel — `CanCloseDialog` hides the window and returns false; re-open it via the tray "Batch subtitles…" menu (`ShowSingleton` re-shows a hidden window). Stop a run with the Cancel button or the tray's Quit. When idle, closing the batch window closes it normally. The dialog also exposes background-friendliness toggles (default on) — "Smooth (no ASR/translate overlap)" and "Run ASR on CPU while I work" with an idle-seconds threshold — and shows an "ASR on CPU (you're active)" indicator in its summary. A 1-second VM heartbeat (`DispatcherTimer`) keeps the active file's elapsed time + an approximate ETA, the overall % and a rough overall "~MM:SS left", and the tray tooltip moving every second even between slow ASR segment reports.
- Sidebar list remains virtualized/recycling and supports text and bitmap templates.
- Each sidebar subtitle row carries per-row icon buttons: Play (left), Sync-to-current (right), and — since F-16 phase 2a — a per-line **dub voice** button (`AccountVoice` icon, far right) that opens a left-click `ContextMenu` of the GPU-free voice bank (built-in presets + `DubbingConfig.CustomVoiceIds`) plus a leading "Use default voice" entry; selecting one sets the cue's `SubtitleData.AssignedVoiceId` via `SubtitlesSidebarVM.CmdSubSetVoice`, lighting the icon (Primary, full opacity) while an override is set. It is additive: rows stay non-selectable and virtualized, the global Settings/batch voice pickers (the run default) are untouched, and a row with no override renders byte-identically. The override only affects the AI dub and is interactive/in-memory by default (not persisted unless the opt-in `Subtitles.PersistPerLineVoices` toggle is on — since 0.3.37 it mirrors the override to a `video.ru.voices.json` companion beside the media and restores it at load, byte-identical when off; see dubbing-contract.md); when the batch dialog runs for the currently open local media, `BatchSubtitlesDialogVM` snapshots the assigned rows and applies matching voices to both fresh subtitles and existing `.ru.srt` subtitles just before rendering.
- Each sidebar subtitle row can additionally show a per-cue **language badge** (T-10 follow-up, since 0.3.38): a small dim `TextBlock` in a fifth Auto column after the dub-voice button, showing the cue's lower-case language code (ISO 639-1 where it exists; a language without a 2-letter code shows the .NET 3-letter fallback, e.g. `haw`/`yue`) (`SubtitleData.Language`, formatted by the pure `FlyleafLib` `LanguageBadge` helper via `SubLanguageBadgeConverter`) with the full language name as tooltip. Its visibility is a single `MultiBinding` (`SubLanguageBadgeVisibilityConv`) over the cue's `Language` and the live `FL.PlayerConfig.Subtitles.ASRPerSegmentLanguage` config gate — the gate must stay a binding (never read from config inside the converter) so flipping the Settings toggle shows/hides badges immediately. Additive and display-only: with the toggle off (default) or on cues without a resolvable language (loaded/translated subs, Unknown) the badge collapses and the Auto column takes zero width, so the row renders byte-identically; the badge is not focusable/clickable and must not disturb row virtualization or the existing Play/Sync/Voice buttons.
- Search behavior: Ctrl+F activates search, Esc clears, Enter/Shift+Enter navigate matches, focus returns to video after clear. Three persisted search-option toggles in the search row — match case, whole word, and regex (F-14, all default off = the prior case-insensitive substring search) — refine matching via a pure `FlyleafLib` `SubtitleSearcher`; a regex is bounded by a match timeout and an invalid pattern shows "Invalid regex" in the hit count (matching nothing). Search operates on the visible slot's original or translated text per the original/translated toggle, not a merged cross-track view.
- The player-bar ASR status chip is interactive (F-04): visible only while ASR is transcribing, a left-click toggles pause/resume (via `AppActions.CmdToggleASRPause` → `SubtitlesASR.Pause`/`Resume`) and the chip's trailing icon reflects the running/paused state (`FL.Player.IsASRPaused`). It is additive over the existing ASR enable/disable commands and must not replace them.
- The player bar has an A-B repeat control (F-12, since 0.3.27): a toolbar button (`RepeatVariant` icon; left-click cycles Set A → Set B → Clear via `FL.Player.Commands.ToggleABLoop`; right-click context menu carries the three explicit commands; lit Primary while `FL.Player.ABLoopActive`) plus a seek-bar marker overlay — a separate, non-hit-testable `Canvas` over the seek slider drawing the A and B markers and the band between them, bound to `Player.ABLoopA/ABLoopB/HasABLoopA/HasABLoopB/ABLoopEnabled/ABLoopActive` through `AbMarkerLeftConverter`/`AbBandWidthConverter`. It is purely additive: the slider's `IsSelectionRangeEnabled` buffering band is untouched, and the overlay is collapsed when no points are set.
- The player bar has a seek-bar waveform layer (F-12, since 0.3.28): a `ToggleButton` (`Waveform` icon, next to the A-B button, two-way bound to `FL.Config.ShowWaveform`, lit Primary when checked) plus a separate, non-hit-testable `Canvas` (`WaveformOverlay`) declared BEFORE the seek `Slider` so it paints BEHIND the track (the opposite z-order from `AbOverlay`, which stays on top). The Canvas holds one `Path` whose `Data` is a frozen `StreamGeometry` produced by `WaveformGeometryConverter` from `Player.WaveformPeaks` + the overlay `ActualWidth`/`ActualHeight` (a mirrored, display-auto-gained envelope, Primary brush at low opacity), collapsed via `Player.WaveformActive` when off/not-built. Purely additive: the slider, its buffering selection-range, and `AbOverlay` are untouched.
- Overlay supports primary/secondary text, bitmap absolute positioning, separator, word-click popups, and separate primary/secondary hover colors.
- The word-click popup (over-video and sidebar) optionally shows a dictionary DEFINITION (F-11, since 0.3.25) in a third, default-collapsed `DefinitionText` row below the translation: read-only, wrapping, height-capped with a vertical scrollbar, styled like the translation but secondary (smaller font, dimmer over video / theme body in the sidebar), and visible only when a definition was fetched (`DefinitionVisible`) — so the popup renders byte-identically when the feature is off or nothing was found. It is additive over the existing source-word + translation + Save/Close layout and must not alter the translation spinner/cancel choreography. The feature is configured by a "Word Definition (Dictionary)" group on the Settings ▸ Subtitles ▸ Word Action page (a Definition-Source dropdown bound to `WordDefinitionServiceType`), added next to the existing Word Translation Engine group.
- Word-popup lifetime is owned by its enclosing host, never by the child popup presentation source. Close, Esc, and playback dismissal only close the inner `Popup` and preserve that live instance's translation/definition caches and lazy services. When a `SubtitlesSidebar` instance or the overlay `SubtitlesControl` leaves its host tree, the parent calls reload-safe `ReleaseOwnedResources`: it invalidates/cancels the active lookup, disposes only the popup-owned translation/definition services, and clears transient UI/context-menu/Save state. Provider or language invalidation also closes and clears the current popup so an old result cannot be saved under new settings. The externally visible spinner/cancel behavior remains unchanged: a superseded operation cannot overwrite the latest popup, cache, spinner, Save snapshot, or notification, and each local async operation disposes its CTS only after all token consumers settle. `NonTopmostPopup` must symmetrically attach/detach its child and `MainWindow` handlers; this removes the known window-event root once in-flight work settles. `PDICSender` remains an app-owned singleton and is disposed only by `App.OnExit`.

## Shortcut UI

Shortcut actions must stay centralized in `AppActions` and visible through CheatSheet/Settings Keys. If an action is renamed, update key settings, CheatSheet behavior, and config migration expectations together.

Settings Keys is an editable DataGrid workflow, not a static shortcut list. Preserve load-on-open, Add/Load/Apply buttons, clone/delete row actions, enabled toggles, modifier columns, `IsKeyUp`, duplicate-key warnings that block Apply, grouped action ComboBox with custom actions, key capture textbox, Enter-to-commit behavior, and scroll-to-added-row behavior.

CheatSheet is both documentation and an action surface. Preserve F1 access, Keyboard and Mouse tabs, enabled-binding filtering, grouped keyboard actions with color coding, Ctrl+F/find focus, search by description/shortcut, hit count, and the per-row action button that executes the selected action through `ActionInternal`.

The Command Palette is a `ShowSingleton` dialog (default `Ctrl+K`) that reuses the CheatSheet `KeyBindingCS` model as a flat, filterable list and runs the selected action through `ActionInternal` (Enter / double-click). It is additive and must not replace CheatSheet/Settings Keys or change `AppActions` centralization.

## Subtitle Word And Mouse UI

Text subtitle interaction is part of the learning workflow. Preserve left-click word lookup, left-drag phrase lookup including right-to-left drag support, middle-click sentence lookup, right-click word actions menu, modifier-triggered last search, pause-on-selection, optional copy-on-selection, popup close-on-play, cached/lazy word translation, and dispatcher-based popup repositioning for overlay and sidebar placement.
