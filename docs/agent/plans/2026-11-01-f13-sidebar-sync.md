# F-13 Primary/Secondary Sidebar and Cue Sync Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development. Steps use checkbox syntax for tracking. The owner already approved the concrete Primary/Secondary Sidebar and selected-cue sync block, including the normal reviewed delivery lifecycle; do not ask for another generic plan/execution approval.

**Goal:** Let the shared Windows/Linux Avalonia sidebar show either subtitle slot and align a chosen cue to the current playhead by changing only that slot's runtime subtitle delay.

**Architecture:** Keep one sidebar and one existing cue/search pipeline. A persisted selector chooses its slot, while the playback adapter owns live cue identity and validates row commands against the current media, subtitle track and cue before applying them. Reuse the engine's subtitle seek and delay/resync paths; WPF remains supported and unchanged.

**Tech Stack:** C#/.NET 10, Avalonia 12.1.3, CommunityToolkit.Mvvm, existing Flyleaf/Core, xUnit and Avalonia headless tests. No new dependency, native asset or packaging format.

**Spec:** The accepted design in this document records the owner-approved stage. Starting base: actual main `817a02b5defa2f073cbf555911de415752373c17` after PR #193. Working branch: `codex/f13-av-sidebar-sync` in the existing isolated worktree. Source/GitHub/CI/Desktop/Vault claims for this new stage require their own fresh receipts.

**App/local checkpoint:** reviewed app commit `c04dd101551ea4ed75cbba94184d7020a5dd38f5`, parent `817a02b5`,
contains exactly 16 product/test files. Final local Windows gates/packages/plugin loads passed on the recorded
working-source snapshots. Their receipts identify the base plus file hashes; `product-source-proof-v2.json` separately
passed all16 raw committed/workspace/manifest-v2/WinAV-v3 comparisons, with no normalization/BOM/EOF exceptions.
App tree `4099b16701e98ae1a9eca5cf4c4074cf54488ca2`; proof JSON SHA-256
`0b3150f9581757b2021584279f775d8d88a6f1690a6eb801dba00f952108162e`. It explicitly records dirty DOCS7 and
`workingTreeCleanAtProof=false`; final docs/CI/Desktop results are not inferred from app byte proof.

## Accepted Design

### Sidebar selection

- Add a visible, keyboard-accessible Primary/Secondary selector. Primary is the absent-preference default, preserving the current startup choice. Persist the user's selection through the existing preferences owner.
- Selection chooses the sidebar's source slot only. It does not assign or disable a track, toggle translation, change subtitle overlay visibility or switch media.
- List contents, hit count, literal/case/whole-word/regex search, original/display choice, spoiler mask, current highlight, idle auto-scroll, per-cue seek and next/previous-match navigation all follow the selected slot.
- Preserve the current trimmed ordinal literal matching, Unicode whole-word behavior, invalid-regex/first-timeout error with zero results and recovery, wrap navigation, Enter/Shift+Enter and Escape/focus semantics. Search reads the chosen original/display source through masks; masks never become provider/library input.
- Word selection and menus carry the chosen row's slot, displayed text, translated/source languages and immutable selection provenance. Existing overlay and popup ownership stays intact.
- Selecting an empty or disabled slot shows an empty sidebar and clears stale current/search-navigation state. Do not silently switch back to the other slot. Existing translation/ASR append updates refresh the selected list without needlessly rebuilding stable rows.
- A preserving external-video switch may retain cue objects/text while replacing their command source. Its settled success/rollback completion refreshes both slots through existing coalesced notifications, including translation-off/no-cache sessions, so fresh rows are actionable and old identities stay rejected.

### Selected-cue sync

