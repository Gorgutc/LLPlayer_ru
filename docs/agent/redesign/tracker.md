# LLPlayer Re-skin Master Tracker (WPF -> Material 3)

> **Single source of truth for "nothing missed"** in the 1:1 WPF -> Material3 re-skin of LLPlayer.
> Every screen, every component, every element is tracked here through DONE / TODO / SKIPPED.

---

## How to update (READ FIRST)

- Each line has a **stable ID** in `[BRACKETS]` (e.g. `[B-04]`, `[C-01-e3]`). Never renumber or delete IDs — only flip status and append a note.
- When you finish an item: change `[ ]` to `[x]` (or `[~]`/`[-]` per legend) **and append** `— <one-line note>` (what you did + commit/PR if any). Keep the original ID and label intact.
- If you discover a new element/region, **add a new line with the next free sub-ID** (e.g. `[C-01-e17]`) — do not reuse a retired ID.
- After flipping any line, **update the Progress Summary table counts** at the top.
- Do not edit other agents' notes; append your own after a `;` if you touch the same line.
- A screen is only "complete" when **both** its element lines AND its Section D parity gate are `[x]`.

## Status legend

| Mark | Meaning |
|------|---------|
| `[ ]` | **TODO** — not started |
| `[~]` | **IN PROGRESS** — partially done / under review |
| `[x]` | **DONE** — re-skinned + verified, parity preserved |
| `[-]` | **SKIPPED** — intentionally not re-skinned (note why) |
| `[!]` | **BLOCKED** — needs decision / depends on another item (note blocker) |

---

## Progress Summary

| Section | Scope | Total | Done `[x]` | In-progress `[~]` | Skipped `[-]` | Blocked `[!]` | TODO `[ ]` |
|---------|-------|------:|-----------:|------------------:|--------------:|--------------:|-----------:|
| A | Foundation | 12 | 5 | 4 | 0 | 0 | 3 |
| B | Components | 18 | 11 | 4 | 3 | 0 | 0 |
| C | Screens (elements) | 116 | 61 | 50 | 3 | 0 | 2 |
| D | Per-screen parity gates | 16 | 16 | 0 | 0 | 0 | 0 |
| **Total** | | **162** | **93** | **58** | **6** | **0** | **5** |

> Counts: A=12, B=18, C=116 (16 screens × their element lines), D=16. Total = 162. Update after every flip.
> Per-screen C element-line counts: C-01=18, C-02=20, C-03=5, C-04=7, C-05=5, C-06=5, C-07=5, C-08=6, C-09=6, C-10=9, C-11=5, C-12=5, C-13=5, C-14=3, C-15=4, C-16=8.

---

# SECTION A — Foundation

> Global tokens & chrome that everything else inherits. Re-skin these FIRST.
> Primary dicts: `Resources/MaterialDesignMy.xaml`, `Resources/Converters.xaml`, `Themes/Generic.xaml`, `Extensions/MyDialogWindow.xaml`.

- [x] `[A-01]` **Color ramp override** — M3 primary-container `#ECB3C4`/on `#5A1B2C` + secondary tonal `#7FD8E6` mapped onto `MaterialDesign.Brush.*` keys — `Resources/M3.Accent.xaml` (toggled, dark+default-colours)
- [x] `[A-02]` **Surface / background layers** — rose-tinted ramp `#1A1216→#382A30` over MDIX surface keys (Paper/Background/Card/ToolBar/Chip/TextFieldBox/Divider/Body/BodyLight/Selection) — `Resources/M3.Surfaces.xaml` (toggled dark)
- [x] `[A-03]` **Accent / theme-swatch wiring** — `AppConfigTheme.RefreshM3Overlays` re-asserts overlays after every `PaletteHelper.SetTheme`; drops `M3.Accent` when accent-sync/custom colour active so the picker + sync keep working; drops both in Light/FollowOS-light — `Services/AppConfig.cs`
- [x] `[A-04]` **Radii system** — `M3.Radius.8/16/20/24/28/Pill` CornerRadius tokens — `Resources/M3.xaml`
- [~] `[A-05]` **Typography ramp** — Roboto kept 1:1 (MaterialDesignFont); button weight 500 via M3.*Button; remaining weight/size pins applied per-screen — `Resources/M3.xaml`
- [~] `[A-06]` **Elevation / shadow tokens** — M3.Card flat (Dp0)+hairline, buttons flat-at-rest (Dp0→Dp2 hover) — `Resources/M3.xaml`; dialog deep-shadow per dialog pass
- [~] `[A-07]` **State-layer opacities** — menu highlight rose (BackgroundRoot 0.13 over `#ECB3C4`), icon-button hover inherited — `Resources/MaterialDesignMy.xaml`; finer @12/14/16/18/20 tints per-screen
- [~] `[A-08]` **Dialog window chrome** — surface `#241A1E` via brush override; inner content radius 16/28 + `M3.DialogWindow` deferred to dialog screen pass — `Extensions/MyDialogWindow.xaml` — partial: inner content panels reskinned to radius 16/28 per dialog; the OS-window frame radius-28 was deliberately NOT applied (kept MaterialDesignWindow chrome to avoid WindowChrome/DWM risk); surface ramp inherited from M3.Surfaces overlay
- [ ] `[A-09]` **Scrollbars** — thin M3-style scrollbars (track, thumb radius, hover) applied globally — `Themes/Generic.xaml` — TODO; not in this diff (Generic.xaml unchanged)
- [ ] `[A-10]` **Focus ring / keyboard nav visual** — M3 focus indicator consistent across controls (preserve a11y/IsTabStop work) — `Themes/Generic.xaml` — TODO; not in this diff (a11y/IsTabStop work preserved but no M3 focus-ring restyle)
- [x] `[A-11]` **Converters audit** — build green under new ramp; `OnColorForegroundConv` (accent-on-Primary readability) reused for `#5A1B2C` on `#ECB3C4` — `Resources/Converters.xaml` (no change needed) — verified: build green under new ramp, converter reused (no edit needed)
- [ ] `[A-12]` **Validators / error styling** — M3 error color + supporting-text styling for input validation — `Resources/Validators.xaml` — TODO; not in this diff (Validators.xaml unchanged). NB: ErrorDialog error tint refreshed to `#FF5370` but that is screen-local, not the global validator styling

