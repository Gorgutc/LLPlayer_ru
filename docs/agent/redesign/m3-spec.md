# M3 Skin Specification — LLPlayer WPF → Material 3 (Material You)

**Status:** authoritative skin spec for the WPF re-skin.
**Source:** Claude Design project `e2036840-4ab4-41e6-b6c3-f883ba53e0ef`,
framework `frameworks/flutter-m3/theme.css` (the M3 skin) layered over the
canonical `styles.css` / `components.css` / `tokens/*`.
**Target:** the existing WPF app (MaterialDesignInXamlToolkit, MaterialDesign2,
`App.xaml` `CustomColorTheme` Dark). Roboto and MaterialDesign `PackIcon` (mdi)
are kept 1:1; this spec changes only colors, surfaces, radii, sizes, and a few
behavioral affordances called out under "CHANGES FROM OLD UI".

> How to read this: the M3 skin is a **delta layer**. Where M3 does not override a
> value, the canonical base value (from `components.css` / `tokens/*`) still
> applies. Every base value that M3 *changes* is flagged. Anything tagged
> **[CHANGE]** is an intentional, owner-visible departure from the shipped dark
> MaterialDesign2 look — do not "restore" it.

---

## 0. Brand & ramp summary (the big picture)

| Aspect | OLD (shipped WPF, App.xaml CustomColorTheme Dark) | M3 (this spec) |
| --- | --- | --- |
| Primary brand | `#D23D6F` saturated rose, on-primary `#FFFFFF` | **Primary container tone** `#ECB3C4`, on-primary `#5A1B2C` |
| Secondary | `#00B8D4` cyan, on-secondary `#00141A` | `#7FD8E6` cyan tonal, on-secondary `#00363F` |
| Neutral ramp | near-black `#121212 → #1A1A1A → #212121 → #2B2B2B → #333 → #444` | **rose-tinted dark** `#1A1216 → #241A1E → #2D2025 → #382A30` |
| Radii | small (3 / 6 / 8 / 10 px) | large/tonal (pill 999, 16, 20, 24, 28 px) |
| Filled surfaces | saturated `#D23D6F` fill, white text | light rose **container** `#ECB3C4` fill, dark-rose text `#5A1B2C` |
| Seed for tonal palettes | — | `#D23D6F` (HCT seed; all M3 tones derive from this) |
| Font | Roboto (MaterialDesignFont) | Roboto — **unchanged** |
| Icons | MaterialDesign PackIcon (mdi) | MaterialDesign PackIcon (mdi) — **unchanged 1:1** |

**[CHANGE — brand]** M3 stops using the saturated `#D23D6F` as the *fill* color.
`#D23D6F` becomes only the **seed** for the tonal palette. Filled surfaces (FAB,
play button, chips, switch-on, sidebar active) use the lighter **primary
container** `#ECB3C4` with dark-rose text `#5A1B2C`. The accent still reads
"rose," but every large filled surface is now a pale rose instead of a vivid
magenta. This is intentional Material-You tonal behavior.

---

## 1. Color tokens

All M3 color tokens live under the `--m3-*` namespace and are intended to map to
WPF resource keys (App.xaml `CustomColorTheme` + a new `M3Palette` resource
dictionary merged after `MaterialDesignMy`).

### 1.1 Core palette

| Token | Hex / rgba | Role | WPF usage |
| --- | --- | --- | --- |
| `--m3-bg` | `#1A1216` | window / letterbox / deepest surface | Window background, video stage base, mini-player window bg |
| `--m3-surf` | `#241A1E` | base surface | Transport bar bg, sidebar bg, dialog `.dlg` bg |
| `--m3-surf2` | `#2D2025` | raised surface | Cards, groups/results/log/msg panels, palette `.pal`, focused field bg |
| `--m3-surf3` | `#382A30` | highest tonal surface | Field/select rest bg, menu bg, word-popup bg, switch track (off), kbd chip |
| `--m3-primary` | `#ECB3C4` | primary container tone | FAB, play button, filled chips, slider/seekbar fill+thumb, switch-on track, active icon color, tab/cue underline |
| `--m3-on-primary` | `#5A1B2C` | text/icon on primary | Text on FAB/play/chip/snackbar-CTA-ish, switch-on thumb |
| `--m3-sec` | `#7FD8E6` | secondary (cyan) tonal | Word-popup word color, secondary chip bg, secondary subtitle word hover |
| `--m3-on-sec` | `#00363F` | text on secondary | Text on secondary chip / secondary button |
| `--m3-sec-container` | `#2A4248` | secondary container | Secondary tonal fills (reserved; e.g. secondary chip variants) |
| `--m3-outline` | `rgba(236,179,196,0.25)` | outline / hairline | Outlined-button border, field bottom border, switch track border (off), listbox border |

