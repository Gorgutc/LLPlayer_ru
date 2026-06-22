# WPF Structure Map — for the Material 3 re-skin

Structural map of the current WPF UI layer (as of `main` / commit `d69b2b6`). Companion to
`docs/agent/wpf-design-contract.md` (which freezes *behaviour*); this file maps *where things live*
and *what the re-skin must retheme*. Repo root = the LLPlayer fork.

Stack anchor: WPF on .NET 10, **MaterialDesignThemes (MDIX) 5.3.1**, Prism.DryIoc 9.0.537.

---

## (a) Resource-dictionary architecture

### Load order in `App.xaml` (load-bearing — do not reorder)

`LLPlayer/App.xaml` `Application.Resources` merges, in this exact order:

1. `materialDesign:CustomColorTheme` — `BaseTheme="Dark"`, `PrimaryColor="#D23D6F"`, `SecondaryColor="#00B8D4"`.
   This is the **single origin of all theme brushes** (`MaterialDesign.Brush.Primary`,
   `…Secondary`, `…Background`, `…Foreground`, plus `MaterialDesignBody`/`MaterialDesignPaper`/
   `MaterialDesignDivider` aliases). It seeds the dark palette + the magenta/cyan accent pair.
2. `MaterialDesign2.Defaults.xaml` (`pack://…/MaterialDesignThemes.Wpf;component/Themes/MaterialDesign2.Defaults.xaml`)
   — the MDIX **v2 (not v3)** default control styles: `MaterialDesignRaisedButton`,
   `MaterialDesignFlatButton`, `MaterialDesignOutlinedButton`, `MaterialDesignIconButton`,
   `MaterialDesignToolButton`, `MaterialDesignActionToggleButton`, `MaterialDesignWindow`,
   `MaterialDesignTextBox`/`…TextBlock`/`…GroupBox`/`…ListBoxItem`/`…DataGridCell`/`…MenuItem`/
   `…Hyperlink`, `MaterialDesignCircularProgressBar`, `Card`/`Snackbar`/`DialogHost`/`ColorPicker`/
   `PopupBox` styling, the `MaterialDesignFont` family, corner radii, elevation/shadow resources
   (`MaterialDesignShadowDepth1`), and the `materialDesign:` attached-property assists
   (`HintAssist`, `TextFieldAssist`, `ElevationAssist`, `ToggleButtonAssist`, `RippleAssist`).
3. `/Resources/MaterialDesignMy.xaml` — app overrides of the MDIX **Menu** subsystem (custom
   `MenuItem` ControlTemplate, `MaterialDesignMenu`, `FlyleafContextMenu`) + `MyHyperLink`.
4. `/Resources/Converters.xaml` — all `IValueConverter`/`IMultiValueConverter` instances as keyed resources.
5. `/Resources/PopupMenu.xaml` — the entire right-click `ContextMenu` (`PopUpMenu`) + per-stream menu
   ArrayLists/templates. **Depends on keys from #2, #3, #4**, hence merged last among the dicts.
6. `/Resources/Validators.xaml` — `ColorHexRule` validation rule.

The comment in `App.xaml` explicitly warns: PopupMenu uses StaticResource keys from MaterialDesignMy +
Converters + MDIX defaults, so it must stay after them. **For the re-skin: if you swap #1/#2 to
Material 3, you must re-validate that every StaticResource key referenced in #3–#6 and in every View
still resolves** (a missing StaticResource key throws at the moment the dictionary/dialog is built, not
at compile time — see gotchas).

### Where colours / brushes / radii / fonts come from