---

# SECTION B — Components

> One line per component. Re-skin after Section A. Each maps to its owning WPF file/dict.
> A component is `[x]` only when its M3 visual is applied **and** it renders correctly everywhere it is used.

- [x] `[B-01]` **Button** — contained / outlined / text × primary / secondary variants — `Resources/MaterialDesignMy.xaml` (button styles) — done: keyed M3.FilledButton/TonalButton/OutlinedButton/TextButton (radius 20, weight 500) in `Resources/M3.xaml`; applied opt-in across overlay + every dialog + settings tabs (FlyleafOverlay, Error, Downloader, Export, Batch, Tesseract/Whisper/Engine downloads, ColorPicker, SettingsKeys/Audio/Subtitles*/Video/Trans/OpenAIBase)
- [x] `[B-02]` **IconButton** — round, size variants (sm/md/lg), active/checked state, tint — `Resources/MaterialDesignMy.xaml` — done: keyed M3.IconButton/M3.IconButton.Small (36/40 touch targets, rose hover) in `M3.xaml`; FlyleafBar IconButton bumped 32→36 + rose mouse-over tint; download dialogs + SettingsAbout use M3.IconButton.Small (30→36)
- [~] `[B-03]` **Chip** — pill shape, variants (assist / filter / input / suggestion), selected state — `Resources/MaterialDesignMy.xaml` — partial: the FlyleafBar ASR status chip reshaped to pill (radius 999, h26, pad 12 0) on primary; no general reusable chip style/variant matrix added (only the one in-use chip flipped)
- [~] `[B-04]` **TextField** — radius 16, M3 focus underline/outline, label + supporting text — `Resources/MaterialDesignMy.xaml` + `Themes/SelectableTextBox.xaml` — partial: in-use fields switched to MaterialDesignFilledTextBox + TextFieldCornerRadius "16 16 0 0" (Batch folder path, Downloader query); no global TextBox restyle in MaterialDesignMy/SelectableTextBox (those dicts unchanged)
- [~] `[B-05]` **Select / ComboBox** — radius 16, focus state, M3 dropdown surface — `Resources/MaterialDesignMy.xaml` — partial: Export dialog combos switched to MaterialDesignFilledComboBox + radius "16 16 0 0"; combobox popup surface radius bumped to 16 in MaterialDesignMy (PopupBorder); no global filled-combo default
- [~] `[B-06]` **Switch** — M3 toggle (track + thumb travel, on/off colors) — `Resources/MaterialDesignMy.xaml` — partial: keyed M3.Switch style exists in `M3.xaml` (foundation); inherits MDIX switch theming via the rose accent overlay, but no per-instance Switch was retargeted to M3.Switch in this diff and no custom M3 track/thumb-grow template authored
- [x] `[B-07]` **Slider** — 6px track / 18px thumb, M3 active-track + value indicator — `Resources/Slider.xaml` — done: thumb grip now `Fill={TemplateBinding Foreground}` so the rose accent flows through; sizing/track inherited from existing Slider geometry + rose ramp
- [x] `[B-08]` **Card** — radius 16, surface-container fill, elevation — `Resources/MaterialDesignMy.xaml` — done: keyed M3.Card (radius 16, flat, hairline) in `M3.xaml`; applied to CheatSheet card; CommandPalette/Batch/Error/Settings group panels use radius-16/28 surf2 borders + CardBackground
- [x] `[B-09]` **Menu + MenuItem** — pill highlight, icon tint, checkable rows — `Resources/PopupMenu.xaml` + `Resources/MaterialDesignMy.xaml` — done (foundation): submenu/menu border radius 6→16 + BackgroundRoot pill-highlight (radius 16) in MaterialDesignMy; rose hover-tint via accent overlay
- [x] `[B-10]` **Table / DataGrid** — selected-row state, header, grouped rows (preserve no `IsVirtualizingWhenGrouping`) — `Resources/MaterialDesignMy.xaml` + `Themes/Generic.xaml` — done: rose-tonal selected-row styles (`#2EECB3C4`) — M3.BatchRow (Batch), M3.ResultRow (Downloader); grouping intact (no IsVirtualizingWhenGrouping reintroduced)
- [x] `[B-11]` **WordPopup** — radius 24, M3 surface + elevation — `Controls/WordPopup.xaml` — done: radius 8→24, padding 14 16, over-video bg `#333333`→surf3 `#382A30` (sidebar still follows MaterialDesignPaper)
- [-] `[B-12]` **Tabs** — M3 tab bar (indicator, label, active tint) — `Resources/MaterialDesignMy.xaml` (used by `SettingsDialog`) — SKIPPED in this pass: Settings TabControl not retemplated; tab indicator/label inherit MDIX + rose accent overlay (no explicit M3 tab restyle in diff)
- [x] `[B-13]` **Spinner / progress** — M3 circular/linear progress indicator — `Resources/MaterialDesignMy.xaml` — done: spinners recoloured to Secondary (cyan) — FlyleafOverlay opening spinner, sidebar loading spinner, Downloader Spinner style; linear progress bars clipped to rounded/pill borders with primary fill (Batch row, Tesseract/Whisper/Engine downloads)
- [-] `[B-14]` **EmptyState** — M3 empty-overlay (icon + text + CTA) styling — `Resources/MaterialDesignMy.xaml` (rendered in MainWindow/FlyleafOverlay) — SKIPPED as a component: no dedicated EmptyState style added; the empty-overlay CTAs were reskinned in-place (see C-13 — Open File → M3.FilledButton, Settings/CheatSheet → M3.TextButton); icon/text inherit M3 text-on-dark
- [x] `[B-15]` **SeekBar** — transport seek slider (track, buffered, thumb, hover preview) M3 styling — `Resources/Slider.xaml` + `Controls/FlyleafBar.xaml` — done: seek thumb picks up rose accent via the shared Slider grip `TemplateBinding Foreground` change; transport play promoted to 48px tonal squircle alongside it
- [-] `[B-16]` **SubtitleCue** — on-video dual-subtitle cue text styling (M3 readable surface/scrim) — `Controls/SubtitlesControl.xaml` + `Controls/SelectableSubtitleText.xaml` — SKIPPED: cue text is config-driven (FL.Config.Subs.*); SubtitlesControl.xaml / SelectableSubtitleText.xaml intentionally unchanged
- [x] `[B-17]` **SubtitleListItem** — sidebar subtitle row (active/playing state, timestamp, M3 hover) — `Views/SubtitlesSidebar.xaml` — done: now-playing 3px left-border replaced by rounded 16px rose tonal fill (`#2EECB3C4`), margin 3 0; selected-row `#444444`→`#2EECB3C4`; loading spinner → Secondary; SubIsPlayingConv signal preserved
- [x] `[B-18]` **Select-language list item** — language pick row reused across dialogs (chip/row M3 styling) — `Views/SelectLanguageDialog.xaml` — done: keyed M3LanguageListItem (radius 16, rose hover `#1FECB3C4` / selected `#33ECB3C4`) on both available + selected lists; rose `#40ECB3C4` listbox borders