### 1.2 Derived / inline colors used by the skin (not separate tokens, but fixed values to reproduce)

| Name | Hex / rgba | Where |
| --- | --- | --- |
| Primary hover | `#F3C6D3` | contained-primary button hover, play button hover, big-play implicit |
| Text on dark (body) | `#F3DDE4` | window text, timestamp text, OSD text, big OSD |
| Title text (dim) | `#D8C2C9` | titlebar title, window-button glyphs, transport title |
| Titlebar bg | `#2A1D22` | titlebar, dialog `.nav`, dialog/mini-player toolbar `.tb` |
| Window-btn hover | `rgba(255,255,255,0.08)` | window min/max hover |
| Close hover | `#E81123` (text `#FFF`) | titlebar close hover |
| Stage gradient | `radial-gradient(120% 90% at 40% 38%, #3A2A30 0%, #241A1E 55%, #1A1216 100%)` | video stage |
| Vignette | `radial-gradient(75% 75% at 50% 45%, transparent 55%, rgba(0,0,0,0.5) 100%)` | stage vignette overlay |
| Timestamp pill bg | `rgba(42,29,34,0.7)` | top-left timestamp |
| Snackbar bg | `#ECE0E3` | snackbar surface (light, inverse) |
| Snackbar text | `#322329` | snackbar label |
| Snackbar action | `#7A2F44` | snackbar button (bold) |
| Switch thumb (off) | `#9A8A90` | switch thumb in off state |
| Hover tint (primary @12%) | `rgba(236,179,196,0.12)` | icon-button hover |
| Hover tint (primary @14%) | `rgba(236,179,196,0.14)` | menu-item hover |
| Selection tint @16% | `rgba(236,179,196,0.16)` | dialog `.it` selected/hover |
| Selection tint @18% | `rgba(236,179,196,0.18)` | table selected row, sidebar sub-item active, cue active |
| Selection tint @20% | `rgba(236,179,196,0.20)` | dialog listbox `.lb` selected, scrollbar thumb |
| Card border | `rgba(255,255,255,0.04)` | card border, sidebar border-left |

> **WPF mapping note:** `--m3-primary` → `PrimaryHueMidBrush` equivalent for
> *containers* (not the seed). Keep the MaterialDesign `CustomColorTheme` seed at
> `#D23D6F` so derived swatches/secondary tones still resolve, then override the
> *container/fill* brushes used by the components below with `#ECB3C4` / `#5A1B2C`.
> The accent-on-Primary readability converter already in the codebase should pick
> `#5A1B2C` as the on-color over `#ECB3C4`.

---

## 2. Typography

Roboto throughout (already `MaterialDesignFont`). Timestamps and seek-bar times
use Roboto Mono (tabular). No type-scale change from base — M3 reuses the
canonical scale; only weights on a few chrome elements are pinned.

| Family token | Stack | Use |
| --- | --- | --- |
| `--font-sans` | `"Roboto", system-ui, sans-serif` | everything except time read-outs |
| `--font-mono` | `"Roboto Mono", "Cascadia Code", ui-monospace, monospace` | timestamp, seek-bar times, menu-item shortcut, kbd chips |

Type scale (px), unchanged from base:

