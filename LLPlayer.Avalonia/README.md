# LLPlayer for Windows and Linux (LLPlayer.Avalonia)

The shared Windows/Linux desktop app of LLPlayer (backlog F-13): one Avalonia 12 UI over FlyleafLib's portable
`net10.0` build, with `win-x64` and `linux-x64` packages. Views, view models, themes, dialogs and Core are shared,
so subsequent UI improvements are implemented once and checked on both platforms. The WPF app in `../LLPlayer`
remains separately supported until replacement acceptance and the owner's retirement decision.

The native Windows software-playback build/package baseline is implemented. Its fresh verification and desktop
smoke are tracked in [current F-13 status](../docs/agent/f13-status-2026-10-18.md). Windows ASR/OCR, GPU support
and full WPF feature parity require separately evidenced work; Linux model receipts do not establish them.

- UI: Avalonia 12.1.3 with the built-in FluentTheme plus the "LLPlayer shadcn" theme (shadcn/ui neutral tokens,
  `Themes/`), CommunityToolkit.Mvvm view models, plain constructor wiring (`AppHost`).
- Video: FlyleafLib's software renderer (deinterlace, HDR tone mapping, `sws_scale` → BGRA, rotation/flips, colour
  filters) presented by `Controls/VideoView`, which only scales the ready-to-show frame into the renderer's viewport.
- Audio: `Services/AudioBackendSelector` → FlyleafLib's `AudioBackendFactory.CreateDefault()`, assigned to
  `AudioEngine.Backend` before `Engine.Start`: OpenAL Soft (system `libopenal.so.1`/`libopenal1` on Linux;
  pinned app-local `soft_oal.dll` 1.23.1 on Windows) when its
  default device opens, otherwise the silent real-time `NullAudioBackend` with a warning in `flyleaf.log`.
  `LLPLAYER_AUDIO_BACKEND=null|openal|auto` forces the choice.
- Linux OCR prerequisite: the portable FlyleafLib adapter uses system Tesseract 5 (`libtesseract.so.5`) and installed
  system tessdata; neither is bundled. Set standard `TESSDATA_PREFIX` only when tessdata is outside system discovery.
  Recognition ▸ OCR runs on a bitmap subtitle track already selected in the requested primary/secondary slot; the
  configured language fallback is used only when track metadata is unknown.
- Windowing: native Win32 on Windows and X11 (or XWayland) on Linux via `UsePlatformDetect()`. Native Wayland (`Avalonia.Wayland`) is a possible future
  opt-in and is intentionally not referenced yet.

## Build and run

### Windows x64