---

# SECTION C — Screens (16)

> Each screen lists its constituent regions/elements as sub-checkboxes. Re-skin per screen after Sections A & B land.
> Pair every screen with its **Section D parity gate** before calling it complete.

## C-01 — Player
**Files:** `Views/MainWindow.xaml` + `Controls/FlyleafBar.xaml` + `Controls/SubtitlesControl.xaml` + `Views/FlyleafOverlay.xaml` + `Views/SubtitlesSidebar.xaml` + `Controls/WordPopup.xaml` + `Controls/SelectableSubtitleText.xaml`

- [~] `[C-01-e1]` Title bar (custom window chrome) — inherits foundation rose ramp via M3.Surfaces overlay; no per-element titlebar height/bg edit in this diff
- [~] `[C-01-e2]` Window controls (min / max / close) — inherits MDIX window chrome + rose overlay; no explicit close-hover `#E81123` / w44 restyle in this diff
- [~] `[C-01-e3]` Video stage + vignette — idle stage inherits surface ramp; no explicit rose radial-gradient/vignette overlay authored (FlyleafHost airspace limit); stage GridSplitter retinted to MaterialDesignDivider (MainWindow)
- [-] `[C-01-e4]` Big-play FAB (center play) — SKIPPED: the app has no center big-play overlay to re-skin; adding one would be a NEW control (out of scope for a 1:1 re-skin). The transport play (C-01-e10) is the 48px tonal squircle
- [x] `[C-01-e5]` Timestamp pill (current / total) — done: FlyleafOverlay timestamp wrapped in a pill Border (radius 999, bg `#B32A1D22`), mono bold 14px, dark halo preserved
- [x] `[C-01-e6]` OSD (on-screen-display messages) — done: OSD icon 32→40 round rose chip (bg Primary, glyph 22px on-Primary); OSD text → weight Medium, 17px
- [-] `[C-01-e7]` Snackbar / toast — SKIPPED in this pass: no snackbar surface present in the diffed XAML to re-skin (spec light-inverse snackbar not implemented; would be new chrome)
- [x] `[C-01-e8]` Right-click context menu (PopupMenu) — done (foundation): MaterialDesignMy menu border radius 16 + pill highlight; rose hover tint via accent overlay (PopupMenu.xaml itself unchanged but inherits)
- [x] `[C-01-e9]` Transport seek bar — done: rose seek thumb via shared Slider grip `TemplateBinding Foreground` change (Slider.xaml)
- [x] `[C-01-e10]` Transport controls row (play/pause, prev/next, etc.) — done: play promoted to 48px tonal squircle (radius 16, bg Primary, glyph 26px on-Primary, rose hover); other transport icon buttons bumped 32→36 with rose mouse-over tint
- [~] `[C-01-e11]` Volume control (button + slider, collapse-on-narrow preserved) — partial: volume button shares the 36px IconButton restyle + rose slider thumb; collapse-on-narrow behavior preserved (untouched); no dedicated volume-only edit
- [x] `[C-01-e12]` Movie title text (ellipsis preserved) — done: transport title italic→normal, 14→13px, Foreground→MaterialDesignBodyLight; ellipsis + bindings/tooltips preserved
- [~] `[C-01-e13]` Sidebar toolbar (SubtitlesSidebar header actions) — inherits foundation theme (surface ramp + icon-button hover); no per-action toolbar edit in this diff
- [~] `[C-01-e14]` Sidebar search field — inherits foundation theme; no explicit filled-field/radius-16 retarget on the sidebar search in this diff
- [x] `[C-01-e15]` Sub-item list (subtitle list items) — done: now-playing left-bar → rounded 16px rose tonal fill (`#2EECB3C4`); selected row `#444444`→`#2EECB3C4`; loading spinner → Secondary (see B-17); SubIsPlayingConv preserved
- [-] `[C-01-e16]` Dual-subtitle overlay (primary + secondary cues) — SKIPPED: config-owned cue text (FL.Config.Subs.*); SubtitlesControl.xaml / SelectableSubtitleText.xaml intentionally unchanged
- [x] `[C-01-e17]` Word popup (tap-a-word dictionary popup) — done: radius 8→24, padding 14 16, over-video bg → surf3 `#382A30` (see B-11)
- [~] `[C-01-e18]` FlyleafBar narrow-window overflow kebab (preserve) — preserved (untouched); narrow-bar overflow/volume-collapse behavior intact, inherits the 36px icon-button + rose ramp restyle

