# M3 Re-skin — Foundation Change Plan (theme + shared resource dictionaries)

Scope: the SHARED foundation every screen inherits — color ramp, radii, component style
overrides, dialog chrome, and `App.xaml` merge order. Per-screen XAML edits are out of
scope and covered by separate screen plans.

Stack (verified): WPF / .NET 10, **MaterialDesignThemes 5.3.1** (MDIX2, namespaced
`MaterialDesign.Brush.*` keys coexist with legacy `MaterialDesignXxx` keys), MaterialDesignColors.
Current brand: `App.xaml` → `<materialDesign:CustomColorTheme BaseTheme="Dark" PrimaryColor="#D23D6F" SecondaryColor="#00B8D4" />`.

Target M3 ramp (rose-tinted dark):
`--bg #1a1216 / --surf #241a1e / --surf2 #2d2025 / --surf3 #382a30`,
primary container `#ECB3C4` on `#5a1b2c`, secondary `#7fd8e6` on `#00363f`,
secondary-container `#2a4248`, outline `rgba(236,179,196,0.25)`, titlebar `#2a1d22`.

---

## 0. Verified facts from the current codebase

- `App.xaml` merge order (load-bearing, frozen in `wpf-design-contract.md` line 11):
  `CustomColorTheme` → `MaterialDesign2.Defaults.xaml` → `MaterialDesignMy.xaml` →
  `Converters.xaml` → `PopupMenu.xaml` → `Validators.xaml`.
  The comment in `App.xaml` explains *why*: `PopupMenu.xaml` uses `StaticResource` keys from
  `MaterialDesignMy.xaml` + `Converters.xaml` + the MDIX defaults, so it must stay AFTER them.
- `CustomColorTheme` is the brush generator: it derives the full `MaterialDesign.Brush.Primary*`
  / `.Secondary*` family and the dark neutral surfaces (`MaterialDesignPaper`,
  `MaterialDesign.Brush.Background`, `MaterialDesignBody`, `MaterialDesignDivider`,
  `MaterialDesignToolBarBackground`, etc.) from the two seed colors + `BaseTheme="Dark"`.
- Keys actually referenced across screens (so renaming/removing any of these breaks the build
  *or* silently de-themes a screen):
  - `MaterialDesignPaper` — `MyDialogWindow.xaml:5`, WordPopup sidebar bg, Settings, Whisper/Tesseract dialogs.
  - `MaterialDesignBody` — body text everywhere (FlyleafBar, sidebar, overlay, dialogs, WordPopup close icon).
  - `MaterialDesignDivider` — WordPopup border, BatchSubtitles, SettingsDialog separators.
  - `MaterialDesignToolBarBackground` — `SubtitlesSidebar.xaml` toolbar (110/160/168).
  - `MaterialDesignDataGrid` — Batch + Settings/Keys grids.
  - `MaterialDesignTextBox` — Settings text fields.
  - `MaterialDesign.Brush.Primary` (+ `.Light`, `.Dark`) — seek/sub accents, menu icons, sliders.
  - `MaterialDesign.Brush.Secondary` (+ `.Light`) — hyperlinks, WordPopup word, secondary sub hover.
  - `MaterialDesign.Brush.Foreground` / `MaterialDesign.Brush.Background` — Slider track, PopupMenu submenu bg.
- `MaterialDesignMy.xaml` re-templates `MenuItem` / `ContextMenu` and hardcodes `CornerRadius="6"`
  on submenu borders + menu-item background roots (lines 200, 287–310, 471).