Install the .NET 10 SDK for development. The extracted package needs the .NET 10 runtime and **Microsoft Visual
C++ x64 Redistributable 2022 or newer** for its native dependencies. WSL is not required for this native app.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\windows\verify-avalonia.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\windows\test-package-avalonia.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\windows\publish-avalonia.ps1
```

The publisher produces `artifacts/windows-avalonia/LLPlayer-Avalonia-<version>-win-x64.zip` and its `.sha256`.
Extract it and launch **`LLPlayer.Avalonia.exe`** inside the extracted directory; keep `FFmpeg/`, `soft_oal.dll`
and the other native dependencies in place. This is the shared Avalonia UI; **`LLPlayer.exe`** is the separately
supported WPF app. Both can be built without replacing the other.

For a source-tree development launch:

```powershell
dotnet build LLPlayer.Avalonia -warnaserror
dotnet LLPlayer.Avalonia/bin/Debug/net10.0/LLPlayer.Avalonia.dll --ffmpeg-dir ./FFmpeg video.mp4
```

The Windows verification script uses the tracked FFmpeg DLLs. It downloads a pinned, SHA-256-verified FFmpeg CLI
for generating the test fixture when `LLPLAYER_FFMPEG_CLI` is unset; this test tool is not shipped. The
`LLPlayer Windows Avalonia Build & Test` CI job runs the Windows checks/package flow, alongside the existing WPF
job. Shared changes also run the Linux gate below.

### Linux x64

```sh
dotnet build LLPlayer.Avalonia
# FFmpeg 8.x shared libraries (libavcodec.so.62, libavformat.so.62, libavutil.so.60, libavfilter.so.11,
# libswscale.so.9, libswresample.so.6):
LLPLAYER_FFMPEG_DIR=/path/to/ffmpeg/lib dotnet LLPlayer.Avalonia/bin/Debug/net10.0/LLPlayer.Avalonia.dll video.mp4
```

FFmpeg folder lookup: `--ffmpeg-dir <dir>`, else `$LLPLAYER_FFMPEG_DIR`, else `<app folder>/FFmpeg`. When the libraries
are missing a window explains what is missing and how to fix it.

Command line: `LLPlayer.Avalonia [--sub <file>] [--theme dark|light] [--ffmpeg-dir <dir>] [media path or URL]`
(`--help` lists everything, including the developer options below).

## Files

| What | Where |
| --- | --- |
| App preferences (volume, subtitle size, sidebar, theme, recent files, last folder) | `$LLPLAYER_CONFIG_DIR`, else `$XDG_CONFIG_HOME/LLPlayer`, else `~/.config/LLPlayer` → `LLPlayer.Avalonia.json` |
| Recognition settings (ASR language/model and OCR language fallback) | Same config directory → `LLPlayer.Recognition.json` |
| Translation settings (word/sentence providers, target language, provider options and credentials) | Same config directory → `LLPlayer.Translation.json` |
| Word library (saved words) | Same config directory → `LLPlayer.WordList.json` |
| Crash log | `$XDG_STATE_HOME/LLPlayer/crash.log` (default `~/.local/state/LLPlayer/crash.log`) |
| Engine log (warnings; `LLPLAYER_LOG_LEVEL=Debug` for more) | `$XDG_STATE_HOME/LLPlayer/flyleaf.log` |

Whisper.cpp models use `<app folder>/whispermodels` by default. Set `LLPLAYER_WHISPER_MODELS_DIR` before launch to use a different absolute directory; the value is captured when the ASR configuration is first initialized. Relative values are rejected. Linux GGML files use canonical whisper.cpp names such as `ggml-large-v3.bin`; model weights are user-downloaded and are not included in the package. The bundled Linux Whisper CPU libraries require glibc 2.34+, `GLIBCXX_3.4.29`, `libgomp.so.1`, and an AVX/AVX2/FMA/F16C-capable CPU. The Windows package includes the selected win-x64 CPU natives, but real Windows recognition acceptance remains separate.

These are the Linux locations. The app code is platform-neutral (`Services/AppPaths.cs`): on Windows the preferences
go to `%APPDATA%\LLPlayer` (or `$LLPLAYER_CONFIG_DIR`) and the logs to `%LOCALAPPDATA%\LLPlayer`.

On first run, translation provider and target values seed from the legacy `LLPlayer.Avalonia.json` preferences while
`LLPlayer.Translation.json` is absent; this seed stays in memory until **Save & Close**. Once present, the separate
translation file is authoritative. It preserves concrete Flyleaf provider settings and advanced chat/sampling
options; API keys are masked in the dialog. Loading and saving never probe endpoints or models. A corrupt settings
file is preserved and reported through a generic warning.

Subtitle translation uses the same settings file and provider records. Word and Sentence provider selections are
independent, while **Edit provider settings** selects which shared provider record to edit. Sentence defaults to
`Off` (also for older settings files without the field). The primary and secondary subtitle-translation switches are
session-only and start Off each time. Loading/saving does not send provider health checks; normal translation can
resume for a slot explicitly enabled in the current session. The menu actions use Flyleaf's existing subtitle
translator and are serialized with other player mutations. Changes suspend and drain old work before applying new
settings or media/track state; canceled/stale results do not update translated cues. This bounded Linux runtime/package
slice passed and its installed launcher receipt is recorded in the subtitle-translation plan. Translation chat options
including context counts, prompts, grammar, KeepContext retention, and target-region formatting are now exposed through
the detached settings draft; Linux tests, package review, and installation readback passed. Native desktop/provider
acceptance was not performed, and the work does not claim full F-13 parity. See
[`docs/agent/plans/2026-10-03-f13-subtitle-translation.md`](../docs/agent/plans/2026-10-03-f13-subtitle-translation.md) and
[`docs/agent/plans/2026-10-03-f13-translation-chat.md`](../docs/agent/plans/2026-10-03-f13-translation-chat.md).

## Features (MVP)

- Transport bar: open, play/pause, seek bar (no feedback loop while dragging), time, speed menu, volume + mute,
  subtitles toggle, sidebar toggle, fullscreen (bar and cursor auto-hide after 2.5 s idle in fullscreen).
- A-B repeat (WPF F-12 parity): the transport's repeat button cycles Set A → Set B → Clear and lights while a point is
  set; right-click it for the explicit Set A / Set B / Clear items. The seek bar shows one marker per point plus a
  translucent band between them, and playback loops back to A through the engine's frame-accurate seek path (priority
  over whole-file Loop Playback, inert for reverse playback and HLS-live streams). Points are transient, never
  persisted, and drop when another file opens; a rejected Set B keeps the previous point and reports the message.
  Waveform and rebindable hotkeys remain the separate F-12 follow-up. Shared Windows/Linux implementation; focused
  regressions 19 (18 plus one env-gated real-engine case that the Windows Avalonia gate runs) and the full
  WPF/Windows Avalonia source, package-contract, local publish/content and independent-review checks passed at product
  `075dd4e4`; Linux CI and native desktop acceptance are separate.
- Text subtitle layout (WPF Position / Size, text half): Settings offers box width 1–100 %, an optional max width
  (0–4000 DIP, 0 = no limit), Center/Left/Right line alignment (an additive Avalonia option; WPF keeps subtitle lines
  centered), Ignore line breaks and a 0–200 DIP primary/secondary distance. The overlay box is centered inside the
  existing 48-DIP side margins and never narrower than 1 DIP; the approved vertical DIP offset is unchanged. Alignment
  and line-break flattening (canonical `SubtitleTextUtil.FlattenText`) share one normalized text with measurement,
  outline/background geometry and word hits, and pixel tests cover the rendered alignment. Five additive `AppPrefs`
  values default to the previous look (100 %, no cap, hard breaks, centered, 2 DIP). Waveform, bitmap scale/position and
  the dual separator remain separate work. Review-fix 105/105 and the final full WPF/Windows Avalonia (UI 777 passed/one skipped, 778),
  package-contract, regression and local publish checks passed at product `baa6dc1`; Linux CI and native desktop
  acceptance are separate.
- App shortcut rebinding (hotkeys half, part 1): the ten app-level shortcuts (sidebar, subtitles search, cheat sheet,
  subtitle size/position, copy primary text, open subtitles, theme) are editable in Settings → Tools with a capture
  button and Reset; clicking starts capture, a second click/Esc/lost focus cancels, the next chord is stored canonically
  and modifier-only keys are ignored. Only changed rows persist as overrides, so untouched actions keep every default
  chord (including both cheat-sheet chords), invalid, duplicate or first-default-equal entries fall back to defaults, Save blocks
  duplicate chords, and restart restores the saved chords. The engine key bindings stay at their defaults in this part
  (the engine editor and app-vs-engine collision check are the next block). Suite 17/17, regression 492/492 and the full
  WPF/Windows Avalonia, package-contract and local publish checks passed at product `24ec18f`; Linux CI and native
  keyboard acceptance are separate.
- Audio waveform overlay (F-12 audio half): the transport bar draws the engine's peak envelope under the seek slider
  with a persisted ShowWaveform toggle (default off, like WPF) next to the A-B button. OFF performs no audio decode;
  disabling the toggle clears the engine peaks; peaks stay engine-owned and transient, and the display-only control
  never hit-tests the slider and is visible only while peaks are active. Geometry mirrors WPF (one column per DIP,
  auto-gain to the loudest bucket, symmetric fill, defensive sanitizing); no engine/WPF/Core change. The Settings hub
  still edits 31 fields and the contract now owns 32 settings values (the file also keeps legacy keys). Waveform suite
  12/12, regression 176/176 and the full WPF/Windows Avalonia, package-contract and local publish checks passed at
  product `c7deb75`; Linux CI, hotkey rebinding and native DPI acceptance are separate.
- Bitmap subtitles, dual separator and block alignment (rest of WPF Position / Size): Settings adds a rendered
  separator max width/opacity, a Top/Center/Bottom block anchor (single and dual; Avalonia adapts it to its approved
  DIP bottom model with a viewport/transport-clearance guard, and dual is selected by loaded slots — including ASR —
  like WPF) and live engine bitmap scale/position (session-only, reset when media opens). Bitmap cues now apply the
  engine's position/scale rules in video coordinates; the dual separator is non-hit-testing, supplies the existing
  primary/secondary gap and adds the WPF 280-DIP/30 % line while single-subtitle layout is unchanged. Four additive
  `AppPrefs` values raise Settings ownership to 31 fields; engine/WPF/Core are unchanged. Bitmap suite 29/29, affected
  136/136, regression 175/175 and the full WPF/Windows Avalonia, package-contract and local publish checks passed at
  product `8d8d704`; Linux CI and native DPI/bitmap acceptance are separate.
- Menus: Media (open media / subtitles, recent, exit), Playback, Subtitles (primary + secondary track for embedded and
  external tracks, size, position, copy), Translation (word/sentence providers and primary/secondary sentence toggles), View (sidebar, search, fullscreen, dark/light theme, Settings…), Help (shortcuts).
- Words ▸ Word Manager opens a singleton list. Filter the five saved-word fields; select a row to edit Reading,
  Translation, Definition, and Example (each edit saves immediately). Term is read-only; delete and clear are explicit.
  TSV and APKG use save pickers; Send to Anki is an explicit AnkiConnect action. Dictionary definition mode is
  configurable in Translation settings and defaults Off.
- Settings hub: View → Settings… and the visible gear button open one modal singleton with left navigation. The first slice covers only existing Appearance and Subtitles preferences; links open the separately owned Translation, Recognition, and shortcut viewer windows after discarding the hub draft, with a warning. Save & Close persists before applying; Close/Esc discard. Implementation, focused tests, Linux `--fast`, and independent source/UI reviews passed; the reviewed Linux package passed archive/content/source-projection checks (SHA-256 `337a37210ec0bf1897df06b550f7f6cf2e8b47695b06e47997fbdb8711cdbe4b`); installation/readback passed: the verified package was current at that Settings Hub checkpoint (`/models/codex-builds/LLPlayer_ru/installed/LLPlayer-0.3.62-linux-x64-settings-337a37210ec0`); it was superseded by the Recognition Advanced package below, and launchers now resolve through stable `current`; prior package and XDG state were preserved. Independent final verification review passed with zero Critical/Important findings; native desktop and Windows acceptance remain open. This does not claim full WPF settings parity. See [`docs/agent/plans/2026-10-07-f13-settings-hub.md`](../docs/agent/plans/2026-10-07-f13-settings-hub.md).
- Recognition menu: open recognition settings; start whisper.cpp CPU ASR on primary or secondary; pause/resume ASR
  globally; cancel and clear either slot; or OCR the selected bitmap subtitle track in either slot. faster-whisper UI
  and broader settings parity remain future work.
- Drag & drop of media and subtitle files; one command-line path / URL opens at start.
- Dual subtitle overlay (primary + secondary, outlined text with shadow, hidden when empty); bitmap subtitles are drawn
  at their position in the picture.
- Subtitles sidebar: cues with start times, current cue highlighted and scrolled into view, click to seek, filter box
  (Ctrl+F, Esc clears).
- Click a subtitle word: playback pauses and the popup shows the word, context, translation and (when enabled and
  available) dictionary definition, with copy and Save word actions. A Not Configured result offers Translation
  settings. Translation and definition lookups run independently; Save waits for both. A translated overlay click
  saves the shown target-language term without re-translating it. Provider/language changes invalidate stale work
  and its cache. The overlay's `Data.IsTranslated` flag is distinct from the sidebar's `SubtitleData.UseTranslated`.
- Keyboard: FlyleafLib's default key bindings (Space, arrows, A/S/D, F, M, Ctrl+O, ...) go through `Player.KeyDown/KeyUp`;
  app shortcuts keep the WPF defaults (Ctrl+B sidebar, Ctrl+F search, F1 cheat sheet, Shift+arrows subtitle size /
  position, Ctrl+C copy) plus `?` (cheat sheet), Ctrl+Shift+O (open subtitles), Ctrl+Shift+T (theme). F1 / `?` open the
  cheat sheet with key caps.
- Screensaver / sleep is inhibited while playing (when the platform supports it).
- Linux ASR engine backend: Whisper.net 1.9.0 CPU (whisper.cpp) and external faster-whisper-XXL process support are wired into FlyleafLib interactive and batch paths. A direct `WhisperCppASRService` CPU inference on the full `large-v3` model and JFK sample passed, as did Whisper CPU native loading and Silero VAD initialization from the published-package staging directory; headless Avalonia UI-command ASR/selected-bitmap OCR also passed with a real Player and local fixtures. Batch workflow acceptance, faster-whisper-XXL execution, native desktop and Windows owner acceptance remain open. Evidence for direct inference: `artifacts/f13-asr-2026-10-02/large-v3-cpu-inference.json`; current UI-command receipt: `artifacts/f13-recognition-2026-10-03/real-ui.log`.
- Recognition settings live in a detached recognition-only JSON file. Save & Close writes it atomically before applying settings; Close discards changes. A corrupt file is preserved and reported. Only existing whisper.cpp CPU models are listed; the dialog does not download, install, or bundle models. Full settings and faster-whisper UI parity remain later work.
- Recognition Advanced adds a collapsed six-field whisper.cpp CPU section (threads, segment character/token limits, split-on-word, no-context, and exact multiline prompt). Blank numeric inputs use the library default; invalid new values block Save visibly. Saved legacy nonpositive numbers display blank and are not normalized in persisted/source/live config until explicit Save; ordinary edits and Reset change only the detached draft, which applies on Save. Reset Advanced affects only these six fields. Save preserves the existing gate: active recognition/subtitle loading blocks Save, and media generation invalidates queued work without restarting active ASR. Focused tests (19), Linux `--fast` with restore (FlyleafLib 1593/6 skipped; Avalonia 235/1 skipped; shell 64), and scoped source/UI reviews passed with zero findings. Thirty-two final headless screenshots are hashed in `screenshots-manifest.json` (SHA-256 `9bfb2fb1ba7609adfbb79e8974f44037f41801df3710dbb01dee2854faf422b4`); root reviewed six. Package coherence passed for the 180,022,223-byte archive (SHA-256 `f080a30143d8b8462fd824e956065ea493c86520c6c4706eafaef186fe6b36d7`; 473 files + 7 symlinks match staging; 723 source records stable); independent package and installer reviews passed with zero Critical/Important findings. Installation/readback passed: stable `current` resolves to `/models/codex-builds/LLPlayer_ru/installed/LLPlayer-0.3.62-linux-x64-recognition-advanced-f080a30143d8`; verified 473 files and 7 symlinks use the reviewed `tarfile.data_filter` modes, old package/critical hashes are preserved, both desktop files are unchanged, and XDG state has no writes. Independent final verification review passed with zero Critical/Important/Minor findings. Native GUI, Windows, and full F-13 point-4 parity remain open. See [`docs/agent/plans/2026-10-07-f13-recognition-advanced.md`](../docs/agent/plans/2026-10-07-f13-recognition-advanced.md).
- Translation settings use a detached Core record and atomic `LLPlayer.Translation.json`; **Save & Close** applies the chosen provider/target after persistence, while **Close** discards the draft. Dictionary lookup and sentence/subtitle translation default Off. Independent primary/secondary session switches opt into subtitle translation through the existing Flyleaf translator; this is a bounded Linux slice and full F-13 parity remains incomplete.
- The F-13 subtitle/sentence translation slice passed focused tests (engine 6; Avalonia settings/UI/controller 44) and the fresh Linux `--fast` gate after restore (FlyleafLib 1590 passed/6 skipped; Avalonia 189 passed/1 skipped; shell tests 64 passed; builds/validators passed). Publish/content validation and independent final verification passed with no Critical/Important findings for `artifacts/f13-subtitle-translation-2026-10-03/package-final/LLPlayer-0.3.62-linux-x64.tar.gz` (179,993,542 bytes, SHA-256 `7bb3c471d7f4aa74cd5bc54804e3cda313b35beddfaa21c99fbe1ab46b886a5b`). Its 96-entry manifest matched the package-stage snapshot; the final compiled-inputs projection is `artifacts/f13-subtitle-translation-2026-10-03/final-compiled-inputs.json` (SHA-256 `d2f0ef84a912201a6eb1ccd01fdfd4609a069eac857b44b6f2f62048a1260f9d`), which excludes nonpackaged acceptance documentation. Dark/light translated-cue renders passed pixel/content review. At that checkpoint, the launcher targeted `/home/maxim/Загрузки/LLPlayer-linux-x64/LLPlayer-0.3.62-linux-x64-subtitles-7bb3c471d7f4/llplayer.sh %F`; that launcher state was superseded by the chat slice, documented in [`docs/agent/plans/2026-10-03-f13-translation-chat.md`](../docs/agent/plans/2026-10-03-f13-translation-chat.md). Its receipt is `artifacts/f13-current-build-2026-10-03/installation-receipt-subtitles.json`. No real provider/GPU/model, native desktop GUI, or Windows runtime test was run; see [`docs/agent/plans/2026-10-03-f13-subtitle-translation.md`](../docs/agent/plans/2026-10-03-f13-subtitle-translation.md).
- The Core `WordLibrary` stores the existing `SavedWord` JSON array atomically in XDG `LLPlayer.WordList.json`. Candidate writes complete before the in-memory list changes; unreadable/corrupt files are preserved and block mutations with a warning until repaired externally. There is no import or recovery UI. AnkiConnect contacts localhost only after the user presses Send to Anki; no endpoint probes happen on load/save.

## File dialogs

Native dialogs come from xdg-desktop-portal (DBus) or GTK 3. On minimal systems without either, start with
`LLPLAYER_MANAGED_DIALOGS=1` to use Avalonia's built-in file chooser (the app also explains this in a toast when no
picker is available). Drag & drop and the command line always work.

## Theme

- `Themes/ShadcnTheme.axaml`: FluentTheme (palettes generated from the tokens) + token dictionary + control styles.
- `Themes/ShadcnTokens.axaml`: `Shadcn.<Token>Color` / `Shadcn.<Token>Brush` for Dark (default) and Light, radius,
  spacing, typography, Fluent resource-key overrides. The coloured regions are generated — edit and run
  `python3 Themes/tools/oklch_to_hex.py` (`--check` verifies they are up to date).
- `Themes/Controls/*.axaml`: shadcn variants (Button classes `secondary outline ghost destructive link`, sizes
  `sm lg icon icon-sm`; ToggleButton; TextBox; Slider (`thin`); CheckBox; ToggleSwitch; ComboBox; ListBox; Menu /
  ContextMenu / MenuFlyout; ToolTip; ScrollBar; `Border.card` / `Border.popover`; Separator; `Window.dialog`; Toast;
  `Border.badge` / `Border.kbd`).
- Developer gallery: `--theme-gallery` shows every styled control in both themes.

## Developer options

`--theme-gallery`, `--sidebar`, `--seek <s>`, `--screenshot <file.png> [--screenshot-delay <s>]` (renders the window to a
PNG and exits), `--dev-word-popup` (opens the word popup on the first subtitle word before the screenshot). Example
under Xvfb:

```sh
xvfb-run -s "-screen 0 1600x900x24" dotnet LLPlayer.Avalonia.dll test-720p.mp4 --screenshot shot.png --screenshot-delay 15
```

The first open of a video with an unlabelled subtitle file next to it takes several seconds because FlyleafLib detects
the subtitle language with Lingua (all language models are loaded once per process, as in the WPF app).

## Tests

Current Windows checks use `scripts/windows/verify-avalonia.ps1`; Linux uses `scripts/linux/verify.sh`.
Package checks use `scripts/windows/test-package-avalonia.ps1`/`publish-avalonia.ps1` or `scripts/linux/publish.sh`.
The same shared UI test sources run on both platforms; native environment skips must be reported explicitly.
The existing `scripts/codex/verify.ps1` remains required for WPF compatibility. See the current status link above
for fresh results; the dated slice receipts below retain their original scope and priority.

`dotnet test LLPlayer.Avalonia.Tests` — headless Avalonia (Skia) UI tests, view-model tests and service tests. The
end-to-end playback test (real engine, media and subtitles) runs when `LLPLAYER_FFMPEG_DIR` and `LLPLAYER_TEST_MEDIA`
(`test-720p.mp4` with `test-720p.srt` next to it) are set and is skipped otherwise. `LLPLAYER_EVIDENCE_DIR=<dir>` makes
the cheat-sheet test save a rendered PNG for visual review. The real recognition UI-command smoke additionally
requires `LLPLAYER_RECOGNITION_SPEECH`, `LLPLAYER_RECOGNITION_MODEL_DIR`, and `LLPLAYER_RECOGNITION_BITMAP` pointing
to existing local fixtures; optional secondary bitmap and artifact output variables are documented in the test.
It runs headlessly with a real Player and does not establish live-desktop or Windows owner acceptance.

The translation-preferences/WordPopup lifecycle slice has deterministic fake-provider tests and headless visual coverage;
the operation-CTS and save-admission race corrections passed 35 focused tests. The Linux `--fast` gate passed:
FlyleafLib 1554 passed/6 skipped and Avalonia 173 passed/1 skipped, with 60 shell tests, portable builds, and validators
passing. The package was
`artifacts/f13-translation-2026-10-03/package/LLPlayer-0.3.62-linux-x64.tar.gz` (179,141,059 bytes, SHA-256
`0b9505e7de323e23c2f943904da94142fcf0eed94d1d68034b317985c078603a`); its positive content validation passed and the
translation settings, backup, and dotted atomic-temp fixtures were rejected. This command does not run Windows
compilation. At that 2026-10-03 checkpoint, Windows runtime/UI acceptance was deferred under the then-current
Linux-priority direction; the later shared Windows/Linux implementation and current priority are recorded above.
Existing WPF CI contracts remain.

The F-13 word-library/definition/export implementation passed its focused Core (30) and Avalonia UI (25) tests; seven
headless PNGs passed root/UI visual inspection. At the words-slice checkpoint, the Linux `--fast` gate passed: FlyleafLib 1584 passed/6 skipped,
Avalonia 181 passed/1 skipped, shell tests 64 passed, and validators passed. Final archive/package APKG native proof,
verification review, and package validation passed. Archive `artifacts/f13-words-2026-10-03/package/LLPlayer-0.3.62-linux-x64.tar.gz`
is 179,984,997 bytes with SHA-256 `4496e5c019c25abcdc904447ef18680773f334ada085f9e82bd0ab2f7a723ed9`; the packaged native
SQLite 3.50.4 load probe passed. Managed tests validate APKG ZIP/SQLite structure; no real Anki import or live
AnkiConnect request was performed. At that words-slice checkpoint, the desktop launcher targeted
`/home/maxim/Загрузки/LLPlayer-linux-x64/LLPlayer-0.3.62-linux-x64-words-4496e5c019c2/llplayer.sh %F`; only `Exec`
and `Icon` changed. Seven installed hashes match, the old translation package and XDG data were preserved, and the
app was not launched. Receipt: `artifacts/f13-current-build-2026-10-03/installation-receipt-words.json`. Windows
compilation/runtime, live desktop acceptance, and live provider/Anki requests are outside this Linux slice.

The 2026-10-03 F-13 recognition gate passed: FlyleafLib 1554 passed/6 skipped and Avalonia 139 passed/1 skipped
(`artifacts/f13-recognition-2026-10-03/linux-full.log`). The real Player/UI-command smoke passed 2/2 in 14 s with
large-v3 CPU ASR and English/Russian PGS OCR (`real-ui.log`). The Windows-target test-project build passed with zero
warnings/errors. Linux publish and package validation passed; the archive is 179,121,762 bytes with SHA-256
`b2ad514a4eaa53ad0aecd4e188af934d5e22af88682a4db9f5ca060d5a5651a6`. Package negatives rejected the recognition
settings JSON and its `.bad` and leading-dot atomic `.tmp` variants (`package-negative.exit` reports harness pass).
Native desktop GUI and Windows runtime/owner acceptance remain pending.

## Manual smoke (not covered by automated tests)

Real Recognition window manager behaviour/focus and actual config-directory Save/Close, real X11 window manager
behaviour (fullscreen, focus), real audio output (after the OpenAL backend), native file
dialogs, clipboard with other applications, HiDPI scaling (`AVALONIA_GLOBAL_SCALE_FACTOR=2`), drag & drop from a file
manager, and long playback / 4K CPU load.
