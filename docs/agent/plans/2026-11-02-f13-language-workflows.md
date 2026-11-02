# F-13 language workflows Implementation Plan

> **For agentic workers:** Use `superpowers:subagent-driven-development` with explicit spawned workers and independent review. This is ONE owner-approved first delivery. Domain ownership below is coordination, not product substeps, new cards or permission gates.

**Goal:** Complete existing WPF ASR/OCR and model management, batch/watch-folder/background tray, dubbing and AI Insights workflows in one shared Windows/Linux Avalonia UI/Core delivery.

**Architecture:** Extend existing recognition/translation stores and serialized player ownership, extracting only reusable WPF-independent orchestration required by these workflows into Core. Reuse Flyleaf ASR/OCR, batch, AI and dubbing engines. Keep platform-specific native OCR, idle/activity, tray and child-process containment in bounded adapters. The coordinator owns app composition and shared integration files.

**Tech Stack:** C#/.NET 10, Avalonia 12.1.3, CommunityToolkit.Mvvm, portable FlyleafLib/FFmpeg, existing Whisper/Tesseract/AI/CosyVoice services, xUnit v3 and Avalonia headless tests. Preserve pinned dependencies unless required and independently reviewed for an authorized adapter.

**Spec:** Direct owner GO in the current thread on 2026-11-02: first deliver ASR/OCR + models AND all language workflows (batch/watch-folder/background tray, dubbing, AI Insights), with the full review/test/GitHub/main CI/Desktop/Vault ritual; then deliver the second commands/services + GPU/capture block; then perform owner-authorized Computer Use full F-13 visual acceptance and fixes. Base actual main `b11d168776dfab503f53af9681357702713a2ca3`, branch `codex/f13-av-language-workflows`. The source mapping below and ignored `artifacts/f13-language-workflows-2026-11-02/scope-matrix.md` define first-block coverage. Prior PR196/197 delivery stays closed.

## Global constraints

- The current GO authorizes first-block implementation and necessary delivery actions without a redundant approval request. Do not stop after a partial subset or use worker boundaries to narrow owner scope.
- SECOND block is authorized in sequence after the entire first delivery ritual: existing commands/services/help/About and remaining GPU/capture capabilities, including source-recording discoverability rather than inventing a second recording engine. This plan records sequencing only; first-block workers do not implement it.
- AFTER the second delivery, owner authorizes Computer Use for full F-13 visual/native acceptance and fixing discovered defects. First-block source/headless/package evidence must not be presented as that future acceptance. Do not launch the player or begin GUI acceptance prematurely.
- One Avalonia Views/ViewModels/Core implementation serves both operating systems. Retain WPF behavior/support, frozen defaults, existing accepted platform/service adaptations and independent WPF compatibility checks.
- No new F cards, providers, ASR engines, diarization, Demucs escalation, future voice features, generic refactors or unrelated dependency upgrades. F-07 remains DONE for WPF; its existing workflow is being ported under F-13.
- Preserve absent-preference defaults and unrelated persisted fields. Detached settings validate before persist, fresh-merge owned changes and persist before live application; Close/Escape discard. Keep existing fallback-language ownership in Playback.
- Opening/settings save/startup does not silently download engines/models, provision Python, contact providers or start inference. Explicit download/run actions are authorized product features. Packaging excludes downloaded weights, engines/venvs, mutable configs, credentials, logs and outputs.
- Microsoft OCR is a lazy Windows adapter with installed-language enumeration and actual recognition; Linux shows explicit unavailability while retaining Tesseract. Do not replace Windows behavior with a global unsupported flag. Unsupported hardware/runtime options show truthful availability and preserve user choices; first block does not introduce GPU decode/render.
- Background watch and tray must have one app lifetime owner: close-to-background preserves active work, explicit Quit cancels/drains all work and disposes watchers/processes; never leave an invisible unreachable process.
- Real providers/models/native audio/GPU and platform desktop acceptance retain actual PASS/NOT_RUN/UNKNOWN status. No fixed passing-test totals or coverage percentage gates; execute full unfiltered suites.
- All application/runtime additions stay separate from documentation/tooling commits. Preserve unrelated dirty work and concurrent Vault history; no broad staging/reset/cleanup.