- Expose an explicit per-row sync-to-current-time action, separate from seek and word selection. Its tooltip and accessible name state that it changes subtitle timing.
- Match WPF `SubtitlesSidebarVM.CmdSubSync`: `newDelayTicks = currentPlayheadTicks - rawCueStartTicks`, then assign only the chosen slot's `Config.Subtitles[slot].Delay` through the adapter. `GetCues` currently returns raw `SubtitleData.StartTime`/`EndTime`; never subtract the existing delay again.
- Delay remains runtime-only. The engine's existing `SubConfig.Delay` is `[JsonIgnore]`, refreshes that slot's current cue, resynchronizes its subtitle stream and resets when a new subtitle stream opens. Do not add a delay field to `AppPrefs` or write subtitle files.
- Sync does not seek, start/pause playback, change speed, modify the other slot's delay, reassign tracks or alter translation configuration. Ordinary playback progression while playing remains possible; the action introduces no explicit playhead move.
- A row command must still refer to the live media, selected slot, subtitle track and cue when it executes. Reject rows from replaced media, same-URL reopen, track replacement/off, list replacement/clear or a previous sidebar selection. An equal index or equal timestamps alone is insufficient identity.
- Cancel the row generation's queued sync when selection/source rebuild, Clear, filter removal or Dispose retires its ownership. Recheck that cancellation at worker entry, within the live-source predicate and immediately before mutation; UI admission alone does not establish execution-time ownership.
- Validated seek keeps the existing `SeekAccurate(rawStart, slot)` path, including that slot's delay and fractional-timestamp rounding. Sync button input must not bubble into row seek.
- A pointer or Space-key press on Sync owns its original row through release. Recycling/rebinding that button to another otherwise-live row between press and release must not synchronize the new row.

### Boundaries and preserved contracts

- One shared Avalonia implementation serves Windows and Linux. Preserve WPF source and behavior, dual-overlay rendering, engine dispatcher/collection ownership, sorted raw cues, translation lifecycle and `SubtitlesSelectedHelper`.
- Keep existing typography, sidebar dock/width, virtualization and pointer-idle auto-scroll rules. The control must remain usable at the existing 220 DIP minimum sidebar width and in dark/light themes.
- The new persisted selection is owned independently of a detached Settings draft. Saving an already-open Settings window must preserve a later toolbar selection and all unrelated live preferences. Failed persistence must retain the existing error/transaction policy without partially applying unrelated values.
- Bitmap sidebar redesign, middle-click expansion, CJK/PDIC, export changes, new subtitle-track management, OCR/ASR/provider/model acceptance, new shortcuts and broader F-13 parity are excluded. Existing bitmap overlay/layout and both-slot export behavior remain unchanged.
- No new tool install, automatic dependency download, GUI/player launch or native acceptance is inferred from this stage. Native Windows/Linux GUI, physical A/V, real providers/models/GPU/DPI acceptance remains separately owner-selected.

## Global Constraints

- Work on `codex/*`; root owns Git/index/commits and exclusive build scheduling. Workers are not alone and preserve each other's changes; no concurrent dotnet builds.
- Respect `AGENTS.md`, product/config/runtime/WPF contracts and cumulative `subagent-review-matrix.md`. The direct owner GO authorizes this bounded additive contract change.
- Keep application changes separate from agent/tooling/contract updates in reviewed commits. Preserve required C#/XAML UTF-8 BOM and intentional line endings.
- Runtime JSON, downloaded tools, models, test logs, packages and generated native assets stay out of source Git.
- Record actual commands, commit/tree identity, exit status and logs. Passing-test counts are observations, never thresholds. Preserve first failures and diagnose before rerunning.
- Native/headless/library evidence and product delivery are separate; do not turn historical PR #193 receipts into this stage's acceptance.

## Review Focus

1. Same-URL media reopen, same-index track replacement and delayed row input must reject stale identity without touching either slot.
2. Positive/negative existing delay and fractional cue timing must use raw start exactly once; sync changes only the chosen runtime delay and seek still adds the matching delay.
3. Secondary current-index and cue-change events must drive highlight, mask, navigation and auto-scroll independently of primary updates.
4. Word click/drag/menu and sync-button pointer/keyboard input must preserve slot provenance and never trigger an unintended seek or hidden-text lookup.
5. A Settings draft opened before a toolbar selection must not overwrite that newer selection; restart and legacy preferences must retain the intended primary-default policy.