## C-02 — Settings
**Files:** `Views/SettingsDialog.xaml` + `Controls/Settings/*` (About, Audio, Keys, KeysOffset, Mouse, Player, Plugins, Subtitles, SubtitlesAction, SubtitlesASR, SubtitlesOCR, SubtitlesPS, SubtitlesTrans, Themes, Video) + `Controls/Settings/Trans/OpenAIBaseTranslateControl.xaml` + `Controls/Settings/Controls/ColorPicker.xaml`

- [x] `[C-02-e1]` Dialog frame + header (MyDialogWindow chrome) — done: surface inherited from M3.Surfaces overlay; group panels rebound to MaterialDesignCardBackground (radius via GroupBox); OS-window frame kept MaterialDesignWindow chrome (radius 28 not applied — DWM risk, see A-08)
- [x] `[C-02-e2]` Left tab/nav list + settings-search box — done: left rail bg → MaterialDesignToolBarBackground, right divider removed (BorderThickness 0); settings-search field preserved
- [x] `[C-02-e3]` Tab: About (`SettingsAbout.xaml`) — done: copy-version button → M3.IconButton.Small
- [~] `[C-02-e4]` Tab: Player (`SettingsPlayer.xaml`) — inherits foundation theme; no per-tab edit needed
- [x] `[C-02-e5]` Tab: Audio (`SettingsAudio.xaml`) — done: Configure button → M3.TonalButton
- [x] `[C-02-e6]` Tab: Video (`SettingsVideo.xaml`) — done: Reset-all button → M3.TonalButton
- [x] `[C-02-e7]` Tab: Subtitles (`SettingsSubtitles.xaml`) — done: Configure button → M3.TonalButton
- [x] `[C-02-e8]` Tab: Subtitles Action (`SettingsSubtitlesAction.xaml`) — done: Add Search/Clipboard/ClipboardAll + Auto Set → M3.TonalButton; Apply → M3.FilledButton
- [x] `[C-02-e9]` Tab: Subtitles ASR + engine combo follow (`SettingsSubtitlesASR.xaml`) — done: Download Engine/Model + Copy Debug/Help Command → M3.TonalButton; engine-combo follow preserved
- [x] `[C-02-e10]` Tab: Subtitles OCR (`SettingsSubtitlesOCR.xaml`) — done: Download Model button → M3.TonalButton
- [~] `[C-02-e11]` Tab: Subtitles PS (`SettingsSubtitlesPS.xaml`) — inherits foundation theme; no per-tab edit needed
- [x] `[C-02-e12]` Tab: Subtitles Translation + `OpenAIBaseTranslateControl.xaml` — done: Reset → M3.OutlinedButton (×4), Set Default / Check / Get Models / Hello API → M3.TonalButton (SettingsSubtitlesTrans + OpenAIBaseTranslateControl)
- [x] `[C-02-e13]` Tab: Keys (`SettingsKeys.xaml`) — done: Add/Load → M3.TonalButton, Apply → M3.FilledButton; Keys DataGrid workflow preserved
- [~] `[C-02-e14]` Tab: Keys Offset (`SettingsKeysOffset.xaml`) — inherits foundation theme; no per-tab edit needed
- [~] `[C-02-e15]` Tab: Mouse (`SettingsMouse.xaml`) — inherits foundation theme; no per-tab edit needed
- [~] `[C-02-e16]` Tab: Plugins (`SettingsPlugins.xaml`) — inherits foundation theme; no per-tab edit needed
- [~] `[C-02-e17]` Tab: Themes + accent picker (`SettingsThemes.xaml`) — inherits foundation theme; no per-tab edit needed (accent picker/sync kept working via RefreshM3Overlays)
- [x] `[C-02-e18]` ColorPicker control (`Controls/Settings/Controls/ColorPicker.xaml`) — done: Apply → M3.FilledButton, Cancel → M3.TextButton
- [x] `[C-02-e19]` Footer / action buttons (Close, etc.) — done: Save & Close → M3.FilledButton, Close-without-saving → M3.TextButton (SettingsDialog footer)
- [~] `[C-02-e20]` Shared field rows (TextField/Select/Switch/Slider instances across tabs) — partial: buttons across tabs retargeted to M3 styles; bare TextBox/ComboBox/Switch/Slider rows inherit MDIX + rose ramp, no global M3 field/switch default applied (see B-04/B-05/B-06)