| Token | px | Use |
| --- | --- | --- |
| `--fs-display` | 34 | dialog / hero titles |
| `--fs-h1` | 24 | — |
| `--fs-h2` | 20 | word-lookup translation |
| `--fs-h3` | 16 | word-lookup source, section heads, `lg` button text |
| `--fs-body` | 15 | menu items, list rows, body |
| `--fs-ui` | 14 | button text, seek-bar/controls, field input |
| `--fs-caption` | 12 | chips, helper text, titlebar title |
| `--fs-overline` | 11 | overline |

Weights: light 300 / regular 400 / **medium 500** / bold 700.

M3-specific weight/size pins (override base where noted):

| Element | M3 value |
| --- | --- |
| Button label | weight **500**, letter-spacing `0.01em` (M3 tightens base `0.02em`) **[CHANGE — minor]** |
| Titlebar title | 12px, `#D8C2C9` |
| Window-button glyph | 15px |
| Timestamp | mono **700**, 14px, `#F3DDE4` |
| OSD text | 500, 17px, `#F3DDE4`, text-shadow `0 1px 6px rgba(0,0,0,0.8)` |
| Transport title | 13px, `#D8C2C9`, **`font-style: normal`** (the base italic is removed) **[CHANGE]** |
| Transport / sidebar play (first control) | inherits FAB-tonal, see §5 |

---

## 3. Shape / radii

M3 substantially enlarges radii vs. the base 3/6/8/10 ramp. Base radii still
apply where not overridden, but the visible M3 surfaces use the larger values
below.

| Element | OLD radius | M3 radius | Note |
| --- | --- | --- | --- |
| Button (all variants) | 6 (`--radius-sm`) | **20px** | pill-ish |
| IconButton | 50% round | **999px** (round) | unchanged in effect |
| Chip | 3 (`--radius-xs`) | **999px** pill | **[CHANGE]** chip becomes a full pill |
| TextField / Select box | 10 (`--radius-lg`) | **16px** | + bottom-border focus underline |
| Select menu | 6 | **16px** | |
| Switch track | pill | pill (999) | reshaped, see §4 |
| Slider track / fill | 3 | **999px** | fully rounded |
| Card | 6 | **16px** | |
| Menu | 6 | **16px** | |
| Menu item | 6 | **999px** pill | **[CHANGE]** menu rows become pills |
| WordPopup | 8 (`--radius-md`) | **24px** | |
| SeekBar fill | 3 | **3px** (track unchanged) + primary color | only color changes |
| Big-play FAB | n/a | **28px** (→ 40px on hover) | morphing FAB |
| Transport play button | n/a | **16px** | squircle |
| Timestamp pill | n/a | **999px** | |
| OSD icon | n/a | **999px** | |
| Snackbar | n/a | **8px** | |
| Sidebar sub-item | 0 (square w/ left bar) | **16px** | **[CHANGE]** see §5 |
| Dialog `.dlg` | small | **28px** | large dialog corners |
| Dialog groups/results/file/log/msg | small | **16px** | |
| Command palette `.pal` | small | **28px** | |
| kbd chip | small | **8px** | |
| progress `.bar` | square | **999px** | |
| listbox `.lb` | small | **16px** | |

---

## 4. Per-component spec

For each: M3 colors, radii, sizes. Base geometry from `components.css` applies
where not restated.

### 4.1 Button (`.llp-btn`)
- **Radius:** 20px (all variants). Weight 500, letter-spacing 0.01em.
- **Sizes (base, unchanged):** sm h28 / pad 0 12; md h36 / pad 0 16; lg h44 / pad 0 24.
- **Contained / primary:** bg `#ECB3C4`, text `#5A1B2C`, **no shadow** at rest;
  hover bg `#F3C6D3` + shadow `0 1px 3px rgba(0,0,0,0.4)`.
- **Contained / secondary:** bg `#7FD8E6`, text `#00363F`, no shadow.
- **Outlined / primary:** border `--m3-outline` (`rgba(236,179,196,0.25)`), text `#ECB3C4`, transparent bg.
- **Text / primary:** text `#ECB3C4`, transparent bg, radius 20.
- **[CHANGE]** rest-state shadow removed on contained (M3 flat tonal); appears only on hover.