## Task 1 — Adapter slot state and live cue commands

**Own:** `LLPlayer.Avalonia/Services/IPlaybackController.cs`, `FlyleafPlaybackController.cs` or a focused sidebar partial, and new adapter regression tests. The existing `FlyleafPlaybackController.Streams.cs` external-video completion hook also refreshes both cue slots; preserve its A/V stream/chapter behavior and native owners.

**Interfaces:** Extend `CueInfo` with trailing optional `SubtitleCueIdentity? Identity = null`; retain `GetCues(int slot)` and timestamp `SeekToSubtitle` compatibility. Add `SecondaryCueIndex`, `bool IsCueCurrent(CueInfo cue, int slot)`, `void SeekToCue(CueInfo cue, int slot)` and `Task SynchronizeSubtitleToCurrentTimeAsync(CueInfo cue, int slot, CancellationToken cancellationToken = default)`. The adapter is the only layer that reads/writes live engine subtitle delay.

- [x] Retain compiled stale-row RED and add both-slot current-cue/sync, delay/fractional timing, other-slot/playhead/play-state and stale identity regressions. Independent runtime review also reproduced translation/opposite-bitmap RED before fixes.
- [x] Implement live command identity and slot validation without changing raw `CueInfo.Start`/`End`, decoder locks, engine source, WPF or export semantics.
- [x] Route sync through the existing selected `SubConfig.Delay` setter; route validated seek through `Player.SeekAccurate(TimeSpan, slot)`. Use the queue without resetting either translator; scope/restore the selected helper around resync.
- [x] Prove disabled/empty/invalid slot and same-URL/same-index replacement fail safely; prove ordinary translation/append updates retain valid live identity as intended, and canceled/rejected work preserves cached translations.
- [x] Cover preserving external-video switch/rollback row admission with six live MainWindow cases after four compiled stale-row RED failures. Compare every original visible/raw snapshot field excluding only opaque identity; separately assert fresh/stale identities, native owners, bitmap and delays.
- [x] Run expanded focused GREEN under the root's exclusive build schedule: `focused-av-integration-green` records 228/0/0, exit 0, including A/V integration and input fixes. Preserve earlier 149/158/161 checkpoints and all original failures; full gates and final source identity remain separate.

## Task 2 — Selected-slot VM and persistence

**Own:** `LLPlayer.Avalonia/ViewModels/SubtitlesSidebarViewModel.cs`, `Services/AppPrefs.cs`, focused MainWindow VM integration and relevant VM/prefs/settings tests. Coordinate shared MainWindow files with the UI worker.

**Interfaces:** Consume Task 1's validated cue commands/current indices. Add `AppPrefs.SidebarShowSecondary` (bool, default false), sidebar `SelectedSlot` (int, validated 0/1), immutable `CueItemViewModel.Slot`, per-row async `SyncCommand`, and `bool IsCurrentRow(CueItemViewModel row)`. Retain `SeekCommand`, filter, appearance and navigation APIs. The preference owner stores the selector independently of unrelated Settings draft fields.

- [x] Add secondary-list/search/original/mask/current/navigation, default/restart, same-index switch and Settings-draft concurrency regressions; preserve the compiled stale-row behavioral RED.
- [x] Generalize list reload/current notifications/seek/match navigation to the selected slot; reset stale row/navigation state on slot/media identity change and preserve stable rows on compatible updates.
- [x] Carry the row's slot/provenance into word actions; expose sync eligibility/command without using filtered-list positions as engine cue indices. Cancel retired-row generation tokens so queued old selection work cannot mutate delay.
- [x] Add primary-default preference cloning/normalization/persistence; preserve a toolbar selection changed after Settings draft creation and unrelated current values. Keep the existing live-selection/save-error policy on toolbar persistence failure.
- [x] Run focused GREEN, including existing search recovery, empty selected slot, selected/unselected event isolation, cancellation and failed-save policy; current expanded controlled evidence is `focused-av-integration-green` (228/0/0).