## Review focus

1. Cancel/close/media or subtitle replacement during asynchronous work must reject stale completions, drain ownership and preserve replacement content.
2. Download cancellation, traversal/archive entries, partial files, deletion during inference and failed persistence must not destroy an existing usable model or unrelated file.
3. Watcher duplicate/rename/copy-in-progress/missing-root events must produce bounded natural-order jobs without overwriting outputs or silently changing persisted watch intent.
4. Background close/restore/quit and unavailable tray/idle APIs must keep work controllable; platform fallback must not falsely claim native activity support.
5. Dubbing subprocess exits/late assembly and AI provider malformed/length-limited/canceled replies must preserve explicit errors, output identity and safe cleanup.

## Complete WPF mapping

ADD means implement the existing WPF function in shared Avalonia/Core. KEEP means preserve an already delivered function. ADAPT means preserve function with an explicit platform adapter or truthful capability boundary; it is not permission to omit it. The ignored ledger keeps each mapped control/action and later source/test/evidence status separately.

### Recognition settings and live actions

Source: `LLPlayer/Controls/Settings/SettingsSubtitlesASR.xaml` and `.xaml.cs`; `SettingsSubtitlesOCR.xaml` and `.xaml.cs`; `FlyleafLib/Engine/WhisperConfig.cs`, `WhisperCppModel.cs`, `TesseractModel.cs`; `FlyleafLib/MediaPlayer/SubtitlesASR.cs`, `SubtitlesOCR.cs`; `LLPlayer/Resources/PopupMenu.xaml`; `LLPlayer/Controls/FlyleafBar.xaml`.

| Existing surface | First-block requirement |
| --- | --- |
| ASR engine selector / selected engine editor / installed status | ADD whisper.cpp and faster-whisper engine choice and actual installed status. Shared Avalonia adapts the independent WPF tabs to the matching editor selected by the detached engine draft; both engines remain editable before Save. |
| Language / automatic detection / translate to English / English-only model warning | ADD/KEEP existing semantics and warnings. Shared general fallback languages remain Playback-owned. |
| whisper.cpp model, prompt / presets, six CPU advanced fields | KEEP current model/CPU controls and complete prompt presets; validate nullable Threads, MaxTokensPerSegment, NoSpeechThreshold, AudioContextSize, MaxSegmentLength and SplitOnWord without changing defaults. |
| whisper.cpp NoContext, GPU device, runtime-library preference | ADD detached NoContext, nullable device, available/selected libraries with add/remove/up/down order and actual LoadedLibrary status. Availability is platform/runtime-specific; do not promise installed GPU runtime from an enum. |
| faster-whisper model / prompt / anti-hallucination | ADD existing options/presets and existing model catalog, including English-only advisory. |
| faster-whisper engine/model paths | ADD UseManualEngine/ManualEnginePath, UseManualModel/ManualModelDir, Open model folder; validate executable/directory and expose missing-tool errors. |
| faster-whisper process priority / extra arguments | ADD existing priority, literal extra arguments and existing presets. Preserve command quoting and parser constraints. |
| Copy help / Copy debug command | ADD clipboard-only existing command previews; these do not launch the engine. |
| Chunk size MB / chunk seconds | ADD both existing limits, correct config snapshot/application and defaults. |
| ASR start primary/secondary / pause/resume / cancel | ADD/KEEP both slot actions, checked/running/paused state and live progress/error surfaces; replacements/settings/shutdown use serialized cancellation and stale-source guards. |
| Existing generation policies | KEEP delivered WordTimestamps, VadCueSnapping, FixAllCaps and all previous eleven policy/limit fields; preserve ASRDenoise, ASRFoldBack and per-segment language defaults and display semantics where present. |
| OCR engine primary/secondary | ADD independent slot choice, actual Tesseract and Windows Microsoft OCR implementation. |
| Tesseract installed languages / size / regional preferences | ADD reloadable language inventory and grouped region selection; preserve `TesseractOcrRegions` behavior and existing preprocessing. |
| Microsoft installed languages / codes / regional preferences | ADD lazy Windows enumeration and `MsOcrRegions`; explicit Linux-unavailable UI. No Windows language-pack installer or silent system changes. |
| OCR live execution | KEEP/complete per-slot ownership, bitmap source selection, cancellation between cues, configured-region lookup and post-disposal native cleanup. |