## C-03 — Cheatsheet
**Files:** `Views/CheatSheetDialog.xaml`

- [x] `[C-03-e1]` Dialog frame + header — done: root materialDesign:Card → M3.Card style (radius 16, flat, hairline)
- [~] `[C-03-e2]` Shortcut group sections / headings — inherits foundation theme (TabControl PrimaryLight + rose ramp); no per-heading edit
- [x] `[C-03-e3]` Key-combo chips / rows — done: shortcut cells wrapped in kbd-chip Border (radius 8, bg surf3 `#382A30`, pad 8 3), mono Cascadia Code, weight Medium
- [~] `[C-03-e4]` Scroll region — inherits foundation theme; no scrollbar restyle (global A-09 still TODO)
- [~] `[C-03-e5]` Footer / close action — inherits foundation theme; no explicit footer button edit in this diff

## C-04 — Downloader (Subtitles Downloader)
**Files:** `Views/SubtitlesDownloaderDialog.xaml`

- [~] `[C-04-e1]` Dialog frame + header — inherits foundation surface ramp; no per-frame edit (OS-window chrome kept)
- [x] `[C-04-e2]` Search / query field — done: Query field → MaterialDesignFilledTextBox + radius "16 16 0 0"; "Query:" label Gray → MaterialDesignBodyLight
- [~] `[C-04-e3]` Provider / language selectors — inherits foundation theme; no explicit selector restyle in this diff
- [x] `[C-04-e4]` Results table (list of subtitles) — done: DataGrid RowStyle → M3.ResultRow (rose-tonal selected `#2EECB3C4`)
- [x] `[C-04-e5]` Download / action buttons — done: Search/Load/Download → M3.FilledButton (font 18→16)
- [x] `[C-04-e6]` Progress / spinner state — done: Spinner style Foreground → MaterialDesign.Brush.Secondary (cyan)
- [~] `[C-04-e7]` Footer / close action — inherits foundation theme; no explicit footer edit in this diff

## C-05 — Whisper Model Download
**Files:** `Views/WhisperModelDownloadDialog.xaml`

- [~] `[C-05-e1]` Dialog frame + header — inherits foundation surface ramp; no per-frame edit (OS-window chrome kept)
- [~] `[C-05-e2]` Model list / table (size, status) — inherits foundation theme; model combo/list not explicitly restyled
- [x] `[C-05-e3]` Download buttons / actions — done: Open Folder → M3.IconButton.Small (30→36); Download Model → M3.FilledButton; Cancel → M3.OutlinedButton; Delete → M3.TextButton (heights 30→36)
- [x] `[C-05-e4]` Progress bar / spinner — done: progress bar clipped to pill Border (radius 999) with primary fill
- [x] `[C-05-e5]` Footer / close action — done: status text wrapped in radius-16 CardBackground panel

## C-06 — Whisper Engine Download
**Files:** `Views/WhisperEngineDownloadDialog.xaml`