## Task 3 — Shared UI selector and explicit sync input

**Own:** `LLPlayer.Avalonia/Views/MainWindow.axaml[.cs]`, coordinated VM-facing bindings and focused headless control/input tests. Preserve existing video/subtitle/word popup routing.

**Interfaces:** Bind `SidebarSlot` (ComboBox, Primary/Secondary text, `SelectedIndex` bound to `Sidebar.SelectedSlot`) and per-row `CueSyncButton` (`Sidebar.SyncCommand`, row command parameter). Replace the sidebar row's hard-coded `SubtitleText.Slot="0"` with `Slot="{Binding Slot}"`. Keep overlay slot bindings independent.

- [x] Retain compiled UI RED and add actual headless selector/word-provenance/button routing regressions, including normal selected-slot timestamp seek and stale/retired word input.
- [x] Add the compact selector and per-row sync button with accessible names/tooltips; prevent button events from invoking `OnCueTapped` and preserve drag/control ownership.
- [x] Retain virtualized list/idle auto-scroll, realized-row appearance, focus and empty-state behavior; headless checks cover 220 DIP sidebar width, dark/light theme and keyboard reachability.
- [x] Obtain independent UI re-review confirming closure of the press-owner/release-cleanup findings: `ui-review.md` reports zero remaining Critical/Important on the reviewed UI source. Sealed 161/0/0 and expanded 228/0/0 include its input regressions; original RED and intermediate results remain preserved. The later chord diagnostic correction changes no product UI. Native GUI/DPI acceptance remains `NOT_RUN`; final whole-branch review is separate.

## Task 4 — Contracts, reviews and full gates

**Own root/docs worker after implementation:** this plan, matching current product/config/runtime contract addenda, `manual-smoke-matrix.md`, current F-13 status/backlog/continuation. WPF contract changes only if an explanatory additive note is necessary; its shipped behavior stays frozen.

- [x] Update the active primary-only sidebar statements with this additive slot selector/sync contract while keeping dated historical receipts intact. Document preference default/ownership, raw timing/runtime reset, stale command admission and explicit exclusions.
- [x] Map each design requirement and Review Focus item to a deterministic regression or a precisely scoped manual scenario. Keep `PASS`, `NOT_RUN`, `UNKNOWN` and deferred boundaries distinct; the mapping below does not claim native execution.
- [x] Spawn cumulative app domain reviews and fix findings: runtime/UI/.NET/whole-app approvals report zero remaining Critical/Important on the final 16-file app scope. Independent package comparison also reports zero C/I. Preserve original failures and review responses.
- [x] Freeze the 16-file app source manifest and commit it separately as `c04dd101551ea4ed75cbba94184d7020a5dd38f5` under root ownership.
- [x] Verify actual `product-source-proof-v2.json` PASS for all16 exact raw app files and its tree/JSON hash; independent raw `cat-file` readback also matched. Preserve the initial helper failure in the ignored diagnostic summary without changing source/gate bytes.
- [ ] Obtain final seven-doc instruction/infra/whole-branch readback, run fresh `scripts/codex/verify-fast.ps1`, and create a separate scoped docs commit.
- [x] Run final local full WPF verification, Windows Avalonia verification, Windows package contracts, fresh publish and staged/extracted plugin loads. Actual receipts/results are below; execution uses base plus frozen working-file hashes and is not claimed as a future committed docs-HEAD run.
- [ ] Run full `scripts/linux/verify.sh` and `scripts/linux/publish.sh` on actual Linux, normally final-head CI from this Windows host. Preserve WPF compatibility checks and package/load tests; inspect any failure before deciding a rerun or repair.

## Task 5 — Reviewed delivery and scoped Vault closeout