### All three resource managers

Source: `LLPlayer/Views/{WhisperModelDownloadDialog,WhisperEngineDownloadDialog,TesseractDownloadDialog}.xaml`; corresponding ViewModels and `ModelDownloadDialogVMBase.cs`.

| Manager | Complete action/state parity |
| --- | --- |
| whisper.cpp model manager | Catalog and selected model; installed mark/size; Download; Cancel; Delete; Open folder; byte/total and indeterminate progress; status/error; disabled conflicting actions and close/drain lifecycle; reload recognition inventory on completion. |
| faster-whisper engine manager | Actual platform engine/artifact identity and source link; installed status; Download/extract; Cancel; Delete; Open folder; progress/status/error and guarded close. WPF Windows archive is not a Linux executable; Linux adapter must use a supported artifact or explicit user-provisioned engine route with truthful download availability. |
| Tesseract model manager | Language catalog and selected traineddata; installed mark/size; Download; Cancel; Delete; Open folder; progress/status/error; guarded close; refresh language/region inventory. Linux system tessdata may be read-only: use explicit writable user model storage compatible with the adapter, never mutate system directories implicitly. |
| Shared download lifecycle | One owner per target; stream to temp then atomic publish; failed/canceled download preserves existing target; reject unsafe extraction paths and symlinks; delete only managed target after idle ownership; never block UI waiting on native/model work. |

### Batch, watch folder, background and tray

Source: `LLPlayer/Views/BatchSubtitlesDialog.xaml`, `ViewModels/BatchSubtitlesDialogVM.cs`; `Services/BatchFolderWatcher.cs`, `BatchActivityService.cs`, `BatchActivityMonitor.cs`, `AppTrayService.cs`; `FlyleafLib/MediaPlayer/Batch/`.

| Existing surface | First-block requirement |
| --- | --- |
| Folder path / Browse / Scan | ADD path validation, natural-order video scan, recursive scope, grouped folders with counts; scan cancellation and safe per-file probe errors. |
| Jobs table | ADD include/all-none/mixed state, selected job, filename, phase, subtitle count, output name/error, per-file audio stream selection; row double-click/Open in player and per-row Retry. |
| Persistent options | ADD LastFolder, Recursive, OverwriteExisting, SerializeAsrAndTranslate, RunOnCpuWhenActive, idle seconds 5..600, PreferRussianAudio, GenerateDubbing and WatchFolder; preserve existing defaults and failed-save warning behavior. |
| Audio and processing policy | ADD preferred Russian stream with explicit per-job override winning; existing ASR-to-Russian translation/output rules; snapshot run inputs; no concurrent ASR/translation when Smooth enabled, no ASR/TTS overlap when dubbing enabled. Match WPF global BOM ownership: read existing PlaybackSettings.SubtitleExportUtf8Bom once when creating each run and use that snapshot for both writer and processor options. No independent batch BOM checkbox or persisted field; a later global edit applies only to the next run. |
| Run controls | ADD Start, Cancel, Retry failed, Open output folder, Close/Escape, enablement/idle/busy states. Existing output handling must retain skip-existing/overwrite policy and safe atomic naming. |
| Live display | ADD active job/transcript, processed/total media time, elapsed/ETA/throughput, row and overall progress, summary, CPU-vs-GPU status, errors. |
| Watch lifecycle | ADD create/rename/change coalescing, file readiness, duplicate suppression, idle-only auto-start, recursive re-point, vanished-root session disable without persisting off, toggle-off/quit disposal and no UI reentrancy. |
| Background lifetime | ADD batch close/main close-to-background while running or watching, paused hidden playback, restore main/batch window, no invisible idle orphan; explicit quit cancels/drains runs and watchers. |
| Tray | ADD lazy app icon, truthful progress/status, Open player, Batch subtitles and Quit actions, restore/double-click and completion notifications where platform supports them. Cancellation remains reachable through the restored batch window. If a Linux desktop offers no tray, retain visible controllable windows and explain capability rather than hide them. |
| Activity policy | ADAPT Windows idle detection and Linux available activity API without inventing a GPU coordinator. Unknown activity cannot be advertised as measured inactivity; preserve bounded existing CPU policy and explicit fallback. |

