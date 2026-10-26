# F-13 Settings audit fixes implementation plan

> **For agentic workers:** use `superpowers:subagent-driven-development` to execute the independent tasks below; preserve other workers' edits.

**Goal:** close the confirmed PR #185 numeric-input defect, engine-adapter test isolation gap and delivery/documentation tail under the owner's explicit corrective GO.

**Architecture:** keep raw invalid Translation input available to the existing `TranslationViewModel.SaveCommand` validation, without changing the three integer ranges or persistence rules. Independently guarantee disposal of the adapter test's controller and Player, and test real key dispatch/custom delegates through the existing safe seam.

**Tech stack:** C#/.NET 10, shared Windows/Linux Avalonia 12.1.3, xUnit v3, PowerShell verification.

**Spec:** [config/data contract](../config-data-contract.md), [manual smoke](../manual-smoke-matrix.md), and the pre-fix audit at `artifacts/dsh-two-session-audit-2026-10-26/AUDIT.md` / `verification.json` on `d1f5427ff9a5793b358d6154507af69d2870c6e4`.

## Global constraints

- Approved correction only: no new Settings features, schema/default changes, dependencies, WPF/engine behavior changes or next F-13 block.
- Backward/forward counts remain integers 0–1000; concurrency remains an integer 1–64. Invalid input blocks Save with a readable error and no write/live apply; Close/Esc discards.
- The prior no-CUA/no-launch restriction was honored through the local code/gate phase. The owner's later explicit GO authorized the bounded native Windows audit, now recorded against candidate `fcf4b49`; open cells/mock/seed limits remain explicit. Linux/model/provider/Anki/GPU/audible-output acceptance remains separate. Full parity remains IN PROGRESS.
- Work on `codex/f13-settings-audit-fixes`; separate product/tests from documentation commits. Historical failures and UNKNOWN causes remain visible.

## Review focus

- Fractional `1.5`, concurrency `0` and `65` must survive text input plus blur until Save rejects them.
- Valid integers and spinner changes must still save; invalid text must not leak into live settings or the saved file.
- Close/Esc and failed persistence must preserve the existing detached-draft/retry behavior.
- Controller then Player must be disposed even when an assertion throws; repeated apply must preserve a working custom delegate.
- Key-down/key-up dispatch evidence is headless/engine evidence, separate from native keyboard acceptance.

## Task 1: preserve invalid Translation input (UI worker)

**Files:** `LLPlayer.Avalonia/Views/TranslationWindow.axaml`, `LLPlayer.Avalonia/Controls/IntegerNumericUpDown.cs` and `LLPlayer.Avalonia.Tests/TranslationNumericInputTests.cs`; existing `TranslationDialogTests.cs` is a verification input, not an implementation change. Keep the existing ViewModel validation contract.

- [x] Add control-boundary tests that set the realized text input, focus/blur, then execute Save. Before production edits, capture compiled RED for forward `1.5`, concurrency `0` and `65`; require a validation failure rather than setup/compile failure.
- [x] Apply the smallest input fix that retains raw invalid text for Save validation and supports valid integer/spinner edits.
- [x] Run the same regressions GREEN plus valid integer/spinner, Close/Esc discard and write-failure/retry controls. Assert no persistence callback or live mutation on invalid input.

## Task 2: isolate adapter resources and prove dispatch (test worker; independent of Task 1)

**Files:** `LLPlayer.Avalonia.Tests/EngineKeyBindingAdapterTests.cs` and a focused existing/new test file only if needed; production changes only for a separately reproduced defect.

- [x] Use guaranteed controller-then-Player teardown, including the construction/assertion failure path; assert Player removal from the engine registry through a safe observable seam.
- [x] Extend real-adapter coverage from row metadata to key-down/key-up behavior after reapply, and preserve/invoke an engine-owned custom delegate. Keep the existing FFmpeg environment gate and global-engine collection.
- [x] Execute the env-enabled focused suite and confirm deterministic assertions and cleanup; separately verify all six tests skip without native fixtures, which is not dispatch acceptance.

## Task 3: reconcile source docs, review and deliver (docs worker + coordinator)

**Files:** this plan, `f13-status-2026-10-18.md`, `f13-next-session.md`, the current F-13 slice of `backlog.md`, historical receipt clarification in `2026-10-26-settings-parity.md`, `f13-native-visual-audit-2026-10-26.md` and a dated bounded-receipt appendix in `manual-smoke-matrix.md`.