- [ ] Fresh GitHub readback: default/base/head, all other open PRs, mergeability, changed-file overlap and required checks. Push the feature branch and open the normal authorized PR.
- [ ] Inspect complete Windows/Linux checks at the expected final PR head, archive/package identities and actual failures/skips. Run a fresh independent final `/review` and close Critical/Important before merging that exact head.
- [ ] Verify merged actual main and separate main push CI with exact checkout/head identities; do not infer main acceptance from a synthetic PR checkout.
- [ ] On clean final main, run default fresh `scripts/windows/update-desktop-test-build.ps1`. Verify `sourceCommit`, staging/ZIP/extracted/installed receipt consistency, physical immutable payload/current link, direct EXE/no arguments/payload cwd/blue icon and preservation of previous builds/WPF shortcut. Do not launch the player.
- [ ] Read fresh Vault preimages and concurrency state; apply the root-approved exact LLPlayer project allowlist only, review its diff, run scoped session-close and create an isolated local Vault commit. No Vault push or Dashboard publication is inferred.
- [ ] Independent delivery readback: source/PR/main CI, Desktop receipt and scoped Vault evidence. Report this block's actual result and retained native/F-13 boundaries, then stop.

## Interface Coordination

The adapter, VM and UI workers agreed the names recorded in Tasks 1–3. `SubtitleCueIdentity` is a public sealed opaque handle with adapter-owned construction/payload and stable equality. Cue command validation covers owner, media epoch/generation, playlist item/video source, slot/native subtitle source identity and URL, cue collection/reference and raw start/end. A null legacy identity must not authorize mutation of a real engine cue.

`IsCurrentRow` requires active displayed-row reference membership, the selected slot and current adapter source identity. A selector change retains the shared query/options/appearance, resets navigation/current state and rebuilds rows; compatible translation/append notifications retain stable rows. VM-owned generation cancellation retires queued sync when a row is no longer owned. The UI uses direct command binding so async command admission/disable behavior stays VM-owned. `OnCueTapped` excludes button descendants, and word handlers reject stale press/release provenance before pausing or starting lookup. Auto-scroll requires the current row to remain in the displayed list.

The Sync-button input fix retains direct command binding and adds a Click admission guard over the original
pointer/Space press's button/row/identity/raw timing snapshot. A stale Click is handled before the native command
runs; matched Space release reaches the button, while unmatched release is consumed. No-input automation validates
the live row. The sealed fix retains press snapshots through every Click, rejected automation and OS key repeat;
only the actual matching release retires its own snapshot through reference-checked posted cleanup. This preserves
held input ownership while allowing a later legitimate automation/fresh key after canceled outside release.
Sealed focused coverage includes rebind/selection/source changes, compatible translation, automation, canceled
release, mixed held-pointer/rejected-click/release, Enter repeat and transferred focus. `ui-review.md` now records
independent closure with zero remaining Critical/Important for that reviewed UI source, separate from full gates.

The engine delay setter calls `SetCurrentTime` and `ReSync` synchronously. No subtitle collection gate is held across that setter. The fixed implementation uses `Recognition.MutateAsync` rather than the translation-reset wrapper, scopes `SubtitlesSelectedHelper.CurIndex` to the selected slot only during the setter and restores its previous value in `finally`. Independent runtime re-review closed both Important findings with compiled RED/GREEN and checked source hashes. The selected bitmap keeps the existing clear/resync semantics; no bitmap restoration path is added.

The existing Player action gate protects media reset/stop/dispose; arbitrary native/custom `Open(StreamBase)` callers do not universally use it. Supported app routes and queued replacement have deterministic coverage, while universal atomicity with simultaneous arbitrary native opens is not claimed. Native text/bitmap timing and physical playback acceptance remain separate.

## Requirement Coverage

The first `full-win-av-sealed` gate failed: engine 1657/0/13 passed, UI 1408/12/1 failed on old whole-DTO equality
expectations that included opaque command identity. All original visible/raw fields remain compared; fresh/stale
identity/native ownership is now explicit. Investigation also reproduced four real MainWindow stale-row failures
after preserving video switch with translation off. The existing completion hook now queues both slot refreshes
after settled success/rollback; six live cases cover fresh row admission. The expanded 228/0/0 GREEN and updated
runtime re-review apply to this larger source manifest; earlier 149/158/161 and WPF runs do not prove its full gates.
The separate chord diagnostic expectation was corrected from installed Avalonia framework behavior with no product
UI change. No initial failure is waived, erased or labeled flaky.