### Existing dubbing end to end

Source: `LLPlayer/Controls/Settings/SettingsSubtitlesDubbing.xaml` and `.xaml.cs`; `FlyleafLib/Engine/DubbingConfig.cs`; `FlyleafLib/MediaPlayer/Dubbing/`; `LLPlayer/Views/SubtitlesSidebar.xaml:702`; `SubtitlesSidebarVM.cs`; batch renderer/autoload integration.

| Existing surface | First-block requirement |
| --- | --- |
| Voice settings | ADD default voice picker, existing built-in bank plus current unknown ID; custom IDs list/add/remove with trim/dedup and retained selection. |
| Mix/timing/output | ADD DuckingPercent 0..100, AtempoMin/AtempoMax 0.5..2.0 (existing inverted-order semantics), FLAC-only output and existing defaults 15, 0.9/1.15, flac; preserve StressNormalization=true. |
| Engine configuration | ADD/KEEP existing TtsServiceType=LocalCosyVoice, model=cosyvoice2-0.5b, UseManualEngine/ManualVenvPython and existing provisioned path semantics. No new cloud TTS/provider/model or automatic environment installation. Explicit missing-runtime guidance is required. |
| Per-line voice | ADD sidebar voice menu, assigned mark, selected voice / inherit-default / clear overrides, media/cue identity, session map and immutable render input; no per-speaker feature. |
| Companion persistence | ADD opt-in PersistPerLineVoices=false, `.ru.voices.json` save/restore/clear with atomic queued writes and stale-target protection; batch merge of current-session/disk assignments. |
| Batch rendering | ADD Generate Russian dub checkbox/default voice, render after translated or existing `.ru.srt` where supported; preserve render-only/skip-existing rules, FLAC path identity and no ASR/TTS overlap. |
| Autoload / audio selection | ADD existing generated dub discovery and attachment as a selectable external audio track with stale media guard. Match WPF: attachment does not select/play the track automatically; no second generation or unsolicited playback. |
| Platform sidecar lifecycle | ADAPT Windows JobObject and Linux process-group/parent-death containment, venv `Scripts/python.exe` versus `bin/python`, parent-watchdog and cancellation; terminate/drain child before final output cleanup to prevent late assembly resurrection. No new GPU coordinator or future voice roadmap. |

### AI Insights complete workflow

Source: `LLPlayer/Views/AiInsightsDialog.xaml`; `ViewModels/AiInsightsDialogVM.cs`; `Services/AppActions.cs:546`; `FlyleafLib/MediaPlayer/AI/`; F-07 source contract `docs/agent/backlog.md:581-599`.

| Existing surface | First-block requirement |
| --- | --- |
| Entry/lifetime | ADD visible Subtitles → AI Insights action and existing window action route, singleton window, close cancellation/drain, no request on open. |
| Inputs | ADD output modes Summary/Vocabulary/Both, primary/secondary slot, original/translated text, configured LLM/model indicator; snapshot actual cues safely. |
| Generate / Cancel | ADD actual existing selector/service reuse, can-generate updates when subtitles appear, busy/progress/cancel and incomplete-ASR/OCR warning; no new provider or silent non-LLM fallback. |
| Long transcripts / failures | KEEP char-budget cue chunking/map-reduce, PartialCoverage warning, length-truncation errors, malformed vocabulary raw reply display and stale/canceled result rejection. |
| Results | ADD readable/copyable summary, vocabulary Term/Reading/Translation/Definition/Example columns, progress and raw reply fallback. |
| Copy / Save / Add to List | ADD existing summary/TSV selection semantics, explicit save picker/cancel and correct extension/encoding, deduplicated saved vocabulary with AIInsights provenance, source/target language and save timestamp; existing Word Manager/Anki/APKG routes remain intact. |

## Coordination and file ownership