### 4.2 IconButton (`.llp-iconbtn`)
- **Radius:** 999px (round).
- **Sizes:** sm 36×36, md 40×40 (M3 enlarges base 24/32 → 36/40 for touch). **[CHANGE — sizing]**
- **Hover bg:** `rgba(236,179,196,0.12)`.
- **Active / primary color:** `#ECB3C4`.

### 4.3 Chip (`.llp-chip`)
- **Shape:** pill 999px, **height 26px**, padding 0 12. **[CHANGE]** base was h22 / radius 3 / pad 0 6.
- **Primary:** bg `#ECB3C4`, text `#5A1B2C` (base used white text — now dark-rose). **[CHANGE]**
- **Secondary:** bg `#7FD8E6`, text `#00363F`.
- **Neutral:** bg `#382A30`.

### 4.4 TextField / Select box (`.llp-field__box`, `.llp-select__box`)
- **Box:** radius 16, bg `#382A30`, transparent side/top border, **bottom border 1px `--m3-outline`**.
- **Focus / open:** border-color `#ECB3C4`, **bottom-border-width 2px**, bg `#2D2025`.
- **Select menu:** radius 16, bg `#382A30`, shadow `0 4px 16px rgba(0,0,0,0.4)`.
- **[CHANGE]** focus accent moves from base cyan (`--secondary`) to rose (`#ECB3C4`); the filled box (`#382A30`) replaces the translucent `rgba(255,255,255,0.06)` base.

### 4.5 Switch (`.llp-switch`) — M3 reshaped
- **Track (off):** h18 × w44, bg `#382A30`, **border 2px `--m3-outline`**.
- **Thumb (off):** `#9A8A90`, 16×16, top 0, left 2.
- **On — track:** bg `#ECB3C4`, border `#ECB3C4`.
- **On — thumb:** `#5A1B2C`, **grows to 22×22**, `translateX(24px)`, top -3.
- **[CHANGE]** Full M3 switch: bordered track when off, thumb grows on enable, thumb darkens to on-primary. Base was a thin 40×16 track with a `#bdbdbd`→primary thumb and no border.

### 4.6 Slider (`.llp-slider`)
- **Track:** h6, radius 999. (Base was h4 / radius 3.) **[CHANGE — thicker, fully round]**
- **Fill:** `#ECB3C4`, radius 999.
- **Thumb:** `#ECB3C4`, 18×18 (base 14×14, white). **[CHANGE — larger, tonal]**

### 4.7 Card (`.llp-card`)
- Radius 16, bg `#2D2025`, **no shadow**, border `rgba(255,255,255,0.04)`.
- **[CHANGE]** base card had `--shadow-dp1`; M3 is flat with a hairline border.

### 4.8 Menu (`.llp-menu`) + MenuItem (`.llp-menu-item`)
- **Menu:** radius 16, bg `#382A30`, shadow `0 4px 16px rgba(0,0,0,0.45)`.
- **MenuItem:** **radius 999 (pill)**; row height 32 (base), pad 0 10.
- **Icon (mdi) color:** `#ECB3C4`.
- **Hover / highlighted:** bg `rgba(236,179,196,0.14)`.
- **Shortcut text:** mono, secondary color (unchanged).
- **[CHANGE]** menu rows are pills (was 6px); hover tint is rose.

### 4.9 WordPopup (`.llp-wordpopup`)
- Radius **24**, bg `#382A30`, shadow `0 8px 24px rgba(0,0,0,0.5)`.
- **Word color:** `#7FD8E6` (secondary/cyan). Translation stays white, h2 (20px), centered.
- Max-width 320 / min-width 180 (base).

### 4.10 Tabs (`.llp-tab`)
- Active: border-bottom-color `#ECB3C4`, text `#ECB3C4`, active mdi `#ECB3C4`.
- Tab height 44, pad 0 16 (base). Inactive text = `--text-secondary`.