A subsequent full run observed UI1426/1/1 from a bitmap fixture's unawaited native seek. A no-sync diagnostic passed
1/0/0 and proved the fixture could queue/clear bitmap state before sync; awaiting exact SeekCompleted plus UI flush
before seeding resolved the fixture, settled bitmap regressions passed 3/0/0, and product source was unchanged.
`full-win-av-final-v3` then passed engine1657/0/13 and UI1428/0/1, exit 0. The current 16-file product/test source is
recorded by `final-product-manifest-v2.json`; prior full failures and all diagnostics remain retained. This local
gate alone did not establish the subsequent WPF/package results or Linux/CI, delivery and native acceptance.

The subsequent final local WPF run passed 1443/0/0; package contracts passed 91, fresh publish produced a 481-file
ZIP with independent staging/ZIP/extracted hash agreement, and both actual packaged plugin loads passed 1/0/0.
This completes the local Windows gates, with app source committed separately as above. Final docs validation,
Linux/remote CI, Desktop/Vault delivery and native acceptance remain separate from the passed raw
product-to-commit proof.

| Accepted behavior / review risk | Deterministic regression or separate manual scope |
| --- | --- |
| Slot list/current/search/original/mask/navigation/empty-slot and selected events | `SidebarSlotTests`, existing `SubtitleSearchTests` and `SubtitlePlaybackRegressionTests` |
| Legacy Primary default, restart, toolbar save failure and fresh Settings merge | `SidebarSlotPrefsTests` |
| Live media/track/cue identity, same-URL reopen, selected generation cancellation and append retention | `SidebarPlaybackAdapterTests`, `SidebarSlotTests` retirement/append cases |
| Preserving external-video success/rollback without translation notifications | Six `AvStreamPlaybackRegressionTests` MainWindow/controller cases, four compiled pre-fix RED; original metadata plus explicit identity/owner/delay assertions |
| Raw fractional timing, existing positive/negative delay, other state/translation/opposite bitmap preservation | `SidebarPlaybackAdapterTests`; `focused-translation-red` and `red-opposite-bitmap` retain pre-fix failures |
| Actual selector/button/word routing, recycled row provenance, 220 DIP/dark/light/keyboard | `SidebarSlotUiTests`; press-rebind/cleanup/mixed-click/Enter-repeat covered by 161 then expanded 228/0/0; independent UI review closed, full gates separate |
| Native paused/playing text/bitmap timing, physical input/A/V/provider/model/GPU/DPI | Dedicated 2026-11-01 rows in `manual-smoke-matrix.md`, all `NOT_RUN` |

## Evidence Ledger