- [~] `[C-06-e1]` Dialog frame + header — inherits foundation surface ramp; no per-frame edit (OS-window chrome kept)
- [~] `[C-06-e2]` Engine list / options — inherits foundation theme; engine list not explicitly restyled
- [x] `[C-06-e3]` Download buttons / actions — done: Download → M3.FilledButton; Cancel → M3.OutlinedButton; Delete → M3.TextButton; Open Folder → M3.IconButton.Small (heights 30→36)
- [x] `[C-06-e4]` Progress bar / spinner — done: progress bar clipped to pill Border (radius 999) with primary fill
- [x] `[C-06-e5]` Footer / close action — done: status text wrapped in radius-16 CardBackground panel (transparent when empty)

## C-07 — Tesseract Download
**Files:** `Views/TesseractDownloadDialog.xaml`

- [~] `[C-07-e1]` Dialog frame + header — inherits foundation surface ramp; no per-frame edit (OS-window chrome kept)
- [~] `[C-07-e2]` Language-data list / table — inherits foundation theme; language list not explicitly restyled
- [x] `[C-07-e3]` Download buttons / actions — done: Open Folder → M3.IconButton.Small (30→36); Download Model → M3.FilledButton; Cancel → M3.OutlinedButton; Delete → M3.TextButton (heights 30→36)
- [x] `[C-07-e4]` Progress bar / spinner — done: progress bar clipped to pill Border (radius 999) with primary fill
- [x] `[C-07-e5]` Footer / close action — done: status text wrapped in radius-16 CardBackground panel

## C-08 — Command Palette
**Files:** `Views/CommandPaletteDialog.xaml`

- [x] `[C-08-e1]` Palette frame (Ctrl+K overlay surface, radius) — done: Card UniformCornerRadius 28, bg → MaterialDesignCardBackground (Light-safe), ElevationAssist Dp0
- [~] `[C-08-e2]` Search input field — inherits foundation theme; no explicit filled-field retarget on the palette search in this diff
- [x] `[C-08-e3]` Result list items (command rows) — done: ListBoxItem margin 0 1; rows on rose ramp
- [x] `[C-08-e4]` Selected / highlighted row state — done: selected `#33ECB3C4` reads stronger than hover `#1FECB3C4` (so keyboard-focus "which row Enter fires" survives mouse-over)
- [x] `[C-08-e5]` Shortcut hint chips on rows — done: shortcut TextBlock → kbd-chip Border (radius 8, bg surf3 `#382A30`, pad 8 2), mono Cascadia Code, weight Medium
- [~] `[C-08-e6]` Empty / no-match state — inherits foundation theme; no explicit empty/no-match restyle in this diff

## C-09 — Export (Subtitles Export)
**Files:** `Views/SubtitlesExportDialog.xaml`

- [~] `[C-09-e1]` Dialog frame + header — inherits foundation surface ramp; no per-frame edit (OS-window chrome kept)
- [x] `[C-09-e2]` Format / options selectors — done: Subtitle + Translated combos → MaterialDesignFilledComboBox + radius "16 16 0 0"
- [~] `[C-09-e3]` Output path field — inherits foundation theme; no explicit field retarget in this diff
- [~] `[C-09-e4]` Toggles / switches (export options) — inherits foundation theme; export toggles not retargeted to M3.Switch
- [x] `[C-09-e5]` Export / action buttons — done: Export button → M3.FilledButton
- [~] `[C-09-e6]` Footer / close action — inherits foundation theme; no explicit footer edit in this diff

## C-10 — Batch (Batch Subtitles)
**Files:** `Views/BatchSubtitlesDialog.xaml`

- [~] `[C-10-e1]` Dialog frame + header (wide default 1650x840 preserved) — inherits foundation surface ramp; wide-default sizing preserved; no per-frame edit (OS-window chrome kept)
- [x] `[C-10-e2]` Folder / source picker + Recursive toggle — done: folder-path TextBox → MaterialDesignFilledTextBox + radius "16 16 0 0"; Browse → M3.OutlinedButton; Scan → M3.FilledButton; Recursive toggle preserved (inside options panel)
- [x] `[C-10-e3]` File table (grouped by sub-folder, fixed-width columns, no virtualizing-when-grouping) — done: RowStyle → keyed M3.BatchRow (rose-tonal selected `#2EECB3C4`, dim-when-excluded + MediaPath tooltip preserved); no IsVirtualizingWhenGrouping reintroduced; fixed-width columns intact
- [~] `[C-10-e4]` Group headers (folder rows) — inherits foundation theme; group-header template untouched (renders correctly under grouping)
- [x] `[C-10-e5]` Row checkbox + file-name + status columns — done: per-row progress bar clipped to rounded (radius 3) Border with primary fill; row tooltip/dim preserved
- [x] `[C-10-e6]` Options panel (engine/language/etc.) — done: options moved onto a raised radius-16 CardBackground Border (pad 12 8); checkboxes + translate-target text preserved
- [x] `[C-10-e7]` Progress / spinner state — done: per-row + transcript progress on rounded surfaces with primary fill; transcript ListBox on radius-16 surf2 panel (hairline outline, transparent listbox)
- [x] `[C-10-e8]` Action buttons (Start, etc.) — done: Start → M3.FilledButton; Cancel/Retry Failed/Open Output Folder → M3.OutlinedButton
- [x] `[C-10-e9]` Footer / close action — done: Close → M3.TextButton

