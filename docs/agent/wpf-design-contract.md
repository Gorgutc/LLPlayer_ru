# WPF Design Contract

This document freezes the current WPF/UI design decisions from `main`.

## Visual Style

- Dark MaterialDesign2 theme is the default.
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

## Dialogs

Registered Prism dialogs are part of the product surface:

- Settings
- Select language
- Subtitles downloader
- Subtitles exporter
- Batch subtitles
- CheatSheet
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

## Subtitle UI

- Sidebar toolbar includes primary/secondary toggle, font size, spoiler mask, original/translated toggle, download/export/batch subtitles, side swap, and search.
- Batch subtitles is a non-modal singleton dialog opened through `AppActions`. It owns folder selection, scan, queue progress, cancel, and output-folder access without redesigning existing subtitle settings or sidebar behavior.
- Sidebar list remains virtualized/recycling and supports text and bitmap templates.
- Search behavior: Ctrl+F activates search, Esc clears, Enter/Shift+Enter navigate matches, focus returns to video after clear.
- Overlay supports primary/secondary text, bitmap absolute positioning, separator, word-click popups, and separate primary/secondary hover colors.

## Shortcut UI

Shortcut actions must stay centralized in `AppActions` and visible through CheatSheet/Settings Keys. If an action is renamed, update key settings, CheatSheet behavior, and config migration expectations together.

Settings Keys is an editable DataGrid workflow, not a static shortcut list. Preserve load-on-open, Add/Load/Apply buttons, clone/delete row actions, enabled toggles, modifier columns, `IsKeyUp`, duplicate-key warnings that block Apply, grouped action ComboBox with custom actions, key capture textbox, Enter-to-commit behavior, and scroll-to-added-row behavior.

CheatSheet is both documentation and an action surface. Preserve F1 access, Keyboard and Mouse tabs, enabled-binding filtering, grouped keyboard actions with color coding, Ctrl+F/find focus, search by description/shortcut, hit count, and the per-row action button that executes the selected action through `ActionInternal`.

## Subtitle Word And Mouse UI

Text subtitle interaction is part of the learning workflow. Preserve left-click word lookup, left-drag phrase lookup including right-to-left drag support, middle-click sentence lookup, right-click word actions menu, modifier-triggered last search, pause-on-selection, optional copy-on-selection, popup close-on-play, cached/lazy word translation, and dispatcher-based popup repositioning for overlay and sidebar placement.