- `Slider.xaml` (`FlyleafSlider`) reads track/bar/thumb sizes from the attached
  `local:SliderLayout.*` props (BarHeight / TrackHeight / ThumbHeight) — **bound at the call site**
  in `FlyleafBar.xaml` to `FL.Config.SeekBarHeight/TrackHeight/ThumbHeight`. The grip Ellipse is
  `MaterialDesign.Brush.Foreground`; the active track is `TemplateBinding Foreground` (i.e. the
  Slider's `Foreground`, set to `MaterialDesign.Brush.Primary` at the seek bar). Track corner radius
  is hardcoded `RadiusX/Y=2` (inactive) and `CornerRadius 3,0,0,3` (active).
- `MyDialogWindow.xaml` is a bare `<Window>` with `Style="{StaticResource MaterialDesignWindow}"`,
  `Background={DynamicResource MaterialDesignPaper}`, `Foreground={DynamicResource MaterialDesignBody}`.
  `MainWindow.xaml` also uses `Style="{StaticResource MaterialDesignWindow}"`. The window chrome
  (border/title bar) comes from MDIX's `MaterialDesignWindow` style, NOT from app XAML.

---

## 1. COLOR RAMP

### Recommendation: keep the seed, override brushes (lowest risk)

**Keep** `CustomColorTheme BaseTheme="Dark" PrimaryColor="#D23D6F" SecondaryColor="#00B8D4"`
**unchanged** as the tonal-palette generator (the M3 brief explicitly keeps `#D23D6F` as the
seed for tonal palettes). Do **not** change `PrimaryColor` to `#ECB3C4`: `CustomColorTheme`
auto-derives `.Light`/`.Dark`/`.Foreground` from the seed via HCT tone steps, so swapping the
seed shifts ~8 derived brushes unpredictably and changes the on-primary contrast pairing. Instead
**override the specific brush keys by re-declaring them** in a new dictionary merged *after*
`MaterialDesign2.Defaults`. Because the brushes are consumed via `DynamicResource` (verified —
every reference above is `DynamicResource`), a later same-key `SolidColorBrush` wins at lookup
time without touching the generator.

> Note: this is a redefine-by-merge-order override, not runtime `PaletteHelper` recoloring.
> The live theme switch (Settings ▸ Themes, `wpf-design-contract.md` line 7) calls
> `PaletteHelper.SetTheme`, which **regenerates** the neutral + primary/secondary brushes from
> the palette and will re-stomp these surface overrides on a Light/Follow-Windows switch. See
> RISK R7 — the foundation override is correct for the shipped dark default; live non-dark theme
> modes need the override re-applied in the theme-apply path (a screen/runtime concern, flagged here).

### Brush keys to override (exact MDIX 5.3.1 keys)

Surfaces / neutrals (rose-tinted ramp):

| Key | New value | Maps to M3 token |
|---|---|---|
| `MaterialDesignPaper` | `#1a1216` | window/page bg (`--bg`) |
| `MaterialDesign.Brush.Background` | `#241a1e` | surface (`--surf`); also PopupMenu submenu bg |
| `MaterialDesignBackground` (legacy alias) | `#241a1e` | keep both in sync if present |
| `MaterialDesignToolBarBackground` | `#241a1e` | sidebar toolbar (`--surf`) |
| `MaterialDesignCardBackground` | `#2d2025` | cards (`--surf2`) |
| `MaterialDesignChipBackground` | `#382a30` | neutral chip / field fill (`--surf3`) |
| `MaterialDesignTextFieldBoxBackground` | `#382a30` | filled field box (`--surf3`) |
| `MaterialDesignDivider` | `#40ECB3C4` (= `rgba(236,179,196,0.25)`) | outline |
| `MaterialDesignBody` | `#f3dde4` | primary on-surface text |
| `MaterialDesignBodyLight` | `#d8c2c9` | secondary/dim text (title 12px) |
| `MaterialDesignColumnHeader` | `#d8c2c9` | DataGrid header text |
| `MaterialDesignSelection` | `#2d2025` | hover/selection neutral |
| `MaterialDesign.Brush.Foreground` | `#f3dde4` | Slider inactive track + grip base |

Primary CONTAINER (lighter rose — the M3 "filled surface" color, replacing saturated `#D23D6F`):

| Key | New value | Notes |
|---|---|---|
| `MaterialDesign.Brush.Primary` | `#ECB3C4` | container fill; consumed by seek bar, menu icons, sub accents |
| `MaterialDesign.Brush.Primary.Foreground` | `#5a1b2c` | on-primary (text/icon on the rose fill) |
| `MaterialDesign.Brush.Primary.Light` | `#f3c6d3` | hover state |
| `MaterialDesign.Brush.Primary.Dark` | `#d99fb2` | pressed / selection-range |
| `MaterialDesignFlatButtonClick` | `#33ECB3C4` | ripple/click overlay |
| `MaterialDesignCheckBoxOff` | `#80ECB3C4` | unchecked stroke tint (optional polish) |

Secondary (kept teal `#7fd8e6`):

| Key | New value | Notes |
|---|---|---|
| `MaterialDesign.Brush.Secondary` | `#7fd8e6` | secondary sub hover, hyperlink, spinner |
| `MaterialDesign.Brush.Secondary.Foreground` | `#00363f` | on-secondary |
| `MaterialDesign.Brush.Secondary.Light` | `#a6e6f0` | hyperlink hover, WordPopup word |
| `MaterialDesign.Brush.Secondary.Dark` | `#5fc4d4` | pressed |

> Implementation note: declare each as a `<SolidColorBrush x:Key="..." Color="#..." />`. Where
> MDIX uses BOTH a namespaced key (`MaterialDesign.Brush.Primary`) and a legacy alias
> (`PrimaryHueMidBrush`), override BOTH only if the legacy one is referenced — none of the legacy
> `*HueMidBrush` aliases are referenced in this codebase, so the namespaced + `MaterialDesignXxx`
> keys above are sufficient. Verify after build that no `*HueMidBrush` lookup falls through.

---

## 2. RADII SYSTEM

Add reusable `CornerRadius` resources to `M3.xaml` (a `CornerRadius` is a value type; declare with
`xmlns:sys`/`x:Type CornerRadius` not needed — use `<CornerRadius x:Key=...>` literal):

```xml
<CornerRadius x:Key="M3.Radius.8"  TopLeft="8"  TopRight="8"  BottomRight="8"  BottomLeft="8"/>
<CornerRadius x:Key="M3.Radius.16" .../>   <!-- card / field / menu -->
<CornerRadius x:Key="M3.Radius.20" .../>   <!-- button (used by FilledButton) -->
<CornerRadius x:Key="M3.Radius.24" .../>   <!-- wordpopup -->
<CornerRadius x:Key="M3.Radius.28" .../>   <!-- dialog / palette / big-play FAB -->
<CornerRadius x:Key="M3.Radius.Pill" TopLeft="999" .../>   <!-- chips, menu items, icon buttons, timestamp pill -->
```

Cleanest application per control (MDIX 5.x exposes radius two ways — a `double`
`uniformCornerRadius` resource and per-style `CornerRadius` via `Border` properties):

- **Button** = pill via style: MDIX `MaterialDesignRaisedButton`/`MaterialDesignFlatButton`
  read `<system:Double x:Key="ButtonBorderCornerRadius">` (a Double, not a CornerRadius) inside
  the button template's `Border`. The robust override is to define our own `FilledButton` /
  `TonalButton` / `OutlinedButton` / `TextButton` styles `BasedOn` the MDIX styles and set
  `materialDesign:ButtonAssist.CornerRadius="20"` (the supported per-button attached property in
  5.x). Do NOT try to globally redefine the `ButtonBorderCornerRadius` double — it is `x:Shared`
  / template-internal and re-merged by `MaterialDesign2.Defaults`.
- **Card** (`materialDesign:Card`) — `UniformCornerRadius="16"` via a global `Card` style setter.
- **Field** (`TextBox`/`ComboBox` filled box) — `materialDesign:TextFieldAssist.TextBoxViewMargin`
  + the box uses `MaterialDesignTextFieldBoxBackground` (already overridden in §1); set radius via
  a `TextBox`/`ComboBox` style applying `materialDesign:TextFieldAssist.UnderlineBrush` for the
  focus underline and a `Border` `CornerRadius=16` in the box-style override.
- **Menu / ContextMenu** — radius is hardcoded `6` in `MaterialDesignMy.xaml` (SubMenuBorder line
  200, templateRoot/BackgroundRoot 287–310, FlyleafContextMenu border 471). These are **edited in
  place** in `MaterialDesignMy.xaml`, not from `M3.xaml` (they're inside ControlTemplates). Menu
  container → 16; individual `MenuItem` background roots → pill (`999`) per the M3 "menu-item pill".
- **Dialog window** — radius 28 (see §4).
- **WordPopup** — radius 24 (edit `WordPopup.xaml:19` `CornerRadius="8"` → `24`).
- **Big-play FAB / OSD / timestamp / snackbar** — per-control radii (28 / round / pill / 8),
  applied in the relevant screen plans, referencing the shared `M3.Radius.*` keys.

---

## 3. COMPONENT STYLE OVERRIDES — new `Resources/M3.xaml`

Create `LLPlayer/Resources/M3.xaml` (`x:Class="LLPlayer.Resources.M3"` to match the
codebase pattern; add to `.csproj` as `Page` — it is auto-included by the WPF SDK glob, verify).
Contents:

1. **Brush overrides** (§1) — at top, so they resolve before any style references them.
2. **Radius resources** (§2).
3. **Button styles** (`BasedOn` MDIX, additive — do not retemplate):
   - `M3.FilledButton` : `BasedOn MaterialDesignRaisedButton`, `Background={DynamicResource MaterialDesign.Brush.Primary}`,
     `Foreground={DynamicResource MaterialDesign.Brush.Primary.Foreground}`,
     `materialDesign:ButtonAssist.CornerRadius=20`, `FontWeight=Medium` (= weight 500),
     hover trigger → `MaterialDesign.Brush.Primary.Light`.
   - `M3.TonalButton` (secondary) : same but `#7fd8e6`/`#00363f`.
   - `M3.OutlinedButton` : `BasedOn MaterialDesignOutlinedButton`, `BorderBrush={DynamicResource MaterialDesignDivider}`,
     `Foreground={DynamicResource MaterialDesign.Brush.Primary}`, radius 20.
   - `M3.TextButton` : `BasedOn MaterialDesignFlatButton`, primary foreground, radius 20.
4. **IconButton (round)** : `M3.IconButton` `BasedOn MaterialDesignIconButton` — `Width/Height` 40
   (sm variant 36), `materialDesign:ButtonAssist.CornerRadius=20` (≥ half = round), hover overlay
   `#1FECB3C4` (= `rgba(236,179,196,0.12)`), `Foreground={DynamicResource MaterialDesign.Brush.Primary}`.
   The existing `MaterialDesignToolButton` (used by WordPopup close) stays valid.
5. **ToggleButton / Switch (M3)** : `M3.Switch` `BasedOn MaterialDesignSwitchToggleButton` — track
   18×44 `#382a30` + 2px `MaterialDesignDivider`; thumb 16 `#9a8a90`; checked → track `#ECB3C4`,
   thumb `#5a1b2c` 22px. MDIX's switch template uses `ToggleButtonAssist` thumb/track brushes; set
   `materialDesign:ToggleButtonAssist.SwitchTrackOnBackground=#ECB3C4` and `...SwitchTrackOffBackground=#382a30`.
6. **Slider retune** : edit `Slider.xaml` (the existing `FlyleafSlider`). The seek bar already binds
   bar/track/thumb to config, so the M3 "6px track / 18px thumb" default applies to *plain* sliders
   (Settings) not the seek bar. Options: (a) add an `M3.Slider` style with `SliderLayout.BarHeight=6`,
   `TrackHeight=6`, `ThumbHeight=18`; (b) change the inactive-track `RadiusX/Y=2`→`3` and active
   `CornerRadius 3,0,0,3`→ a value scaled to 6px (`3` is already half of 6 — keep) so the track reads
   as `radius999` (fully rounded). Fill + thumb already follow `Foreground` → set seek/sliders'
   `Foreground={DynamicResource MaterialDesign.Brush.Primary}` (already the case for seek bar).
   Grip `Ellipse` currently `MaterialDesign.Brush.Foreground` — change to `TemplateBinding Foreground`
   so the thumb is rose `#ECB3C4` not neutral (M3 brief: "thumb #ECB3C4 18×18").
7. **Card** : global `materialDesign:Card` style — `UniformCornerRadius=16`,
   `Background={DynamicResource MaterialDesignCardBackground}` (= `#2d2025`),
   `materialDesign:ElevationAssist.Elevation=Dp0` (no shadow), 1px border `#0AFFFFFF`
   (`rgba(255,255,255,.04)`). NOTE: `FlyleafBar.xaml:29-32` sets its own `Card.Background` inline
   (video back-color at 0.15 opacity) — an explicit local value, so the global setter won't override
   it. Leave FlyleafBar as-is (transport chrome is a screen concern).
8. **ContextMenu / MenuItem** : edit `MaterialDesignMy.xaml` radii (§2) + retint — the icon
   `PackIcon` style at line 51 already uses `MaterialDesign.Brush.Primary` (→ becomes `#ECB3C4`
   automatically via §1, satisfies "icon tint #ECB3C4"). Add highlight bg
   `#24ECB3C4` (`rgba(236,179,196,.14)`) by changing `BackgroundRoot` fill from
   `MaterialDesign.Brush.Primary` opacity-0.13 to the rose container at the new opacity (already
   tracks Primary → fine). Menu item shape → pill (`999`) on `templateRoot`/`BackgroundRoot`.
9. **ToolTip** : `M3.ToolTip` `BasedOn MaterialDesignToolTip` — bg `#382a30`, radius 8, body text.
10. **TextBox / ComboBox (filled, radius 16)** : `M3.TextBox` / `M3.ComboBox` `BasedOn` the MDIX
    filled styles; box bg `#382a30`, bottom outline 1px `MaterialDesignDivider`, focus underline +
    2px `#ECB3C4`, focused box bg `#2d2025`, radius 16 on the box `Border`.
11. **TabControl** : `M3.TabControl`/`M3.TabItem` `BasedOn MaterialDesignTabControl` — active tab
    underline + foreground `#ECB3C4` (MDIX uses `TabAssist`/the selection-indicator brush; set the
    selected-item underline brush to `MaterialDesign.Brush.Primary`).
12. **DataGrid selected row** : selected-row bg `#2EECB3C4` (`rgba(236,179,196,.18)`) via a
    `DataGridRow`/`DataGridCell` style `BasedOn MaterialDesignDataGrid*` — used by Batch + Keys grids.

All styles are **keyed** (`M3.*`) unless they are safe to make implicit (`TargetType` only):
implicit-safe = `Card`, `ToolTip`, and the `MenuItem`/`ContextMenu` edits (already global). Buttons,
sliders, fields, tabs, switches → **keyed** so screens opt in deliberately and we don't silently
restyle every existing button before its screen plan is reviewed.

---

## 4. DIALOG WINDOW CHROME

Today: `MyDialogWindow.xaml` + `MainWindow.xaml` use `Style="{StaticResource MaterialDesignWindow}"`
(MDIX-provided). That style draws the window border + a custom title bar via `WindowChrome`. The
dialog's content background is `MaterialDesignPaper` (→ now `#1a1216`). For dialogs the M3 brief
wants **surface `#241a1e`, radius 28, no border**.

Plan:
- Define `M3.DialogWindow` `BasedOn MaterialDesignWindow` in `M3.xaml`:
  - `Background={DynamicResource MaterialDesign.Brush.Background}` (= `#241a1e`, the dialog surface),
    overriding the `MaterialDesignPaper` set inline in `MyDialogWindow.xaml` (change that line to
    use the new style or drop the inline `Background`).
  - `BorderThickness=0`, `BorderBrush=Transparent` (no border).
  - Outer radius 28: MDIX `MaterialDesignWindow` exposes its chrome corner radius via the window
    template's root `Border`. The clean override is `WindowChrome.CornerRadius` is not a thing on
    Win32 < Win11; instead set the template root `Border CornerRadius=28` by retemplating in
    `M3.DialogWindow`, OR (lower risk) keep MDIX chrome square and apply radius 28 to the **content
    root** `Border` inside each dialog's top-level grid (a screen concern). RECOMMENDATION: keep
    the window square (avoids fighting `WindowChrome`/`AllowsTransparency` + DWM drop-shadow), and
    achieve the rounded look on the inner content panels (`#2d2025` radius-16 groups, palette radius-28)
    — matching the CSS where `.dlg` is the rounded surface and the OS window is the frame.
- `MainWindow` keeps `MaterialDesignWindow` unchanged (player window chrome is a screen concern;
  titlebar `#2a1d22`, win buttons, close-hover `#e81123` handled in the Player chrome screen plan).

> Do NOT replace `MaterialDesignWindow` globally or retemplate it in `M3.xaml`: `MyDialogWindow` is
> registered via `RegisterDialogWindow<MyDialogWindow>()` and download/error dialogs rely on VM-driven
> sizing + fixed/non-resizable styles (`wpf-design-contract.md` lines 40–42). A keyed
> `M3.DialogWindow` opted into by `MyDialogWindow.xaml` is the safe unit of change.

---

## 5. APP.XAML MERGE PLAN

Insert `M3.xaml` so its brush + style overrides win over `MaterialDesign2.Defaults` but its
`StaticResource` references (e.g. `BasedOn MaterialDesignRaisedButton`) still resolve. Place it
**after `MaterialDesign2.Defaults.xaml` and after `MaterialDesignMy.xaml`** (so its menu retints
layer on the re-templated menu), and **before `Converters.xaml` / `PopupMenu.xaml`** is NOT
required — but since `M3.xaml` does not depend on converters or the popup menu, and the popup menu
depends on `MaterialDesignMy`, the safest insert is immediately after `MaterialDesignMy.xaml`:

```xml
<materialDesign:CustomColorTheme BaseTheme="Dark" PrimaryColor="#D23D6F" SecondaryColor="#00B8D4" />
<ResourceDictionary Source="pack://.../MaterialDesign2.Defaults.xaml" />
<ResourceDictionary Source="/Resources/MaterialDesignMy.xaml"/>
<ResourceDictionary Source="/Resources/M3.xaml"/>          <!-- NEW: brush + radius + style overrides -->
<ResourceDictionary Source="/Resources/Converters.xaml"/>
<ResourceDictionary Source="/Resources/PopupMenu.xaml"/>
<ResourceDictionary Source="/Resources/Validators.xaml"/>
```

Rationale: brush overrides resolve at `DynamicResource` lookup time regardless of position relative
to consumers, so being after Defaults is sufficient for recoloring. Being after `MaterialDesignMy`
lets any `BasedOn` references to MDIX styles still resolve, and keeps the documented constraint
(`PopupMenu` after `MaterialDesignMy` + `Converters`) intact — `M3.xaml` slots between them without
disturbing it. Update `wpf-design-contract.md` line 11 to record the new order:
`CustomColorTheme, MaterialDesign2.Defaults, MaterialDesignMy, M3, Converters, PopUpMenu, Validators`.

If `M3.xaml` needs converters (e.g. for an opacity/color converter in a style), move it to *after*
`Converters.xaml` instead — but the brush/radius/style set above needs none.

---

## 6. RISK LIST

- **R1 — Merge-order regression (HIGH).** Reordering or misplacing `M3.xaml` can break
  `PopupMenu.xaml`'s `StaticResource` lookups. Mitigation: insert exactly as §5; do not move
  `Converters`/`PopupMenu`/`Validators`. This ordering is frozen in `wpf-design-contract.md` line 11
  and must be updated in the same change.
- **R2 — `PaletteHelper` re-stomp on live theme switch (HIGH for non-default modes).**
  Settings ▸ Themes calls `PaletteHelper.SetTheme`, which regenerates neutral + primary/secondary
  brushes and overwrites the §1 surface/container overrides at runtime. The shipped dark default is
  fine; Light / Follow-Windows / accent-sync will lose the rose ramp. Mitigation: re-apply the §1
  overrides in the theme-apply code path after `SetTheme`, or scope the M3 ramp to dark-only (flag
  for the theme/runtime screen plan — not solvable in foundation XAML alone).
- **R3 — Seed-vs-override divergence (MED).** Keeping seed `#D23D6F` but overriding
  `MaterialDesign.Brush.Primary`=`#ECB3C4` means any *un-overridden* derived primary key (e.g. an
  obscure `MaterialDesign.Brush.Primary.Light.Foreground`) still reflects the saturated seed.
  Mitigation: after build, grep rendered screens for residual `#D23D6F`; override any stragglers.
- **R4 — `x:Shared` / template-internal radius doubles (MED).** MDIX button corner radius lives in
  template-internal `Double` resources re-merged by `MaterialDesign2.Defaults`; redefining them
  globally is fragile and may throw or no-op. Mitigation: use `materialDesign:ButtonAssist.CornerRadius`
  per-style (supported public API in 5.x), never redefine the internal double.
- **R5 — Wrong StaticResource key throws at dialog-open, not build (MED).** (Documented lesson in
  project memory.) A typo'd `BasedOn`/`StaticResource` key in `M3.xaml` compiles but throws when the
  consuming control is realized. Mitigation: verify each `BasedOn` MDIX key name against 5.3.1
  (`MaterialDesignRaisedButton`, `MaterialDesignFlatButton`, `MaterialDesignOutlinedButton`,
  `MaterialDesignIconButton`, `MaterialDesignSwitchToggleButton`, `MaterialDesignTabControl`,
  `MaterialDesignDataGrid`, `MaterialDesignToolTip`) and launch-test every dialog after the change.
- **R6 — Frozen brushes (LOW).** WPF freezes `SolidColorBrush` on render; re-declaring keys is fine
  (each is a new instance), but never attempt to mutate an existing brush's `Color` at runtime.
  Mitigation: only ever replace whole brush resources, as planned.
- **R7 — Brushes referenced by name elsewhere (LOW, audited).** All 13 surface/primary/secondary keys
  in §1 are referenced by `DynamicResource` across ≥9 files (listed in §0). Overriding the *value* is
  safe; **renaming or deleting any key would break the build / de-theme a screen.** Mitigation: only
  add/override values, never rename; keep both the namespaced key and any referenced legacy alias.
- **R8 — Inline local values defeat global setters (LOW, expected).** `FlyleafBar` Card background,
  WordPopup over-video `#333333`, and the GridSplitter `#302D2B` are intentional local literals; the
  global Card/brush overrides won't reach them. Mitigation: handle those in their screen plans;
  foundation does not touch them.
- **R9 — `.csproj` inclusion (LOW).** `M3.xaml` must build as a `Page`. The WPF SDK auto-globs
  `**/*.xaml`; confirm it isn't excluded and that `x:Class` partial matches the file. Verify with a
  clean `dotnet build` in the worktree (restore-in-worktree gotcha applies).