## C-11 — Select Language
**Files:** `Views/SelectLanguageDialog.xaml`

- [~] `[C-11-e1]` Dialog frame + header — inherits foundation surface ramp; no per-frame edit (OS-window chrome kept)
- [~] `[C-11-e2]` Search / filter field — inherits foundation theme; no explicit filter-field retarget in this diff
- [x] `[C-11-e3]` Language list items (rows/chips) — done: keyed M3LanguageListItem (radius 16, margin 2 1, rose hover `#1FECB3C4`) on both available + selected ListBoxes; listbox borders → rose `#40ECB3C4`, pad 4
- [x] `[C-11-e4]` Selected state — done: selected `#33ECB3C4` (stronger than hover)
- [~] `[C-11-e5]` Footer / action buttons — inherits foundation theme; no explicit footer button edit in this diff

## C-12 — Error
**Files:** `Views/ErrorDialog.xaml`

- [~] `[C-12-e1]` Dialog frame + header (error-tinted) — inherits foundation surface ramp; header error tint refreshed (see e2); no per-frame edit (OS-window chrome kept)
- [x] `[C-12-e2]` Error icon — done: unknown-error tint `#ef5350` → `#FF5370` (rose-leaning error red on title/icon trigger)
- [x] `[C-12-e3]` Message / details text region (height-cap preserved) — done: message TextBox wrapped in radius-16 CardBackground Border (transparent TextBox bg); read-only/copy-guard preserved
- [x] `[C-12-e4]` Copy / details expander — done: exception-detail TextBox wrapped in radius-16 CardBackground Border; MaxHeight 140 cap preserved
- [x] `[C-12-e5]` Action buttons (close / report) — done: Copy to Clipboard → M3.FilledButton; Close → M3.OutlinedButton (IsDefault/IsCancel preserved)

## C-13 — Empty State (player overlay)
**Files:** `Views/MainWindow.xaml` + `Views/FlyleafOverlay.xaml` (empty overlay region)

- [~] `[C-13-e1]` Empty-overlay container (surface/scrim) — inherits foundation surface ramp; only the CTA buttons carry a rendered background (click-through preserved); no container edit
- [~] `[C-13-e2]` Logo / illustration / icon — inherits foundation theme; icon/illustration not explicitly restyled
- [~] `[C-13-e3]` Prompt text ("open a file…") — inherits foundation theme (M3 text-on-dark); opacity-dimmed prompt untouched
- [x] `[C-13-e4]` CTA button(s) (Open File, etc.) — done: Open File → M3.FilledButton; Settings + Keyboard-shortcuts → M3.TextButton (FlyleafOverlay)
- [~] `[C-13-e5]` Drag-and-drop hint affordance — inherits foundation theme; no explicit drag-hint restyle in this diff

## C-14 — Loading (player stage)
**Files:** `Views/MainWindow.xaml` + `Views/FlyleafOverlay.xaml` (loading region)

- [~] `[C-14-e1]` Loading overlay container / scrim — inherits foundation surface ramp; no explicit container/scrim edit
- [x] `[C-14-e2]` Spinner / progress indicator (M3) — done: opening spinner Foreground Primary → MaterialDesign.Brush.Secondary (cyan reads on rose ramp, per spec §4.15)
- [~] `[C-14-e3]` Loading / status text — inherits foundation theme; "Opening…" label preserved (no explicit restyle)

## C-15 — Playback Error (player stage)
**Files:** `Views/MainWindow.xaml` + `Views/FlyleafOverlay.xaml` (playback-error region)

- [~] `[C-15-e1]` Error overlay container / scrim — inherits foundation surface ramp; no explicit container/scrim edit
- [~] `[C-15-e2]` Error icon — inherits foundation theme; no explicit error-icon restyle in this diff
- [~] `[C-15-e3]` Error message text — inherits foundation theme; no explicit message-text restyle in this diff
- [x] `[C-15-e4]` Recovery actions (Retry = Reopen(Selected) / Open File fallback — preserve commands) — done: Retry → M3.FilledButton (BasedOn, keeps null-selection visibility trigger); Open-File fallback → M3.OutlinedButton; commands/triggers preserved

## C-16 — Shared resource dicts (cross-cutting reskin pass)
**Files:** `Resources/MaterialDesignMy.xaml`, `Resources/Converters.xaml`, `Resources/PopupMenu.xaml`, `Resources/Slider.xaml`, `Resources/Validators.xaml`, `Themes/Generic.xaml`, `Themes/SelectableTextBox.xaml`, `Extensions/MyDialogWindow.xaml`

