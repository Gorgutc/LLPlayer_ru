# F-13 A/V Streams and Chapters — Design and Implementation Plan

**Goal:** One shared Windows/Linux Avalonia UI for embedded/external audio and video choices and chapter seeking.

**Architecture:** Detached typed menu DTOs with adapter-private opaque handles; serialized controller mutations
use portable engine atomic admission and the existing recognition/translation ownership queue.

**Tech Stack:** C#/.NET 10, Avalonia 12.1.3, CommunityToolkit.Mvvm, Flyleaf/FFmpeg and existing xUnit/headless tests.

**Spec:** Owner GO 2026-11-01; base delivered PR #192/main `aa123a288ac7e7757fb5abcb66b2aba604259a8c`.

## Global constraints

- Keep one Windows/Linux UI/Core. Preserve WPF behavior, dependencies, persistence and release packaging.
- Preserve time/play-pause, audio delay, both subtitle caches and translation owners across video variants.
- Bind choices to adapter/epoch/media generation/item/source; validate inside queued work and atomically with native use.
- Cancel and drain owned work during media replacement/shutdown; refuse video replacement while subtitles load.
- Exclude playlist/output-device UI, YoutubeDL preferences, GPU work, live helpers/providers/models and native acceptance.
- Keep runtime files, generated fixtures, logs and receipts under ignored artifacts; retain failed attempts and exact source identities.

## Task 1 — portable engine state preservation

- [x] Add `Player.OpenExternalVideoPreservingState` in `FlyleafLib/Platform/Portable/MediaPlayer/Player.StreamSwitch.cs`.
- [x] Rebind retained audio/subtitle sources without full Initialize; retain selected previous video for return/rollback.
- [x] Preserve native owner identity, cached cues, delays and playback state; deduplicate retained sources on repeated switches.
- [x] Add `TryMutateMedia` admission under the reset/dispose lock; conditional shared hooks compile only for `!WINDOWS`.
- [x] Reject unsupported IOStream variants before mutation with an actionable error and intact previous state.

## Task 2 — controller snapshots and lifecycle

- [x] Extend `IPlaybackController` with `PlaybackStreamTrack`, `PlaybackChapter`, snapshot and asynchronous choice methods.
- [x] Implement `FlyleafPlaybackController.Streams.cs`; snapshot collections through `CollectionSyncBridge`.
- [x] Describe the current demuxer's embedded tracks plus item external alternatives, with metadata/checked state.
- [x] Coalesce `PlaybackChoices` refresh; reject stale/foreign source tokens and clear choices during media changes.
- [x] Serialize synchronous engine calls, cancel active external opens and drain their completion on replacement/shutdown.

## Task 3 — shared menus and deterministic regressions

- [x] Reuse `TrackMenuItem` under Playback and the video context menu: Audio track / Video track / Chapters.
- [x] Preserve automatic defaults; no A/V Off row. Show chapter time/title; refresh on opening and choice notifications.
- [x] Add `AvStreamMenuTests`, `PlaybackChoicesTests`, `AvStreamPlaybackRegressionTests` and offline `AvStreamTestMedia`.
- [x] Preserve the canonical single-stream fixture; assert real selection/decoded output, seek/state, two subtitle owners and stale commands.
- [x] Retain compiled behavioral RED (`red-complete`, corrected menu oracle `red-ui-corrected`) and focused GREEN receipts.
- [x] Close review findings for reset/dispose atomicity, active cancellation, delayed PCM priming, failed-switch rollback/return,
  delayed stale errors, pending-open menus, retainer dedup and owned-interrupt revision recovery preserving later writers; retain RED/GREEN.

## Task 4 — verification and authorized delivery

- [x] Focused recovery GREEN: 60 passed/0 failed/0 skipped (`green-final-recovery.json`, before BOM correction).
- [x] Final full WPF verification/validators: exit 0, 1443 passed/0 failed/0 skipped (`gate-wpf-final.json`).
- [x] Final Windows Avalonia verification: exit 0; engine 1657/0/13, UI 1357/0/1 passed/failed/skipped (`gate-windows-av-final.json`).
- [x] Final Windows package contracts: exit 0, 91 passed (`gate-package-final.json`).
- [x] Verify recovery correction; independent whole-branch and .NET `/review` report zero remaining Critical/Important/Minor.
- [x] Commit reviewed app allowlist as `7063e00e917c1c1fb4cec7c97971a3d0018b0945`; exact source mapping belongs in closeout.
- [ ] Complete fresh Windows publish and package-load checks.
- [ ] Run fresh full Linux verification/publish via CI; local WSL has no installed distro. Inspect failures before fixes/retries.
- [ ] Commit reviewed docs separately; authorized PR/checks/merge and distinct committed-source/actual-main Windows/Linux CI.
- [ ] From clean final main run default fresh Desktop updater; verify sourceCommit, payload bytes, direct EXE/empty args/cwd/blue icon/current.
- [ ] Scoped six-file Second Brain sync, session-close review/gate and isolated local commit; actual results go in ignored `CLOSEOUT.md`.

## Native acceptance checklist — separately agreed, currently NOT_RUN

- [ ] Windows and Linux menus: metadata, selected state, empty lists, keyboard access and source changes while menus are open.
- [ ] Embedded/external audio and video: audible/visible result, seek/play-pause, delays, two subtitle/translation slots and failed switch recovery.
- [ ] Video-switch refusal during subtitle loading, retry, repeated return cycles and encoded uncached PGS; bitmap-owner tests cover ownership only.
- [ ] Live helper/provider/model, physical audio/video, GUI/DPI and GPU checks remain separate; full F-13 stays IN PROGRESS.

Pre-delivery checkpoint: receipts in `artifacts/f13-av-streams-chapters-2026-11-01/` pin base plus `workingTreeFiles` hashes.
Full final gates include recovery/BOM corrections; the later fixture EOF trim is a whitespace-only delta. Counts are observations.
Actual final delivery authority is this stage's ignored `CLOSEOUT.md` and completed Vault session.