- [x] Record PR #185 MERGED at `d1f5427`; bind FINAL3 only to `6e73ad8`, with later `e28e76c` test-only xUnit1031 fix and final PR/exact-main CI recorded separately. Preserve last historical delta FAIL verdicts and absence of a later independent final-tree PASS.
- [x] Replace the obsolete Sidebar/premerge continuation with this already-approved correction and actual remaining boundaries; keep historical checkpoints as links.
- [x] Run unfiltered WPF `scripts/codex/verify.ps1`, Windows Avalonia `scripts/windows/verify-avalonia.ps1` and package contracts on clean committed `116a4ba`. Retain Windows attempt-1 FAIL and the one unchanged full diagnostic attempt-2 PASS; root cause UNKNOWN. Totals are observations, never a fixed-count gate.
- [x] Run explicit spawned source `/review` covering the review matrix; address Critical/Important findings, fix the docs anchor Minor and verify the two-file BOM-only normalization. Record reviews against their actual source inputs.
- [x] Commit product/tests (`b36655d`), BOM-only normalization (`0e4635a`) and initial source docs (`116a4ba`) separately.
- [x] Review/commit candidate docs as `fcf4b4912c23f080f4db1a5a005e669d6372a566`, fresh-publish/install with sourceCommit readback and perform the authorized bounded Windows GUI/CUA audit. Native case matrix preserves mock/seed/open cells; candidate evidence does not automatically accept a later final payload.
- [ ] Review/commit this later docs receipt delta, then publish through the authorized GitHub flow. Require actual corrective PR/main Windows/Linux CI, Linux full `scripts/linux/verify.sh`, and publish/content verification; local Linux remains NOT_RUN on this host.
- [ ] Run default fresh `scripts/windows/update-desktop-test-build.ps1` on the final clean source HEAD. Verify receipt `sourceCommit=HEAD`, payload/manifest and direct-EXE/no-args/working-directory/blue-icon shortcut readback; preserve prior payloads.
- [ ] Coordinator updates the exact project-scoped Second Brain allowlist, runs `session-close`, commits the vault and writes the final handoff. No vault publication is implied.

## Execution receipts

Plan baseline: pre-fix `d1f5427`; existing ordinary suites passed WPF 1440/0, Windows portable engine 1622/13 skipped and UI 979/1 skipped, while the three extra input probes were RED (3 failed, 0 passed). Those are audit facts, not acceptance of corrective code.

**Code-stage checkpoint, implemented/locally verified:** product `b36655d40a75f8724c01d79ed745926415faed18`, BOM-only `0e4635a38b90e1f0b6fc2564e7fd6918c0a682fd`, full gates on clean `116a4baa7acb49aca7480634df8b6c998cc2b1aa`. Numeric RED: 24 failed/12 passed/0 skipped (36 total); adapter-lifecycle RED: 2 failed/0 passed. Expanded focused GREEN2: 60 passed/0 skipped (47 numeric + 6 adapter + 7 existing dialog cases), exit 0. No-native adapter run: 6 skipped/0 failed, exit 0, separate from dispatch evidence.

WPF: 1440 passed/0 skipped, exit 0. Windows Avalonia attempt 1: FAIL, engine 1621 passed/1 failed/13 skipped, downstream UI NOT_RUN; new OpenAL child max desync 120.3333 ms over 100 ms. Independent runtime/verification triage found no engine/Core/engine-test/dependency/fixture changes and excluded the old slow-read sampler gap; cause UNKNOWN. Exactly one unchanged full diagnostic attempt 2: PASS, engine 1622/13 skipped and UI 1031/1 skipped, exit 0. Separate unfiltered UI: 1031/1 skipped, exit 0. Offline package: 53 fixtures/38-file ZIP, exit 0. This is not an audio fix, flaky-test diagnosis or host-cause finding. Full log identities and source hashes are in [F-13 status](../f13-status-2026-10-18.md).

Independent .NET/UI/runtime/verification and docs instruction/architecture reviews report 0 Critical/Important; the historical-anchor Minor is fixed. Review later documentation deltas separately before commit. At this code-stage checkpoint corrective publication/CI, fresh publish/Desktop and vault closeout are NOT_RUN; for later completion, resolve actual GitHub main/candidate, Desktop sourceCommit/readback and the live vault handoff instead of repeating completed implementation. At the pre-fix audit Desktop/vault were on `0811f615` (PR #184); that observation does not establish their later state.

After the local code/gate phase, fresh-published candidate `fcf4b49` was exercised under the new direct Windows visual GO. [Native audit](../f13-native-visual-audit-2026-10-26.md) records the observed case-level results, 29 JPEG-encoded screenshots with legacy `.png` names, separate three-note export/four-word final-profile snapshots and owned mock/window cleanup. Native Enabled/reset/live-sheet, numeric UIA Save and the bounded product paths passed; capture/rebind/collision/held phase, ComboBox choice, minimum-size/high-DPI/custom path and other listed cells remain unaccepted. Independent visual review: 0 Critical/Important, P3 Light-sidebar shadow deferred to a separate bounded styling proposal. Linux/real providers/models/Anki/GPU/audible-output are not accepted. Corrective PR/main CI, final-main Desktop and vault closeout remain NOT_RUN at this candidate checkpoint; resolve later actual receipts before repeating work.
