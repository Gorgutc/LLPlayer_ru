# WPF Design Contract

This document freezes the current WPF/UI design decisions from `main`.

## Visual Style

> **Updated 2026-06 — Material 3 (Material You) re-skin.** The shipped default look is now a **rose-tinted
> dark Material 3** theme, re-skinned 1:1 from the Claude Design `flutter-m3` direction (the design was
> reverse-engineered from this app's own WPF). This is an appearance-only change: the WPF framework,
> MaterialDesignInXAML 5.3.1, the engine, and all behaviour are unchanged. The full skin spec, the master
> element tracker, the WPF map, and the foundation plan live in `docs/agent/redesign/`.

- **Default theme = Material 3 rose-tinted dark.** Brand seed `#D23D6F` is kept only as the HCT palette
  generator; filled surfaces use the lighter primary **container** tone `#ECB3C4` on `#5A1B2C` (not the
  saturated seed). Neutral surfaces are a rose-tinted dark ramp `#1A1216 → #241A1E → #2D2025 → #382A30`;
  secondary is tonal cyan `#7FD8E6`. Roboto (`MaterialDesignFont`) and MaterialDesign `PackIcon` (mdi) are
  kept 1:1.
- **Theme modes still work (functionality preserved).** Light, Follow-Windows, Win11 Mica, and Windows
  accent-color sync remain opt-in (Settings ▸ Themes), all default off. Theme mode applies live via
  `PaletteHelper`. The M3 rose ramp is delivered by two **toggled** resource dictionaries
  (`Resources/M3.Surfaces.xaml` = surfaces, `Resources/M3.Accent.xaml` = primary-container + secondary),
  managed by `AppConfigTheme.RefreshM3Overlays()`: present only for the dark default and only while the
  user has not enabled accent-sync or picked a custom Primary/Secondary colour. In Light / Follow-Windows
  the overlays are removed so the stock MaterialDesign light palette shows; accent-sync / colour-picker
  drop only the accent overlay so the chosen colour flows through. The overlays are re-asserted after
  every `PaletteHelper.SetTheme` (they are matched/removed by leaf filename). Mica stays restart-to-apply
  and (FlyleafHost DirectX child-HWND airspace) affects only chrome, never the video surface.
- **Shared shape + component layer** lives in `Resources/M3.xaml` (radii tokens `M3.Radius.8/16/20/24/28/Pill`;
  keyed opt-in styles `M3.FilledButton/TonalButton/OutlinedButton/TextButton` (pill 20, weight 500,
  flat-at-rest), `M3.IconButton`/`.Small` (round), `M3.Switch`, `M3.Card` (flat radius-16 + hairline);
  implicit `ToolTip`). Screens opt into these keyed styles; surfaces/accents apply globally via the brush
  overrides.
- App colors originate from `App.xaml` (`CustomColorTheme`), the M3 overlay dictionaries, and app theme settings.
- MaterialDesign PackIcon is the primary icon language for toolbar and menu actions.
- Resource dictionaries under `LLPlayer/Resources` and `LLPlayer/Themes` are shared UI infrastructure, not per-view decoration.
- Preserve the `App.xaml` merged dictionary order unless a task explicitly changes them together:
  `CustomColorTheme`, `MaterialDesign2.Defaults`, `MaterialDesignMy`, `M3` (shape/styles),
  `Converters`, `PopUpMenu`, `Validators`, then the two M3 colour overlays (`M3.Surfaces`, `M3.Accent`) last.
- Do not remove shared converters, popup menus, validators, or MaterialDesign resource defaults as cleanup; many views depend on them indirectly.

### Intentional M3 departures from the old UI (do not "restore")
- Filled surfaces use pale rose container `#ECB3C4` (not saturated `#D23D6F`); neutral ramp is rose-tinted.
- Sidebar now-playing cue: the 3px primary **left-border** is replaced by a **rounded 16px rose tonal fill**
  (`#2EECB3C4`), reusing the same `SubIsPlayingConv` signal — **no left bar**.
- Larger radii: buttons pill (20), fields/cards/menus 16, WordPopup 24, menu items rounded; menu icons rose.
- Transport play is a 48px tonal squircle (radius 16); the bare-icon play is gone; transport title is non-italic.
- OSD = round rose chip + on-primary glyph; timestamp = mono pill; loading spinner reads cyan.
- Settings left rail loses its right divider; dialog inner content panels are rounded (16/28). The dialog
  **OS window frame keeps `MaterialDesignWindow` chrome** (not retemplated) to avoid WindowChrome/DWM risk —
  the rounded M3 look is on the inner content, not the window border.
- Switches' on-track and seek/slider fill+thumb are rose; the snackbar keeps its placement/behaviour.

## Main Window Layout

- Main surface is media-first: `FlyleafHost` with `FlyleafOverlay`, bottom `FlyleafBar`, and optional `SubtitlesSidebar`.
- Sidebar can be left or right, has configurable width, and collapses with its `GridSplitter`.
- Fullscreen/video focus workflows must not be broken by dialogs or sidebar search.
- Taskbar progress and play/pause thumbnail action are owned by `MainWindowVM`.
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

- Sidebar toolbar includes primary/secondary toggle, font size, spoiler mask, original/translated toggle, download/export/batch subtitles, side swap, and search.
- Batch subtitles is a non-modal singleton dialog opened through `AppActions`. It owns folder selection, scan, queue progress, cancel, and output-folder access without redesigning existing subtitle settings or sidebar behavior.
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