- Recognition worker: recognition settings/view-model/services and model manager UI/Core logic, scoped tests; Windows OCR/native adapters by explicit native-owner coordination.
- Batch/dubbing worker: shared batch/dubbing settings/orchestration/UI and tests; runtime owner handles platform child containment.
- AI worker: AI Insights UI/view-model/shared orchestration and tests.
- Coordinator: App/MainWindow composition, interfaces, shared menus/settings links/prefs integration, conflicting files and final source matrix reconciliation. Workers are not alone in the codebase and must preserve each other's edits.
- Follow architecture-owner interface report before adding shared service APIs. Domain files use existing naming/patterns; this plan does not authorize parallel edits of coordinator-owned composition points.

## Implementation and verification checklist

- [x] Pin approved domain interfaces/file ownership; reconcile every mapped action against WPF and current source in the ledger. Keep unsupported platform facts explicit and all original scope rows.
- [x] Implement complete recognition/model/OCR mapping with deterministic regressions for engine/path/runtime/chunk/region edits, manager download/cancel/delete failure and per-slot stale completion; compiled recognition/model/resource-admission RED receipts and focused GREEN are retained.
- [x] Implement complete batch/watch/tray mapping with deterministic regressions for natural-order inclusion/audio/output, watcher readiness/coalescing/persisted intent, CPU policy and close/background/quit ownership; compiled batch RED and focused GREEN are retained.
- [x] Implement full existing dubbing mapping with deterministic regressions for detached config/custom/per-line voices, companion persistence, render-only/autoload identity and cancellation/late assembly; compiled save-admission RED and focused GREEN are retained.
- [x] Implement full AI workflow with deterministic regressions for modes/input/can-generate, canceled provider/length-limited/malformed replies, copy/save and deduplicated Add to List; focused GREEN is retained. No unrecorded AI-specific RED run is claimed.
- [x] Integrate coordinator-owned menus/windows/settings/app lifetime and actual platform adapters; source and focused tests cover one shared UI, explicit I/O and preserved defaults. Native platform behavior remains separate acceptance.
- [x] Reconcile each of the 257 original ledger rows with source/test mapping or explicit supported platform adaptation. No omitted approved control/action remains in the source audit.
- [x] Run unfiltered `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\codex\verify.ps1`: `full-wpf-02.json` exit 0, 1496 passed / 0 failed / 2 skipped. This is working-tree local evidence, not final committed HEAD evidence.
- [ ] Run Windows `verify-avalonia.ps1`, `test-package-avalonia.ps1`, `publish-avalonia.ps1` from `scripts/windows/`; run Linux `scripts/linux/verify.sh` and `scripts/linux/publish.sh` on Linux/CI. Preserve initial failures; diagnose before retries. Test totals are observations.
- [x] Independently inspect actual staged/extracted package/native and legal notices, portable plugin loads and downloaded/model/config/output exclusions: `package-review.json` PASS, 0 Critical/Important; `plugin-staged-01.json` and `plugin-extracted-01.json` each PASS 1/0/0. These are local publish02 artifact results.
- [x] Run cumulative spawned source/domain reviews and explicit final source `/review`; coordinator reports all confirmed Critical/Important findings closed, including latest root lifecycle fixes. Subsequent documentation/source-seal deltas retain their separate pending readback.
- [ ] Update matching frozen contracts/manual smoke, F-13 backlog/status and actual completion ledger. Commit reviewed product and docs/tooling separately with exact allowlists.
- [ ] Use GitHub plugin to recheck target/main/open PRs; publish reviewed first-block branch/PR, inspect required Windows/Linux checks, merge authorized reviewed head, then verify distinct actual-main CI and actual main identity.
- [ ] From clean actual final main run default fresh `scripts/windows/update-desktop-test-build.ps1`. Independently verify sourceCommit=HEAD, payload/ZIP/installed bytes, direct EXE/no args/payload cwd/blue icon/physical paths/current.json; preserve old payloads and WPF shortcut. This is delivery, not literal click acceptance.
- [ ] Complete exact-scope Second Brain candidate/review/CAS apply/session-close/local commit, preserve foreign history and frozen files; record actual receipts. Vault push/public Dashboard/memory changes require existing explicit authority.
- [ ] Record first-block CLOSEOUT with source, actual gates/skips/failures, reviews, PR/main CI/Desktop/Vault identities and unrun native boundaries; then begin authorized SECOND whole block. Full F-13 remains open until second delivery and subsequent owner-authorized visual acceptance/fixes are completed.

