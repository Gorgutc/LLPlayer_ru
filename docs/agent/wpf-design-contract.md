# WPF Design Contract

This document freezes the current WPF/UI design decisions from `main`.

## Visual Style

- Dark MaterialDesign2 theme is the default. Light and Follow-Windows theme modes and Windows accent-color sync are opt-in (Settings ▸ Themes) and default off, so the dark MaterialDesign2 look is the shipped default. The Win11 Mica backdrop defaults **on** as of 0.3.2 (toggle in Settings ▸ Themes); theme mode applies live via `PaletteHelper`, while Mica is restart-to-apply and, due to the FlyleafHost DirectX child-HWND airspace, only affects chrome/borders (never the video surface) and gracefully no-ops on Windows 10 / non-Win11.
- App colors originate from `App.xaml` and app theme settings.
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
- Keys
- Key Offset
- Mouse
- Themes
- Plugins
- About

Do not remove or merge sections unless the user explicitly requests a settings redesign.

A search box above the TreeView filters sections by label/key (hiding non-matches, expanding branches with a match) and is cleared automatically before a deep-link navigation so targets are never hidden; it must not change the page cache or the `SelectedItemChanged`/`LoadPage` flow. The ASR section keeps its advanced whisper.cpp tuning knobs in a collapsed Expander.

## Subtitle UI

- Sidebar toolbar includes primary/secondary toggle, a subtitle-tracks quick switcher, font size, spoiler mask, original/translated toggle, download/export/batch subtitles, side swap, and search.
- Subtitle track switching is surfaced in two ways over the same engine streams + `OpenSubtitles`/`SubtitlesOff` commands: (1) the right-click Subtitles ▸ Subtitle Tracks menu exposes BOTH the Primary (1st) and Secondary (2nd) slots (previously only primary), each grouping Embedded (in video) / External files / ASR with source icons; (2) the sidebar toolbar "Subtitle tracks" `PopupBox` lists every available track (embedded, external files auto-detected beside the video or downloaded, and ASR) with its language + source and one-click ① primary / ② secondary assignment plus per-slot Off. Both are additive over the frozen dual-subtitle model and must not bypass `SubtitlesSelectedHelper`.
- Batch subtitles is a non-modal singleton dialog opened through `AppActions`. It owns folder selection, scan, queue progress, cancel, and output-folder access without redesigning existing subtitle settings or sidebar behavior. A batch run is decoupled from the player: it keeps running when the main video window is closed (the app minimizes to the system tray instead of quitting — see Main Window Layout), shows overall progress on its own taskbar button + the tray icon, and a video row can be double-clicked to play that file in the main player (restoring it from the tray if hidden). The dialog also exposes background-friendliness toggles (default on) — "Smooth (no ASR/translate overlap)" and "Pause while I work" with an idle-seconds threshold — and shows a paused-by-user indicator in its summary.
- Sidebar list remains virtualized/recycling and supports text and bitmap templates.
- Search behavior: Ctrl+F activates search, Esc clears, Enter/Shift+Enter navigate matches, focus returns to video after clear.
- Overlay supports primary/secondary text, bitmap absolute positioning, separator, word-click popups, and separate primary/secondary hover colors.

## Shortcut UI

Shortcut actions must stay centralized in `AppActions` and visible through CheatSheet/Settings Keys. If an action is renamed, update key settings, CheatSheet behavior, and config migration expectations together.

Settings Keys is an editable DataGrid workflow, not a static shortcut list. Preserve load-on-open, Add/Load/Apply buttons, clone/delete row actions, enabled toggles, modifier columns, `IsKeyUp`, duplicate-key warnings that block Apply, grouped action ComboBox with custom actions, key capture textbox, Enter-to-commit behavior, and scroll-to-added-row behavior.

CheatSheet is both documentation and an action surface. Preserve F1 access, Keyboard and Mouse tabs, enabled-binding filtering, grouped keyboard actions with color coding, Ctrl+F/find focus, search by description/shortcut, hit count, and the per-row action button that executes the selected action through `ActionInternal`.

The Command Palette is a `ShowSingleton` dialog (default `Ctrl+K`) that reuses the CheatSheet `KeyBindingCS` model as a flat, filterable list and runs the selected action through `ActionInternal` (Enter / double-click). It is additive and must not replace CheatSheet/Settings Keys or change `AppActions` centralization.

## Subtitle Word And Mouse UI

Text subtitle interaction is part of the learning workflow. Preserve left-click word lookup, left-drag phrase lookup including right-to-left drag support, middle-click sentence lookup, right-click word actions menu, modifier-triggered last search, pause-on-selection, optional copy-on-selection, popup close-on-play, cached/lazy word translation, and dispatcher-based popup repositioning for overlay and sidebar placement.