### 4.11 Table / DataGrid (`.llp-table`)
- Selected row bg `rgba(236,179,196,0.18)`. Hover bg `rgba(255,255,255,0.04)` (base).
- Header/cell paddings unchanged.
- **[CHANGE]** selected-row tint is rose-tonal (was solid `#444`).
- **WPF GOTCHA (carry over from #24):** never set
  `VirtualizingPanel.IsVirtualizingWhenGrouping="True"` on a grouped DataGrid —
  it breaks `DataGridRow` cell rendering. Not an M3 concern but must survive the re-skin.

### 4.12 SubtitleCue (`.llp-cue`) — overlay over video
- Primary text white, bold, 30px; **word hover underline `#ECB3C4`**.
- Secondary text `#F0F0F0`, 24px; **word hover underline `#7FD8E6`**.
- Separator, text-shadow, max-width 80% unchanged.
- **[CHANGE]** primary word-hover ring is the rose *container* tone now, not `#D23D6F`.

### 4.13 SubtitleListItem (`.llp-sub-item`) — sidebar row
- **Radius 16**, margin 3 4, padding 8 12, **NO left border**.
- **Active:** bg `rgba(236,179,196,0.18)`; active play-glyph `#ECB3C4`.
- **[CHANGE — important]** OLD UI used a **3px primary LEFT-BORDER accent**
  (`--accent-bar` + `border-left-color: --cue-active`) on the now-playing row.
  **M3 removes the left bar entirely** and replaces it with a **rounded (16px)
  tonal fill**. This is intentional; the redesign team must not re-add the left bar.

### 4.14 SeekBar (`.llp-seekbar`)
- Fill `#ECB3C4`, thumb `#ECB3C4` (base thumb was white `--seek-thumb`). Track/buffered geometry unchanged.
- **[CHANGE]** thumb color rose-tonal instead of white.

### 4.15 Spinner (`.llp-spinner`)
- No explicit M3 override → inherits base: 20×20, 2px ring, top-color `--secondary` (cyan); `--primary` variant top-color follows the seed.
- **Implementation note:** since base `--primary` is overridden contextually,
  reproduce the spinner using `#7FD8E6` (default) / `#ECB3C4` (primary variant) so it reads on the rose ramp.

### 4.16 EmptyState (`.llp-empty`)
- No explicit M3 override → inherits base layout: centered column, 48px dimmed mdi glyph, title `--fs-ui`/70% opacity, hint `--fs-caption`/60%, max-width 240.
- Colors follow the M3 text-on-dark (`#F3DDE4` body) on the rose surfaces.

---

## 5. Player chrome (`.llpk-*`)

### 5.1 Window
- bg `#1A1216`, text `#F3DDE4`, font Roboto. Full height flex column.

### 5.2 Titlebar (`.llpk-titlebar`)
- Height **40px** (base titlebar token was 32 — M3 uses 40). **[CHANGE — sizing]**
- bg `#2A1D22`. Left group pad-left 14, gap 10; logo 18×18.
- Title 12px `#D8C2C9`, ellipsis.
- Window buttons (`.llpk-wbtn`): **w44** × full height, glyph 15px `#D8C2C9`;
  hover bg `rgba(255,255,255,0.08)`; **close hover bg `#E81123`, glyph `#FFF`**.

### 5.3 Stage / vignette (`.llpk-stage`)
- bg `radial-gradient(120% 90% at 40% 38%, #3A2A30 0%, #241A1E 55%, #1A1216 100%)`.
- Vignette overlay (pointer-events none): `radial-gradient(75% 75% at 50% 45%, transparent 55%, rgba(0,0,0,0.5) 100%)`.
- Status chips top 14 / right 16 (gap 8). Cue zone bottom 8%, centered, pad 0 24.
- Cue-in animation 280ms decelerate (respects reduced-motion).
- **WPF note:** the FlyleafHost DirectX child-HWND covers the video; this rose
  radial only shows as the *idle/letterbox* stage, never over live video (same
  airspace limit as Mica). Keep the gradient on the host background, not over the surface.

### 5.4 Big-play FAB (`.llpk-stage__bigplay`)
- **80×80**, radius **28** → **40 on hover** (morphing), centered.
- bg `#ECB3C4`, icon color `#5A1B2C`, icon **42px** (margin-left 3 for optical centering).
- Shadow `0 6px 20px rgba(0,0,0,0.4)`; hover `scale(1.05)` + radius 40.
- **[CHANGE]** large tonal FAB is new chrome; the seed magenta is not used here.

### 5.5 Timestamp (`.llpk-ts`)
- top 12 / left 16; mono **bold 14px** `#F3DDE4`; pad 4 12; bg `rgba(42,29,34,0.7)`; radius **999**.

### 5.6 OSD (`.llpk-osd`)
- top 50 / right 16; gap 8; fade-in 150ms.
- Icon: 40×40 round, bg `#ECB3C4`, glyph 22px `#5A1B2C`.
- Text: 17px / weight 500 / `#F3DDE4`, shadow `0 1px 6px rgba(0,0,0,0.8)`.

### 5.7 Snackbar (`.llpk-snack`)
- top 16 / centered (translateX -50%); z 30; **light inverse surface** bg `#ECE0E3`, text `#322329`.
- Radius **8**, pad 12 18, font 14, gap 18, shadow `0 6px 20px rgba(0,0,0,0.4)`, fade-in 200ms.
- Action button: transparent, color `#7A2F44`, **bold**, 13px.
- **[CHANGE]** snackbar is a light/inverse surface (M3 inverse-surface idiom) — the only light surface in the dark app. Must still sit top-center and not overlap the FlyleafBar / subtitle zone (per design contract §Main Window Layout).

### 5.8 Transport bar (`.llpk-transport`)
- bg `#241A1E`, pad `12 16 14`. Seek row pad 0 4. Controls gap **6**, margin-top 4.
- **FIRST control (play) is a tonal squircle:** **48×48**, radius **16**, bg `#ECB3C4`, icon `#5A1B2C`, icon **26px**; hover bg `#F3C6D3`. **[CHANGE]** the primary play control is a filled tonal button, not a bare icon button.
- Title: flex-1, 13px `#D8C2C9`, **non-italic** **[CHANGE]**, ellipsis, pad 0 12.
- Volume slider width **96**, pad 0 6. (On a narrow bar the volume slider collapses — carry over the existing FlyleafBar narrow-bar behavior.)

### 5.9 Sidebar (`.llpk-sidebar`)
- width = `--sidebar-width` (300 default, configurable), bg `#241A1E`, **border-left `rgba(255,255,255,0.04)`**.
- Toolbar: flex-wrap, gap 4, pad 10. Search row: pad 0 10 14, gap 6, field flex-1.
- List: pad 0 6; scrollbar width 10, thumb `rgba(236,179,196,0.2)` radius 6.
- Sub-item: see §4.13 (rounded tonal fill, no left bar).

---

## 6. Dialog chrome (`.dlg / .nav / groups / .pal / kbd / progress / .msg / listbox`)

All dialog overrides are `!important` in the skin (they override base dialog
styling). Reproduce in WPF as dedicated dialog window/control templates.

| Selector | M3 spec |
| --- | --- |
| `.dlg` (dialog shell) | bg `#241A1E`, radius **28**, **no border**, shadow `0 20px 60px rgba(0,0,0,0.5)` |
| `.nav` (settings left rail) | bg `#2A1D22`, **no right border** **[CHANGE]** (old rail had a right divider) |
| `.group`, `.gbox`, `.results`, `.file`, `.log` | bg `#2D2025`, radius **16** (`.gbox` clips overflow) |
| `.pal` (command palette) | bg `#2D2025`, radius **28**, no border |
| `.it.is-sel`, `.it:hover` (palette/result rows) | bg `rgba(236,179,196,0.16)` |
| `.kbd`, `.it__kbd` (key caps) | bg `#382A30`, **no border**, radius **8** |
| `.bar`, `.bar i` (progress) | radius **999** (fully rounded pill) |
| `.msg`, `.err__msg` (message/error blocks) | bg `#2D2025`, radius **16** |
| `.listbox`, `.lb` | border `--m3-outline`, radius **16** |
| `.lb div.is-sel` | bg `rgba(236,179,196,0.2)` |

### 6.1 Embedded mini-player (inside downloader/preview dialogs)
- `.win` (mini window) bg `#1A1216`; `.tb` (mini toolbar) bg `#2A1D22`.
- `.win .stage` rose radial `radial-gradient(120% 90% at 40% 38%, #3A2A30, #1A1216)`.
- `.win .transport` bg `#241A1E`, **no top border** **[CHANGE]**.

### 6.2 Dialogs covered (per WPF design contract — all keep their geometry/behavior, only re-skinned)
Settings (left TreeView + right content), Select language, Subtitles downloader,
Subtitles exporter, Batch subtitles, CheatSheet, Command palette (Ctrl+K),
Whisper model download, Whisper engine download, Tesseract download, Error dialog.
The contract behaviors (singleton activation, VM-driven sizing, non-resizable
download/error windows, Settings search/deep-link flow, Keys DataGrid workflow)
are **unchanged** — M3 is skin-only here.

---

## 7. Summary of intentional CHANGES FROM OLD UI (single-glance list)

1. **Brand fill** — saturated `#D23D6F` → light rose container `#ECB3C4` on `#5A1B2C`; `#D23D6F` is now only the tonal seed.
2. **Neutral ramp** — near-black `#121–#444` → rose-tinted `#1A1216–#382A30`.
3. **Sidebar now-playing** — 3px primary **left-border** → **rounded 16px tonal fill, no left bar**.
4. **Chip** — 3px / h22 / white-on-primary → **pill 999 / h26 / dark-rose-on-`#ECB3C4`**.
5. **Switch** — thin track + grey thumb → **M3 bordered track, thumb grows 16→22 and darkens to on-primary on enable**.
6. **Slider** — h4 / 3px / white 14px thumb → **h6 / pill / rose 18px thumb**.
7. **Menu item** — 6px rows → **pill rows** with rose hover tint and rose mdi icons.
8. **Card** — elevated (dp1 shadow) → **flat with hairline border**.
9. **Contained button** — rest shadow → **flat, shadow only on hover**; radius 6 → 20.
10. **Field/Select focus** — cyan focus + translucent box → **rose focus underline + filled `#382A30` box**, radius 16.
11. **Transport play** — bare icon → **48px tonal squircle**; transport title **italic → normal**.
12. **IconButton sizing** — 24/32 → **36/40** touch targets.
13. **Titlebar** — 32px → **40px**.
14. **Big-play** — new **80px morphing FAB** (28→40 radius).
15. **Snackbar** — **light inverse surface** `#ECE0E3` (only light surface in app).
16. **Dialogs** — `.dlg` radius **28**, no border, deep shadow; settings rail loses its right divider; mini-player transport loses top border.
17. **Word-popup / seekbar / cue accents** — rose-container tonal accents replace saturated magenta; word-popup radius 8 → **24**.

---

## 8. Things that DO NOT change (guardrails)

- Roboto font and Roboto Mono for time read-outs.
- MaterialDesign PackIcon (mdi) glyph set, 1:1.
- App.xaml merged-dictionary **order** (CustomColorTheme, MaterialDesign2.Defaults,
  MaterialDesignMy, Converters, PopUpMenu, Validators) — add `M3Palette` after
  `MaterialDesignMy`, do not reorder.
- Shared converters / popup menus / validators / MaterialDesign defaults — keep.
- All dialog behaviors (singleton activation, VM-driven sizing, non-resizable
  download/error windows, Settings search + deep-link, Keys DataGrid workflow,
  word/drag/mouse subtitle interaction, snackbar placement).
- Stage gradient is idle-only chrome; live video still owns the FlyleafHost child HWND (same airspace limit as Mica — gradient never overlays live video).
- DataGrid grouping gotcha (no `IsVirtualizingWhenGrouping=True`) survives the re-skin.