- **Colours & brushes:** 100% from the `CustomColorTheme` + `MaterialDesign2.Defaults` (#1/#2). Views
  reference them almost exclusively via `{DynamicResource MaterialDesign.Brush.Primary}` /
  `MaterialDesignBody` / `MaterialDesignPaper` / `MaterialDesignDivider` /
  `MaterialDesignToolBarBackground`, etc. `DynamicResource` means **live theme swaps work** (theme mode
  is applied at runtime via `PaletteHelper`, and accent-sync rewrites Primary live).
- **Hard-coded colours (will NOT follow a new theme — must be hunted down):** `#333333` (WordPopup over
  video), `#444444` (sidebar selected row), `#888888`/`#01000000` (sidebar spinner / hit-test-only
  backgrounds), `#302D2B` (GridSplitter), `#AA000000` (PlayerDebug box), `#ef5350` (ErrorDialog
  unknown-error red), `#FFFFFF` (subtitle separator), `White` literals on the FlyleafBar card text and
  WordPopup translation. Subtitle font/stroke colours come from **config** (`FL.Config.Subs.*`), not the
  theme, and are intentionally user-owned.
- **Radii:** mostly inline `CornerRadius` literals (menu `6`, WordPopup `8`, chips `3`, OSD `15`) plus
  MDIX defaults; `materialDesign:TextFieldAssist.TextFieldCornerRadius` used in the sidebar list.
- **Fonts:** `MaterialDesignFont` (from #2) is the app default; subtitle/sidebar fonts are config-driven.

---

## (b) 16 design screens → WPF file(s)

| # | Design screen | WPF file(s) (all under `LLPlayer/`) |
|---|---|---|
| 1 | player | `Views/MainWindow.xaml` (shell: FlyleafHost + GridSplitter + sidebar host) → `Views/FlyleafOverlay.xaml` (overlay: empty/loading/error states, OSD, debug, snackbar) → `Controls/FlyleafBar.xaml` (bottom transport bar) + `Controls/SubtitlesControl.xaml` (overlay subtitle render) + `Views/SubtitlesSidebar.xaml` + `Controls/WordPopup.xaml` + `Controls/SelectableSubtitleText.xaml` |
| 2 | settings | `Views/SettingsDialog.xaml` (TreeView + content host) → pages in `Controls/Settings/`: `SettingsPlayer`, `SettingsVideo`, `SettingsAudio`, `SettingsSubtitles`, `SettingsSubtitlesPS`, `SettingsSubtitlesASR`, `SettingsSubtitlesOCR`, `SettingsSubtitlesTrans`, `SettingsSubtitlesAction`, `SettingsKeys`, `SettingsKeysOffset`, `SettingsMouse`, `SettingsThemes`, `SettingsPlugins`, `SettingsAbout` + `Controls/Settings/Trans/OpenAIBaseTranslateControl.xaml` + `Controls/Settings/Controls/ColorPicker.xaml` |
| 3 | cheatsheet | `Views/CheatSheetDialog.xaml` |
| 4 | downloader | `Views/SubtitlesDownloaderDialog.xaml` |
| 5 | whisper-download | `Views/WhisperModelDownloadDialog.xaml` |
| 6 | whisper-engine-download | `Views/WhisperEngineDownloadDialog.xaml` |
| 7 | tesseract-download | `Views/TesseractDownloadDialog.xaml` |
| 8 | command-palette | `Views/CommandPaletteDialog.xaml` |
| 9 | export | `Views/SubtitlesExportDialog.xaml` |
| 10 | batch | `Views/BatchSubtitlesDialog.xaml` |
| 11 | select-language | `Views/SelectLanguageDialog.xaml` |
| 12 | error | `Views/ErrorDialog.xaml` |
| 13 | empty-state | Empty-state `StackPanel` inside `Views/FlyleafOverlay.xaml` (lines ~31–85): movie icon + "Open a file…" + Open File raised button + feature teaser + Settings/Shortcuts flat buttons. Sidebar has its own "No subtitles" empty state in `SubtitlesSidebar.xaml` (~463–487). |
| 14 | loading | Indeterminate `MaterialDesignCircularProgressBar` + "Opening…" in `Views/FlyleafOverlay.xaml` (~170–191); sidebar load spinner in `SubtitlesSidebar.xaml` (~490–499); WordPopup spinner. |
| 15 | playback-error | Error `StackPanel` (capped ScrollViewer + read-only TextBox + Retry/Open File) in `Views/FlyleafOverlay.xaml` (~195–250). Distinct from the modal `ErrorDialog` (#12). |

Note: screens 13–15 are **not separate files** — they are visibility-toggled regions composed inside
`FlyleafOverlay.xaml` over the video stage.

---

## (c) Reusable styles / templates / StaticResource keys to retheme

### Buttons / icon buttons (consumed everywhere; reskin once at the MDIX-default layer)
- MDIX-default keys used directly: `MaterialDesignRaisedButton`, `MaterialDesignRaisedSecondaryButton`,
  `MaterialDesignFlatButton`, `MaterialDesignOutlinedButton`, `MaterialDesignIconButton`,
  `MaterialDesignToolButton`, `MaterialDesignActionToggleButton`.
- Local `IconButton` styles are defined **inline and duplicated** (not shared): one in `FlyleafBar.xaml`
  (`Grid.Resources`, 32×32 BasedOn `MaterialDesignIconButton`) and one in `SubtitlesSidebar.xaml`
  (`x:Key="IconButton"` 24×24, plus `ToggleButton`, `SubIconButton`, `SubPlayIconButton`). A Material 3
  pass should consider promoting these to a single shared dictionary.

### Slider (`Resources/Slider.xaml`)
- `FlyleafSlider` (TargetType Slider) — the seek bar and volume slider both use it
  (`Style="{DynamicResource FlyleafSlider}"`).
- Supporting templates: `MaterialDesignSliderHorizontal` (ControlTemplate), `MaterialDesignSliderThumb`
  (Thumb template), `MaterialDesignRepeatButton`. Track/thumb/bar sizing is driven by the attached
  property `local:SliderLayout.BarHeight/TrackHeight/ThumbHeight` (bound to `FL.Config.SeekBar*`).
  Active track + selection range + chapter ticks live here. **This is the SeekBar component.**

### Menu / context menu (`Resources/MaterialDesignMy.xaml` + `Resources/PopupMenu.xaml`)
- `MaterialDesignMenuItem` (full custom `MenuItem` ControlTemplate — ripple, icon column, check glyph,
  submenu popup with `CornerRadius="6"` + `MaterialDesignShadowDepth1`).
- `MaterialDesignMenu` (MenuBase), `FlyleafContextMenu` (ContextMenu chrome: rounded border + shadow).
- `MyHyperLink`.
- `PopupMenu.xaml` data: `PopUpMenu` (the master right-click menu), `MenuAudioStreams`,
  `MenuVideoStreams`, `MenuSubtitlesStreams`, `MenuSubtitlesStreams2`,
  `MenuSubtitlesStreamsItemTemplate`, `MenuSubtitlesStreamsItemContainerStyle`,
  `DeviceMenuHierarchyTemplate`. These are bound into the FlyleafBar stream buttons via
  `ItemsSource="{StaticResource …}"`. The Menu component is the heaviest custom-templated surface.

### Converters (`Resources/Converters.xaml`) — ~35 keyed converters
Re-skin-relevant ones: `BooleanToVisibilityConv` (+ Invert/Hidden variants), `WidthToVisibilityConv` /
`InverseWidthToVisibilityConv` (FlyleafBar narrow-bar overflow), `OnColorForegroundConv`
(black/white text auto-pick over the accent chip — keep this for M3 contrast),
`ColorToBrushConv` / `ColorToHexConv` (color picker + subtitle colours), `EnumToBoolean/Description/
Visibility/String`, `QualityToLevelsConv`, `VolumeToLevelsConv`, `TicksToTime*`, `SubIsPlayingConv`
(currently-playing cue accent), `SubTextMaskConv`/`SubTextFlowDirectionConv`. These are logic, not
style — keep them; only the brush/visibility ones interact with theming.

### Dialog window chrome
- `MaterialDesignWindow` (MDIX default) is the Style on `MyDialogWindow.xaml` **and** `MainWindow.xaml`.
  This is where window border/title-bar chrome + corner treatment come from at the WPF level.

### Validation / text
- `ColorHexRule` (`Resources/Validators.xaml`), used by ColorPicker + SettingsThemes hex boxes.
- `SelectableTextBox` style in `Themes/SelectableTextBox.xaml` (deliberately **not** MaterialDesign —
  transparent, borderless, non-focusable; used for click-to-translate subtitle text). `Themes/Generic.xaml`
  exists only to merge `SelectableTextBox.xaml` (the WPF `themes/generic.xaml` default-style location for
  the custom controls).

### Component → location quick index (from the brief's component list)
| Component | Where it is themed today |
|---|---|
| Button / IconButton | MDIX `MaterialDesign*Button` defaults + inline `IconButton` styles (FlyleafBar, Sidebar) |
| Chip | Inline `Border` (ASR status chip in FlyleafBar) — no shared chip style |
| TextField | MDIX `MaterialDesignTextBox` + `HintAssist`/`TextFieldAssist` assists |
| Switch | `ToggleButton` (MDIX default) — Settings rows, sidebar toggles |
| Slider / SeekBar | `FlyleafSlider` + templates in `Resources/Slider.xaml` |
| Select | MDIX `ComboBox` default (SettingsThemes, ColorPicker, download dialogs) |
| Tabs | MDIX `TabControl` (CheatSheet Keyboard/Mouse tabs) |
| Card | MDIX `materialDesign:Card` (FlyleafBar, CommandPalette) |
| Menu | `MaterialDesignMy.xaml` + `PopupMenu.xaml` (see above) |
| Table | MDIX `DataGrid` + `MaterialDesignDataGridCell` (SettingsKeys, Batch) |
| WordPopup | `Controls/WordPopup.xaml` (inline Border + spinner + read-only TextBox) |
| Spinner | `MaterialDesignCircularProgressBar` (overlay, sidebar, WordPopup) |
| EmptyState | Inline StackPanels in `FlyleafOverlay.xaml` + `SubtitlesSidebar.xaml` |
| SubtitleCue | `Controls/SelectableSubtitleText.xaml` (overlay) — uses `AlignableWrapPanel` |
| SubtitleListItem | `ListBox.ItemTemplate` inline in `SubtitlesSidebar.xaml` |

---

## (d) Dialog window chrome / border-radius control

- All Prism dialogs render inside **`LLPlayer/Extensions/MyDialogWindow.xaml`**, a `Window` with
  `Style="{StaticResource MaterialDesignWindow}"`, `Foreground=MaterialDesignBody`,
  `Background=MaterialDesignPaper`, `Topmost` bound to `FL.Config.AlwaysOnTop`, `SizeToContent="Manual"`,
  `WindowStartupLocation="CenterOwner"`. **Width/Height bind TwoWay to the VM** (`WindowWidth`/
  `WindowHeight`, e.g. SettingsDialogVM defaults 1000×700). Registered once in `App.xaml.cs` via
  `RegisterDialogWindow<MyDialogWindow>()`; the 11 dialog *contents* are `RegisterDialog<…>()`.
- **Title-bar dark mode is code-behind, not XAML:** `MyDialogWindow.xaml.cs` ctor calls
  `MainWindow.SetTitleBarDarkMode(this)`, which P/Invokes `DwmSetWindowAttribute`
  (`DWMWA_USE_IMMERSIVE_DARK_MODE`). Mica backdrop is applied the same way on MainWindow only
  (`ApplyMicaBackdrop`, restart-to-apply). The window border/radius itself is OS + `MaterialDesignWindow`.
- Some dialogs override the window via **`prism:Dialog.WindowStyle`** inline (not via MyDialogWindow):
  `ErrorDialog` (borderless `WindowStyle=None`, `NoResize`, `Topmost`, click-to-close) and the three
  download dialogs (`ShowInTaskbar=False`, `NoResize`). The re-skin must touch both the shared
  `MyDialogWindow` chrome AND these per-dialog `WindowStyle` overrides.
- The `SettingsDialog` additionally hosts a `materialDesign:DialogHost`
  (`Identifier="SettingsDialog_RootDialog"`, `DialogTheme="Inherit"`) for in-place sub-dialogs (the
  ColorPicker opens through `DialogHost.OpenDialogCommand`). The CommandPalette content is wrapped in a
  bare `materialDesign:Card`.

---

## (e) Gotchas for restyling

1. **`MaterialDesign2.Defaults` is v2, not v3.** A "Material 3 re-skin" means swapping/augmenting the
   MDIX v2 defaults. MDIX 5.x ships separate v2/v3 default dictionaries; many of the keys used here
   (`MaterialDesignRaisedButton`, `MaterialDesignToolBarBackground`, the assists) are the **v2 key
   names**. If you adopt MDIX v3 dictionaries wholesale, audit every `StaticResource`/`DynamicResource`
   key name used across Views/Resources first — renamed/removed keys break things.
2. **Wrong StaticResource key throws at dialog-open, not at build.** A typo'd or removed key only fails
   when WPF instantiates that dictionary/template (e.g. opening Settings). Build + a launch-test of the
   main window is NOT enough; you must open every dialog. (Documented lesson in project memory.)
3. **Load order in `App.xaml` is contractual** (see (a)). PopupMenu/Validators/MaterialDesignMy depend on
   earlier dicts. Do not reorder; if you replace #1/#2, keep #3–#6 after them and re-verify keys.
4. **`x:Shared="False"`** on `MenuVideoStreams`, `MenuSubtitlesStreams`, `MenuSubtitlesStreams2` — these
   ArrayLists are instantiated fresh per consumer (a button's ContextMenu and the master PopUpMenu can
   both want them). Do not "optimise" this away; shared menu instances cannot live in two visual trees.
5. **`DynamicResource` vs `StaticResource` is deliberate.** Theme brushes use `DynamicResource` so live
   theme/accent swaps work. If you convert brush refs to `StaticResource` during a reskin you will break
   live theme switching and accent-color sync.
6. **Hard-coded literal colours** (listed in (a)) will not respond to a new palette. The Material 3 pass
   must replace `#333333`, `#444444`, `#302D2B`, `#888888`, `#ef5350`, `#FFFFFF`, and `White` literals
   with theme brushes (or M3 surface/role tokens) where they are meant to be themed. Note `OnColorForegroundConv`
   already solves accent-on-Primary legibility — reuse it rather than reinventing.
7. **Subtitle visuals are config-owned, not theme-owned.** `SelectableSubtitleText` colours/fonts/stroke
   come from `FL.Config.Subs.*`. Do not fold these into the theme; they are a user feature.
   `SelectableTextBox` is intentionally non-MaterialDesign (`Themes/SelectableTextBox.xaml` comment:
   "Do not use materialDesign").
8. **FlyleafHost DirectX airspace:** the video surface is a child HWND; theme brushes / Mica / overlays
   never paint over it. Empty/loading/error overlays deliberately have no panel `Background` so clicks &
   drag-drop pass through to the surface — preserve this (do not add a hit-testable background as part of
   a card/surface restyle).
9. **MDIX 5.3.1 ProgressBar binding-error noise** is a known upstream issue (commented at the spinners in
   FlyleafOverlay/WordPopup); not introduced by us, don't "fix" by removing the spinners.
10. **Inline-duplicated `IconButton` styles** (FlyleafBar vs Sidebar) and inline chip/empty-state markup
    mean some component styling is not centralised; a clean M3 pass may want to extract shared styles, but
    that is a refactor with regression surface — keep behaviour identical (sizes, focusability,
    AutomationProperties.Name values, narrow-bar overflow thresholds `BarOverflowThresholdPx=520` / volume
    `620`).
11. **Frozen contracts describe the OLD UI.** `docs/agent/wpf-design-contract.md` freezes current
    behaviour/structure; per project memory it must be revisited with the owner for the redesign rather
    than treated as a hard constraint. Honour behavioural invariants (shortcuts centralised in
    `AppActions`, dialog singleton activation, sidebar/search workflows) even while the visual skin changes.