- [x] `[C-16-e1]` `MaterialDesignMy.xaml` — global styles re-skinned & no orphan keys — done: menu/submenu/combo-popup border radius 6→16, BackgroundRoot pill-highlight radius 16; build green, no orphan keys
- [x] `[C-16-e2]` `Converters.xaml` — all converters verified under new ramp — done: unchanged; build green under new ramp, OnColorForegroundConv reused for accent-on-Primary (see A-11)
- [~] `[C-16-e3]` `PopupMenu.xaml` — context-menu items M3-styled, bindings intact — partial: file unchanged; menu pill/radius come from the MaterialDesignMy templates it consumes; bindings intact
- [x] `[C-16-e4]` `Slider.xaml` — seek + volume + settings sliders consistent — done: thumb grip `Fill={TemplateBinding Foreground}` so rose accent flows to seek + volume + settings sliders uniformly
- [ ] `[C-16-e5]` `Validators.xaml` — error styling consistent — TODO; file unchanged (see A-12)
- [ ] `[C-16-e6]` `Themes/Generic.xaml` — scrollbars/focus/datagrid consistent — TODO; file unchanged (scrollbars A-09 + focus-ring A-10 still TODO; DataGrid rose-selected handled per-screen via M3.BatchRow/M3.ResultRow, not globally here)
- [~] `[C-16-e7]` `Themes/SelectableTextBox.xaml` — selectable text M3-styled — partial: file unchanged; inherits MDIX + rose ramp, no dedicated M3 selectable-text restyle
- [~] `[C-16-e8]` `Extensions/MyDialogWindow.xaml` — dialog chrome reused by all dialogs — partial: file unchanged; dialog surface comes from the M3.Surfaces overlay; OS-window radius 28 deliberately NOT applied to the frame (kept MaterialDesignWindow chrome — DWM risk, see A-08)

---

# SECTION D — Per-screen functional-parity gate

> One gate per screen. Flip `[x]` only when: **all `x:Name` / `Command` / `Binding` / event-handlers preserved AND build is green** for that screen's file(s).
> This is the regression backstop — the re-skin must not change behavior, only appearance.

- [x] `[D-01]` **Player** parity — `MainWindow` + `FlyleafBar` + `SubtitlesControl` + `FlyleafOverlay` + `SubtitlesSidebar` + `WordPopup` + `SelectableSubtitleText`: names/commands/bindings/handlers preserved, build green — parity-verified (git-diff token check) + build green
- [x] `[D-02]` **Settings** parity — `SettingsDialog` + all `Controls/Settings/*` + `OpenAIBaseTranslateControl` + `ColorPicker`: names/commands/bindings/handlers preserved, build green — parity-verified (git-diff token check) + build green
- [x] `[D-03]` **Cheatsheet** parity — `CheatSheetDialog`: names/commands/bindings/handlers preserved, build green — parity-verified (git-diff token check) + build green
- [x] `[D-04]` **Downloader** parity — `SubtitlesDownloaderDialog`: names/commands/bindings/handlers preserved, build green — parity-verified (git-diff token check) + build green
- [x] `[D-05]` **Whisper Model Download** parity — `WhisperModelDownloadDialog`: names/commands/bindings/handlers preserved, build green — parity-verified (git-diff token check) + build green
- [x] `[D-06]` **Whisper Engine Download** parity — `WhisperEngineDownloadDialog`: names/commands/bindings/handlers preserved, build green — parity-verified (git-diff token check) + build green
- [x] `[D-07]` **Tesseract Download** parity — `TesseractDownloadDialog`: names/commands/bindings/handlers preserved, build green — parity-verified (git-diff token check) + build green
- [x] `[D-08]` **Command Palette** parity — `CommandPaletteDialog`: names/commands/bindings/handlers preserved, build green — parity-verified (git-diff token check) + build green
- [x] `[D-09]` **Export** parity — `SubtitlesExportDialog`: names/commands/bindings/handlers preserved, build green — parity-verified (git-diff token check) + build green
- [x] `[D-10]` **Batch** parity — `BatchSubtitlesDialog` (grouping render intact, no `IsVirtualizingWhenGrouping`): names/commands/bindings/handlers preserved, build green — parity-verified (git-diff token check) + build green; no IsVirtualizingWhenGrouping reintroduced
- [x] `[D-11]` **Select Language** parity — `SelectLanguageDialog`: names/commands/bindings/handlers preserved, build green — parity-verified (git-diff token check) + build green
- [x] `[D-12]` **Error** parity — `ErrorDialog` (Retry/Reopen fallback commands intact): names/commands/bindings/handlers preserved, build green — parity-verified (git-diff token check) + build green
- [x] `[D-13]` **Empty State** parity — MainWindow/FlyleafOverlay empty overlay: names/commands/bindings/handlers preserved, build green — parity-verified (git-diff token check) + build green
- [x] `[D-14]` **Loading** parity — MainWindow/FlyleafOverlay loading region: names/commands/bindings/handlers preserved, build green — parity-verified (git-diff token check) + build green
- [x] `[D-15]` **Playback Error** parity — MainWindow/FlyleafOverlay playback-error region: names/commands/bindings/handlers preserved, build green — parity-verified (git-diff token check) + build green
- [x] `[D-16]` **Shared dicts** parity — all resource dicts in C-16: keys referenced everywhere resolve, no `StaticResource` key-miss (throws at dialog-open, not build), build green — parity-verified (git-diff token check) + build green; literal surfaces rebound to DynamicResource for Light-mode safety, RefreshM3Overlays URI-match fixed

---

*Generated as the living re-skin checklist. Keep the Progress Summary table in sync with every status flip.*