| Gate or action | Current stage status | Evidence |
| --- | --- | --- |
| Owner scope and execution authorization | APPROVED | Parent task records owner GO for this concrete block and full scoped delivery; native acceptance separate |
| Starting source | VERIFIED READ-ONLY | Clean branch created from main `817a02b5defa2f073cbf555911de415752373c17`; prior PR #193 receipts are historical |
| Design/implementation app source | COMMITTED / APP REVIEWS APPROVED | `c04dd101551ea4ed75cbba94184d7020a5dd38f5`, parent817a02b5, exactly16 files; runtime/UI/.NET/whole-app zero C/I; final docs/delivery readback separate |
| Raw product-to-commit binding proof | PASS | `product-source-proof-v2.json`, JSON SHA-256 `0b3150f9581757b2021584279f775d8d88a6f1690a6eb801dba00f952108162e`: all16 raw committed/workspace/manifest-v2/WinAV-v3 bytes match, tree4099b16701e98ae1a9eca5cf4c4074cf54488ca2; no normalization/BOM/EOF exceptions; dirty DOCS7/false full-tree cleanliness explicitly recorded |
| Compiled behavioral RED | OBSERVED | Initial stale-row/UI/translation/opposite-bitmap failures, `sync-press-rebind-red`, `red-canceled-press-cleanup` and subsequent mixed-click failure receipts remain preserved; no blind retry/failure erasure |
| Focused GREEN/regressions | PASS ON EXPANDED INTEGRATION SNAPSHOT | `focused-av-integration-green.{json,log}`: exit 0, 228/0/0; base `817a02b5` plus expanded `workingTreeFiles`, log SHA-256 `25cb8f816590b28f6b8060eaecac0c24a5cb7243f9ce4f0188a7b19fad374d60`; preserves 149/158/161 checkpoints separately |
| Independent source/domain `/review` | APP APPROVED; FINAL DOCS READBACK PENDING | Runtime/UI/.NET/whole-app reports zero C/I for final APP16; package comparison PASS/zero C/I; initial scoped docs review found no findings, final docs checkpoint still requires readback |
| Fast infrastructure/docs gate | PRIOR VALIDATORS PASS; FINAL DOCS GATE NOT_RUN | Full WPF receipt contains validator completion before this docs addendum; final docs validators require a fresh run |
| Full WPF gate | PASS ON FINAL LOCAL APP SNAPSHOT | `full-wpf-final.{json,log}`: exit0,1443/0/0, log SHA-256 `5f3912336d2095003bf2cd189c539f2846bd057a8d1b915213ae790ea6605b37`; earlier full-wpf/sealed checkpoints preserved, no future docs-HEAD run claimed |
| First full Windows Avalonia gate | FAILED / RETAINED | `full-win-av-sealed.{json,log}`: exit 1, engine1657/0/13, UI1408/12/1, diagnosed DTO equality failures; real stale-row follow-up separately reproduced/fixed |
| Subsequent Windows Avalonia failure | FAILED / RETAINED AND DIAGNOSED | `full-win-av-final`: exit 1, UI1426/1/1; fixture seek ordering corrected after real no-sync diagnostic, product source unchanged |
| Current Windows Avalonia full verification | PASS ON CURRENT LOCAL SOURCE | `full-win-av-final-v3.{json,log}`: exit 0, engine1657/0/13, UI1428/0/1, log SHA-256 `4d7d7ae3181a98003cba6c1a88372f1fde066859838ee81eebe4346ad2d9d8da`; current16-file manifest v2, not future commit/CI/native acceptance |
| Windows package contracts | PASS | `package-contract-final`: exit0,91 passed; log SHA-256 `106856644ba8c1af698ed537438dd148ab194a0c24e519b765fa72ab068c4c8a` |
| Fresh local publish / independent package review | PASS ON LOCAL WORKING-SOURCE SNAPSHOT | `publish-local-final`, `package-review.md/.json`: all481 staged/ZIP/extracted names/sizes/hashes match; ZIP180634345bytes SHA-256 `744590fb2359bfcfd8374a1b8dab535024edeabb6d8138a8f24c1852deeba018`; zero C/I, not a final-main Desktop payload |
| Actual packaged plugin loads | PASS | `plugin-load-staging` and `plugin-load-extracted`: each exit0,1/0/0; actual portable plugin loaded from the corresponding payload |
| Linux full verify/publish/load | NOT_RUN | Await actual Linux final-head CI evidence |
| Feature PR and exact-head CI | NOT_RUN | Await fresh GitHub readback |
| Merge and actual-main CI | NOT_RUN | Await exact merged-head readback |
| Fresh Desktop payload/blue-shortcut verification | NOT_RUN | Await final-main fresh publish and physical readback; no launch authorized here |
| Scoped Vault session-close/local commit | NOT_RUN | Await fresh exact allowlist, preimages, diff review and real gate/commit |
| Native Windows/Linux GUI, physical A/V, provider/model/GPU/DPI acceptance | NOT_RUN / SEPARATE OWNER GATE | Automated/headless/package results do not establish native acceptance |
| Full F-13 parity | IN PROGRESS | This bounded block cannot close the whole migration |

Ledger updates must cite actual source identity and evidence. Checkboxes and a worker's report alone do not convert a row to `PASS`.
