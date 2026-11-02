# F-13 remaining Settings, Subtitles/words and Playlists/online video Implementation Plan

> **For agentic workers:** Use `superpowers:subagent-driven-development` with explicit spawned workers and independent review. This is one owner-approved delivery and one PR. The ownership boundaries and verification steps below are implementation coordination, not new product stages or backlog cards.

**Goal:** Complete the three approved existing F-13 areas in the shared Windows/Linux Avalonia UI: remaining Settings, Subtitles/words, and Playlists/online video.

**Architecture:** Extend the existing detached Settings transaction, shared preferences and playback adapter. Keep engine-owned audio/video/subtitle/playlist state behind the adapter, snapshot UI inputs, preserve source identity and cancellation, and expose existing portable capabilities through one shared UI. Retain explicit user-provisioned yt-dlp/Deno execution with no startup probe, download or install.

**Tech Stack:** C#/.NET 10, Avalonia 12.1.3, CommunityToolkit.Mvvm, portable Flyleaf/FFmpeg, xUnit v3/headless Avalonia; existing pinned WPF dependencies are references for reuse, not permission for unrelated upgrades.

**Spec:** Owner GO in the current thread for all three groups together, including the previously proposed complete Audio/output/recovery behavior. Source base `280f58278752a7375e1068563740a71355f624f6` (delivered PR #195); branch `codex/f13-av-settings-subtitles-online`. The source mapping below and the ignored working ledger `artifacts/f13-settings-subtitles-online-2026-11-02/scope-matrix.md` define closure coverage. PR #195 is closed context, not work to repeat.

**Source checkpoint, 2026-11-02:** The three mapped source areas are implemented; the ignored ledger now reconciles
every original feature row with source/test pointers or an explicit supported adaptation. These include typed
current-plugin options, REST text-only OpenSubtitles/Unavailable duration, Windows PDIC/Linux unavailable and the
previously accepted Avalonia DIP layout. WPF remains supported and full F-13 remains in progress.
`full-wpf-01.json` records exit 0 (1445/0/0); `windows-avalonia-full-02.json` records exit 0
(engine 1706/0/13, UI 1719/0/1; zero build warnings/errors). Totals are observations, not quality thresholds.
The first Windows full attempt remains a failure: engine 1706/0/13, UI 1716/1/1 because a fixture tried Save during
legitimate default subtitle-cache loading. The correction waits for event-driven cue/cache readiness and retains a
real native-busy no-write control; no busy guard was relaxed and no flaky label is used. Actual mute/max-volume RED
and the corrected source-identity union GREEN (6/0/0) are separately retained. Package fixture tests and focused
checks do not establish actual publish/CI/merge/Desktop/Vault results, all of which remain pending their own receipts.

## Global constraints

- One Avalonia view/view-model/Core implementation serves Windows and Linux. WPF remains supported. Preserve unrelated product, media-runtime, config/data, design, dependency and package contracts; update matching descriptions for this approved additive behavior in the same branch.
- Preserve current absent-preference/startup defaults. WPF identifies the functions to port; do not silently replace an existing Avalonia default with a different WPF default. Explicitly record new-field defaults and effective values in tests and contracts.
- Settings are detached drafts. Validate raw text and cross-field invariants before persistence; fresh-merge only edited/owned values; persist before live application. Close/Escape discards. Failed save keeps the retry draft and preserves unrelated persisted/live state. Runtime-only values, including media delays and bitmap placement, retain engine lifetime/reset semantics.
- Audio Default Volume follows the accepted proposal: editing it and saving updates both remembered and current volume; leaving it untouched preserves the latest current/remembered volume, including volume changes while Settings is open. A lowered maximum may clamp the effective volume only through the agreed bounded policy. Do not reset live volume on every Settings save.
- No newly enabled provider/helper/model requests or external process execution on startup, load, Settings open or Save. OpenSubtitles search/download, online extraction and external helper execution remain explicit. The approved Audio recovery uses a two-second, single-flight refresh through the existing serialized mutation queue in addition to the explicit device-refresh action. Do not install/probe yt-dlp or Deno, add a helper updater, or bundle these tools.
- Native Windows/Linux desktop, physical A/V/input, providers/models/GPU/DPI and literal shortcut-click/rendered-icon acceptance are `NOT_RUN / DEFERRED BY OWNER`. Automated native-library checks and packaging do not change that status.
- New ASR backends, model-management parity, batch/dubbing, command palette/executable help, About, new GPU/capture backends and other existing F-13 remainders remain separately listed and are not authorized by these three groups. Editing the existing subtitle-generation policy/limits listed below is part of remaining Settings; it does not authorize inference or a model manager. Do not invent new F cards or silently move approved controls into another stage because implementation is difficult.
- The owner answered the PDIC platform choice: implement the lazy Windows bridge and explicit Linux-unavailable state. Preserve the external manual-path/Auto Set workflow and app-singleton lifetime; do not substitute a Linux dictionary or perform startup probes. Native PDIC acceptance remains deferred.

## Complete WPF mapping and portable capability boundary

`ADD` means approved work or completion of an incomplete surface. `KEEP` means an existing implemented owner/behavior must survive and remains in regression coverage. `CAPABILITY` identifies an actual backend boundary; it does not excuse an unported UI for an existing working portable feature.

### Remaining Settings

WPF references: `LLPlayer/Controls/Settings/SettingsAudio.xaml` and `.xaml.cs`; `SettingsVideo.xaml`; `SettingsPlayer.xaml`; `SettingsPlugins.xaml` and `.xaml.cs`; `FlyleafLib/Engine/Config.cs`.

| WPF function | Shared Avalonia requirement | Boundary / evidence |
| --- | --- | --- |
| Audio Enabled | ADD committed enable/disable with engine state | `AudioConfig.Enabled`; distinguish disabled audio from a failed/silent backend |
| Audio Use Filters | ADD `FiltersEnabled`, effective FFmpeg-profile compatibility | Main load profile cannot supply avfilter; saved intent/effective state must be explicit |
| Audio Delay | ADD editable ms, bounded conversion to ticks and existing resync | Session/stream state resets on new audio stream; do not persist it as a global offset |
| Max Volume (1–1000%) | ADD maximum, live slider/wheel/keys and clamping consistency | Preserve existing default maximum `150`; zero volume remains valid |
| Default Volume | ADD dirty-edit behavior above | Existing Avalonia remembered/startup volume `100` survives absent fields; WPF `75` is not a migration default |
| Ordered audio languages / Configure | ADD add/remove/reorder valid unique language preferences | Used by current automatic A/V selection; do not launch extraction to validate |
| Default Device and Device | ADD default/current-device display and explicit selection | Surface backend identity/status; preserve missing preferred device instead of falsely marking it selected |
| Whole Audio/output/recovery proposal | ADD output status, enumerated devices, explicit refresh and bounded two-second single-flight recovery | Existing serialized queue owns refresh/recovery; temporary Null clock drops old PCM, accepts future samples after recovery, retains explicit target through default fallback/return; Audio Off/forced Null preserved |
| Aspect Ratio and custom rational | ADD named modes plus validated positive custom ratio | Portable `Renderer.Portable.VP.cs` consumes these values |
| Deinterlace and Double Rate | ADD existing software field order/bwdif behavior | `Renderer.Portable.Convert.cs` and `SoftwareFramePreprocessor` implement this today |
| Decoder Threads | ADD validated decoder preference with honest apply lifetime | Existing software decoder consumes VideoThreads; do not reinterpret it as GPU support |
| Zoom Unit | ADD `ZoomOffset` and existing zoom commands | Keep current zoom/render ownership |
| Max Latency and Zero Latency | ADD validated latency controls / engine side effects | Validate interactions with buffer duration/minimum before applying |
| HDR to SDR Method and SDR Display Nits | ADD existing software tone mapping / luminance selection | Existing `zscale`/`tonemap` path; unavailable filter/runtime must be reported honestly |
| Flyleaf filters | ADD Brightness, Contrast, Hue, Saturation, individual defaults and Reset all | Software filter ranges come from live/canonical specs; pending draft reset must not mutate live renderer |
| Super Resolution, VSync, Video Acceleration, Profile Mismatch, D3D processor selection and D3 filters | CAPABILITY: truthful unavailable state for this software backend; retain WPF behavior | Portable renderer selects `SwsScale`; `D3Filter.Available=false`, VSync is informational and Profile Mismatch is consumed only inside `if (VideoAccelerated)`. No new GPU/capture backend in this GO |
| Buffer Duration Min / Max | ADD ms editors for Player.MinBufferDuration / Demuxer.BufferDuration | Preserve engine coupling and `minimum <= maximum` rather than relying on setter order |
| Audio / Video Frames Max | ADD positive bounded frame counts | Preserve queue/reopen lifetime and protect excessive allocation |
| Vertical Resolution Max | ADD `MaxVerticalResolutionCustom`, `0` retains auto | Shared stream suggestion/render monitor path |
| Open / Read / Read Live / Seek Timeouts | ADD four ms editors | Overflow-safe ms-to-ticks conversion; retain their distinct meanings |
| Always Seek Accurate / Fix Margin | ADD `SeekAccurate` and margin | Preserve fractional subtitle seek/slot-delay semantics |
| Log File / Level / FFmpeg Level / Cached Lines | ADD detached persisted logging editor with honest live/restart behavior | Keep app log/crash ownership and existing `LLPLAYER_LOG_LEVEL` precedence explicit |
| FFmpeg Load Profile | ADD persisted next-start profile and clear restart status | Do not reload global FFmpeg while playing; profile cannot silently strip required filter functionality |
| Plugin preferences | ADD the installed option-bearing YoutubeDL plugin's height/prefer-combined/extra arguments | WPF currently exposes these three values; built-in plugins have empty option dictionaries. One persisted online settings owner, no helper/network execution merely to render Settings; no future arbitrary-plugin editor is claimed |
| Player UI, Mouse, Always On Top, Keys, appearance and existing Tools | KEEP previously delivered controls, defaults, routing and Save semantics | PR #195 and earlier blocks are preserved, not replayed |

### Subtitles and words

WPF references: `SettingsSubtitles.xaml`, `SettingsSubtitlesPS.xaml`, `SettingsSubtitlesAction.xaml`, `SelectableSubtitleText.xaml.cs`, `Views/SubtitlesSidebar.xaml`, `ViewModels/SubtitlesDownloaderDialogVM.cs`, `Views/SubtitlesDownloaderDialog.xaml`, `Services/OpenSubtitlesProvider.cs`.

| WPF function | Shared Avalonia requirement | Boundary / evidence |
| --- | --- | --- |
| Cached bitmap cues in sidebar / preview | ADD selected-slot image thumbnails, current/future visibility and seek behavior | `EnabledCached` owns engine caching; no OCR to manufacture text. Respect bitmap ownership/disposal and avoid unbounded per-refresh copies |
| Middle-click sentence lookup | ADD whole rendered cue, `IsWord=false`, correct slot/languages/translated provenance | WPF middle-click uses full cue, not the single clicked token. Preserve pause/popup/input ownership |
| Japanese overlay segmentation | ADD existing WPF MeCab/IpaDic word boundaries preserving original offsets/whitespace | Reuse the existing pinned tokenizer dependency through a portable seam; do not substitute per-character splitting and claim parity |
| Chinese overlay segmentation | ADD WPF Chinese-character/punctuation/whitespace rule preserving offsets | Preserve apostrophes/hyphens, mixed scripts, Unicode and phrase-drag selection |
| Manual OpenSubtitles query | ADD explicit HTTPS REST search/pages with dialog-only API key/username/password; current-title refresh/cancellation | WPF XML-RPC is a historical reference. Opening/changing title never auto-searches; credentials/tokens are cleared at dialog close |
| OpenSubtitles results and sort | ADD Language/FileName/Format/Year/Downloads/Rating/release; Duration explicitly Unavailable; preferred languages then language/download/name ordering | REST adaptation supplies no duration metadata; empty/error results never replace current subtitles |
| OpenSubtitles Load | ADD text validation, unique owned temp child, Primary load and downloaded-language/ManualDownloaded metadata | Capture media before network waits; adopted files stay owned until full Player.Dispose, exact-path nonrecursive cleanup; no stale load |
| OpenSubtitles Download | ADD explicit picker/path and UTF-8/BOM text policy | Current REST adaptation explicitly lacks bitmap/archive download; no invented binary parity. Confined filenames, bounded transport and contextual errors; no overwrite without picker confirmation |
| PDIC path/Auto Set/action | ADD owner-selected Windows bridge; Linux explicit unavailable | Lazy singleton, unchanged sentence/exact one-based UTF-16 word offset, framed UTF-8 JSON, bounded writes/Close/process cleanup; no startup probes |
| Subtitle Enabled / SearchLocal / SearchLocalPaths / OpenAutomaticSubs / EnabledCached | ADD the missing general editors and persistence, preserving effective defaults | SearchLocal/Paths, OpenAutomaticSubs and EnabledCached are independent from a model manager; no silent omission |
| Subtitle language auto-detection, fallback, secondary-same and ordered preference list | ADD missing general editors; share existing recognition-owned fallback rather than duplicate it | RecognitionSettings currently owns only Whisper, WhisperCpp and OCR fallback. Avoid another owner overwriting a newer Recognition draft |
| Export UTF8 with BOM | ADD persisted default, true when absent, consumed by export and manual OpenSubtitles text download | Existing `SubtitlesExportViewModel.IsUtf8Bom=true` checkbox is session-only; preserve per-operation snapshot/explicit override |
| Delay Primary / Secondary | ADD two raw ms editors with current-session snapshot and guarded live apply | `SubConfig.Delay` is JsonIgnore and resets on every new subtitle stream; existing row-sync is not a numeric editor. Untouched or stale-source drafts cannot reset a newer delay |
| ResegmentSubtitles | ADD existing policy editor and persistence; default true | Currently missing from Avalonia Recognition/Settings, not covered by whisper.cpp MaxSegmentLength |
| WordTimestamps / VadCueSnapping / FixAllCaps | ADD three policy editors; defaults true / true / true | Existing engine behavior and fail-soft/interactive-vs-batch distinctions remain unchanged; Save starts no inference |
| ASRDenoise / ASRPerSegmentLanguage | ADD two policy editors; defaults false / false | Existing frozen opt-in defaults; no new denoiser/backend/model work |
| SubtitleMaxLinesPerCue / SubtitleMaxCharsPerLine / SubtitleMaxCjkCharsPerLine | ADD raw validated limits; defaults 3 / 48 / 24 | Currently absent from RecognitionSettings and both UI dialogs; do not substitute MaxTokensPerSegment |
| SubtitleMaxCueDurationSec / SubtitleMinCueDurationSec | ADD finite validated limits; defaults 7 / 1 seconds | Preserve existing zero-disables semantics and max/min relationship accepted by SubtitleSegmenter; record exact validation |
| Existing selected Primary/Secondary sidebar, search, seek and sync | KEEP identity guards, translated/original display, spoiler mask, current highlight and typography | Empty selected slot stays empty; stale source/retired row commands never affect replacement media |
| Existing overlay typography/layout and bitmap placement | KEEP dual-slot layout, fonts/colors/background/outline, separator and session-only bitmap scale/position | Percentage/fix-overflow/IsAbsolute WPF layout differences remain explicitly inventoried; no accidental reset from unrelated Settings save |
| Existing left click/drag/right action menu, copy-on-select, last-search modifier and auto-copy | KEEP all delivered Word Actions behavior and clipboard/browser admission | Middle-click/CJK additions must not weaken existing click-time snapshots or change masked-row provenance |
| Existing provider/dictionary/word list/Anki/export and existing Recognition model/CPU/OCR editors | KEEP detached prefs, cancellation, saved metadata and explicit I/O | Existing generation-policy editors above are added; new backend/model-management, batch/dub and provider-run acceptance remain outside |

### Playlists and online video

WPF references: `Controls/FlyleafBar.xaml:294`, `Resources/PopupMenu.xaml:330`, `Services/AppActions.cs:131`, `FlyleafLib/MediaFramework/MediaPlaylist/Playlist.cs`, `Plugins/YoutubeDL/YoutubeDL.cs`. Portable baseline: `YoutubeDL.Portable.cs`, `OnlineVideoSettings.cs`, `OnlineVideoViewModel.cs`.

| WPF function | Shared Avalonia requirement | Boundary / evidence |
| --- | --- | --- |
| Playlist item list/count/loading expectation | ADD live shared list/count/ExpectingItems status | Snapshot via existing collection bridge; item identity is scoped to the current media/open generation |
| Item title and selected mark / reopen | ADD exact-item selection and current checked state | Engine opens the selected item; rejecting stale same-title/same-URL item tokens is mandatory |
| Previous / Next playlist item | ADD transport/menu actions and availability | Distinct from filesystem sibling navigation; do not wrap where engine PrevItem/NextItem is null |
| Title left-open and context Open next / Open prev | ADD current-file folder siblings with WPF sorting/filter/error behavior | WPF FileHelper.GetNextAndPreviousFile selects the same extension case-insensitively, natural filename order, no wrap; no startup scan |
| Open path / Copy path | ADD current local folder or admitted URL and exact clipboard path | Explicit action only; preserve URL/local distinction and contextual failures |
| Paste/Open online URL | KEEP explicit tools and safe local/direct HTTP/Ctrl+V behavior | Absent tools must not intercept otherwise playable direct media |
| yt-dlp `MaxVideoHeight` | ADD validated height `0..32768`, default `720`; `0` uses the engine limit | Current shared GetBestMatch consumes it; persist through the plugin option owner |
| yt-dlp `PreferVideoWithAudio` | ADD Boolean option, default false | Preserve current audio-language and best-format selection, chapters and headers |
| yt-dlp `ExtraArguments` | ADD safely tokenized, explicitly saved preference mapped to allowed helper execution | WPF exposes this option. Do not silently omit it; retain no-shell invocation and protect internal output/admission/runtime options. Document any exact reserved options rather than claiming unrestricted parity |
| Full extractor playlists/multi-video | ADD playlist extraction/item publication and playlist navigation | Current portable code rejects both types and forces single-video flags; implementation must replace that limit, including partial results, errors, cancellation and late completion guards |
| Audio/video selection and external subtitles | KEEP shared automatic selection plus existing A/V track and subtitle assignment UI | Preserve VTT automatic-caption/language policy and OpenAutomaticSubs behavior; bounded network/auth arguments are additive; DRM/new remote plugin execution are not implied |
| External yt-dlp and Deno configuration | KEEP unset paths, explicit validation and saved-path admission | No bundling, updater, PATH search, startup process or live helper acceptance without separate native GO |
| Whole-file loop / A-B / waveform | KEEP existing playback semantics and reset behavior | Playlist actions must not introduce unrelated autoplay/shuffle/queue-edit/save behavior not present in WPF |

## Ownership and interfaces

The coordinator owns shared composition points and resolves worker interfaces before applying overlapping edits. Workers must preserve others' changes and communicate new public members before consumers use them.

| Owner responsibility | Primary files / interface |
| --- | --- |
| Audio/output/recovery | Focused audio preference/runtime snapshot/service files; audio partial of `SettingsViewModel` and playback adapter. Consume current `IPlaybackController.Volume`, `VolumeMax`, `Mute`; expose detached capture, validated apply and explicit refresh/recovery operations without storing native devices in prefs |
| Software Video / Player engine / logging / plugin / general subtitle settings | Focused immutable settings model and `SettingsViewModel` partial; `EngineBootstrap` next-start config; playback adapter apply. Own the eleven existing subtitle policy/limit editors, persisted BOM default and runtime-only primary/secondary delays; coordinate OCR fallback with Recognition's existing owner. Preserve engine setter coupling and keep startup-only profile distinct from effective live state |
| Subtitle/words | `Controls/SubtitleText.cs`, focused tokenizer, `SubtitlesSidebarViewModel`, bitmap cue adapter/data and OpenSubtitles shared client/view-model/window. Extend `CueInfo` compatibly while retaining `SubtitleCueIdentity`; reuse `IsCueCurrent`, `SeekToCue` and `SynchronizeSubtitleToCurrentTimeAsync` |
| Playlists/online | `YoutubeDL.Portable.cs`, shared plugin-option model, playlist adapter partial/view-model and online dialog. Publish detached item rows/tokens; online options have one owner shared by generic plugin editor and online UI |
| Local title actions | `Services/LocalMediaNavigation.cs`, `MainWindowViewModel.LocalMedia.cs` and dedicated tests. Generated command properties: `OpenNextLocalMediaCommand`, `OpenPreviousLocalMediaCommand`, `OpenMediaLocationCommand`, `CopyMediaPathCommand`. Snapshot `IPlaybackController.CurrentMediaPath` for the selected item, preserve the normal `Open` pipeline; Open path uses `IAppShell.RevealMediaAsync(string path)` for an existing local path or `OpenBrowserAsync(Uri uri)` for a safe credential-free HTTP(S) page |
| Coordinator | `AppPrefs`/store merge, `IPlaybackController`, `MainWindowViewModel` composition and main/settings AXAML integration, app startup and fakes if ownership would overlap. Wire worker partials instead of duplicating persistence or native owners |
| Scope guardian / local action worker | This plan, ignored scope ledger and the isolated local-title files/tests above. No shared composition/interface/Git/Vault writes |

Existing adapter signatures above are constraints, not permission to call engine/native objects from view models. Each added operation must define cancellation, source lifetime, effective status and apply/restart behavior. Pending async results must validate current media/slot/item/output identity again at execution and publication.

## Review focus

1. Old/malformed prefs, raw numeric errors, enum/rational validation and cross-field coupling: safe old defaults and no partial save/apply.
2. Concurrent volume changes, autosaves, device disappearance and persistence failure: current state survives untouched drafts; explicit edits win only after durable save.
3. Rapid media/track/slot/item changes, cancellation, shutdown and delayed helper/provider completions: stale work cannot mutate new media, list, cache or output sink.
4. Bitmap ownership/masking, Japanese/Chinese offset preservation and middle/left/right/drag event routing: displayed source and lookup provenance remain exact without duplicate gestures.
5. Hostile/malformed provider metadata, filenames, helper arguments and extractor outputs: bounded parsing/I/O, no shell execution, no path escape, no hidden startup work, recoverable user-visible errors.

## One coordinated delivery and evidence

Checked source steps below refer to the reconciled implementation and actual scoped evidence above. Delivery steps
remain unchecked until their own facts exist. The coordinator records exact commands/source identities, unfiltered
totals, original failures/skips, reviews and delivery outcomes in ignored artifacts and concise verified living docs.

- [x] Agree worker interfaces/ownership and pin deterministic regressions for each mapping row. Record intentional behavioral RED at existing safe seams; test real adapter/selection/persistence behavior rather than source-string assertions or mocks that only echo implementation.
- [x] Implement complete Audio/output/recovery; software Video; Player buffers/frames/timeouts/seek; engine logging/profile; plugin options; general subtitle settings including all eleven existing generation-policy/limit fields, BOM default and both runtime delay editors; and integrate detached Save/application semantics. Keep effective backend limits visible.
- [x] Implement bitmap sidebar preview, middle-click cue action and WPF-compatible JA/ZH overlay segmentation; manual REST text OpenSubtitles search/load/download and the answered Windows/Linux PDIC policy. Preserve existing subtitle/word/export behavior and explicitly record supported service/platform adaptations.
- [x] Implement existing playlist surfaces and online/plugin preferences, including extractor playlists. Preserve explicit external helper admission, cleanup and current stream/caption/chapter/header behavior; title actions and transport previous/next use the proper owners.
- [x] Reconcile every scope-ledger row with actual source and regression evidence; record the precise supported backend/service/layout adaptations. A buildable subset does not close an omitted approved feature.
- [ ] Run focused deterministic GREEN, then unfiltered `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\codex\verify.ps1`; Windows `verify-avalonia.ps1`, `test-package-avalonia.ps1`, `publish-avalonia.ps1`; and Linux `scripts/linux/verify.sh` plus `publish.sh` on Linux/CI. Use actual existing scripts and fixture overrides; retain original failures and investigate before retry. Counts are observations, never required pass totals.
- [ ] Verify staged/extracted Windows/Linux package content, native allowlists/notices and portable plugin loading as applicable. Adding tokenizer libraries or settings models requires honest dependency/package evidence; no packaging claim implies real providers/audio/native GUI acceptance.
- [ ] Run cumulative spawned reviews from `subagent-review-matrix.md`: .NET, runtime, UI, config/instruction, dependency/packaging where relevant, dead-code/reuse and final `verification_reviewer` `/review`. Address confirmed Critical/Important findings and recheck changed behavior.
- [ ] Update matching product/config/media/dependency/manual-smoke descriptions and existing F-13 backlog/status, preserving old receipts. Commit reviewed product and docs within this single coordinated delivery; inspect current GitHub target/open PRs through the GitHub plugin, push one branch/PR, verify required Windows/Linux checks and merge only the reviewed head.
- [ ] Verify actual merged main and its distinct CI, then fresh-publish with `scripts/windows/update-desktop-test-build.ps1` from a clean committed source. Verify receipt `sourceCommit=actual final HEAD`, published/installed bytes, direct EXE/no arguments/payload working directory/blue icon/physical paths and matching `current.json`; preserve earlier payloads/WPF shortcut state. Do not launch the player.
- [ ] Complete only the authorized project-scoped Second Brain living docs/completed session/index/Tests changes with exact allowlist, fresh guarded preflight, independent review, session-close gate and isolated local Vault commit. No Vault push, Dashboard publication or Codex memory write. Record actual outcomes separately; stop after this delivery with full F-13 still in progress and deferred native acceptance explicit.