## Current evidence

Requirements guardian source audit, 2026-11-02: all 257 original scope rows are reconciled to current working-tree implementation and related test source in the ignored ledger. Recognition/models/OCR, batch/watch/background/tray, existing dubbing and AI workflow surfaces are present. The runtime-preference catalog gap found during audit is corrected in `RecognitionViewModel.cs:67`, with a new optional-runtime/detached-save regression in `RecognitionParityTests.cs:141`; this is source/test-source evidence, not a test execution claim.

The reuse-review BOM ownership finding is source-corrected: `BatchRunFactory` snapshots the existing global subtitle export preference per run, and AppHost wires the current Playback owner. Batch has no independent BOM UI/config field. `FlyleafLibTests/Core/BatchOutputPreferenceTests.cs` covers false/true global values, immutable current-run policy, next-run updates and ignoring/removing the unshipped draft field; execution remains coordinator-owned and is not claimed here.

Explicit supported adaptations: the detached engine selector chooses its matching editor; Linux faster-whisper automatic installation is unavailable and a compatible manual executable is supported; Microsoft OCR is Windows-only with Linux unavailable; uncertain Linux tray/global-idle capabilities retain a reachable window and conservative CPU fallback. No other omitted first-block control/action was identified by the static requirements audit.

### Actual local checkpoint; first delivery IN PROGRESS

Coordinator-run receipts under `artifacts/f13-language-workflows-2026-11-02/` record:

- `full-wpf-02.json`: PASS, 1496/0/2 (passed/failed/skipped).
- `full-windows-avalonia-02.json`: PASS, engine 1762/0/15 and UI 1863/0/1; offline package fixtures 157 PASS.
- `green-ui-regression-07.json`: PASS, extended UI 176/0/0; `green-core-second-02.json`: PASS, 120/0/2; `python-lifecycle-01.json`: PASS, four lifecycle checks.
- `windows-publish-02.json`: fresh publish PASS, 503 files, 206504499-byte ZIP, SHA-256 `23c1fd72fdb738320ba22aebaf68e82e5e83f203eba94c6167b2fadd61fcea18`. This is a package from the dirty working tree based on B11, not a committed final-HEAD/main/Desktop package. Independent native/release artifact review `package-review.json` PASS, 0 Critical/Important; staged and extracted plugin loads each PASS 1/0/0 (`plugin-staged-01.json`, `plugin-extracted-01.json`).
- Compiled intentional RED receipts remain `red-ui-parity-01`, `red-core-parity-01`, `red-resource-admission-03` and `red-dubbing-admission-01`. Initial failed/nonqualifying attempts remain in the original receipts/logs; no later GREEN relabels them.
- `full-wpf-01.log`, `full-windows-avalonia-01.log` and `windows-publish-01.log` remain original FAIL evidence. Publish01 hit strict-mode project-version property access; publish02 follows the XPath version-read correction. Test-failure causes/corrections remain with their original coordinator receipts; no blind-retry/fixed-native claim is inferred.

Source/domain reviews have no unresolved Critical/Important findings at the coordinator's latest lifecycle checkpoint. `source-seal-03.json` is a dated seal, not evidence for subsequent publisher XPath and documentation changes; the next source seal/readback remains pending. Test totals above are observations, not quality thresholds.

Linux/PR checks, merge and distinct actual-main CI, final source/doc seal, clean final-main blue Desktop delivery, and scoped Vault closeout remain PENDING. Native Windows/Linux GUI, live models/providers, physical A/V/input/GPU/idle/tray/OCR and literal Desktop click acceptance remain NOT_RUN. The first approved group is still delivery IN PROGRESS; the authorized second whole block and later full F-13 Computer Use acceptance/fixes follow the first ritual. Guardian ran no builds or tests and makes no final-HEAD or future acceptance claim.
