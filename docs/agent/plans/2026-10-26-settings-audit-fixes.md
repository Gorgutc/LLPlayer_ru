# F-13 Settings audit fixes implementation plan

> **For agentic workers:** use `superpowers:subagent-driven-development` to execute the independent tasks below; preserve other workers' edits.

**Goal:** close the confirmed PR #185 numeric-input defect, engine-adapter test isolation gap and delivery/documentation tail under the owner's explicit corrective GO.

**Architecture:** keep raw invalid Translation input available to the existing `TranslationViewModel.SaveCommand` validation, without changing the three integer ranges or persistence rules. Independently guarantee disposal of the adapter test's controller and Player, and test real key dispatch/custom delegates through the existing safe seam.

**Tech stack:** C#/.NET 10, shared Windows/Linux Avalonia 12.1.3, xUnit v3, PowerShell verification.

**Spec:** [config/data contract](../config-data-contract.md), [manual smoke](../manual-smoke-matrix.md), and the pre-fix audit at `artifacts/dsh-two-session-audit-2026-10-26/AUDIT.md` / `verification.json` on `d1f5427ff9a5793b358d6154507af69d2870c6e4`.

## Global constraints

- Approved correction only: no new Settings features, schema/default changes, dependencies, WPF/engine behavior changes or next F-13 block.
- Backward/forward counts remain integers 0–1000; concurrency remains an integer 1–64. Invalid input blocks Save with a readable error and no write/live apply; Close/Esc discards.
- No Computer Use, app/launcher launch, native GUI/DPI/OS-keyboard acceptance or live model/provider/Anki requests. Full parity remains IN PROGRESS.
- Work on `codex/f13-settings-audit-fixes`; separate product/tests from documentation commits. Historical failures and UNKNOWN causes remain visible.

## Review focus

- Fractional `1.5`, concurrency `0` and `65` must survive text input plus blur until Save rejects them.
- Valid integers and spinner changes must still save; invalid text must not leak into live settings or the saved file.
- Close/Esc and failed persistence must preserve the existing detached-draft/retry behavior.
- Controller then Player must be disposed even when an assertion throws; repeated apply must preserve a working custom delegate.
- Key-down/key-up dispatch evidence is headless/engine evidence, separate from native keyboard acceptance.

## Task 1: preserve invalid Translation input (UI worker)

**Files:** `LLPlayer.Avalonia/Views/TranslationWindow.axaml`, the smallest necessary existing/new control seam under `LLPlayer.Avalonia/Controls/`, `LLPlayer.Avalonia.Tests/TranslationDialogTests.cs` and a focused numeric-input regression file if useful. Keep the existing ViewModel validation contract.

- [ ] Add control-boundary tests that set the realized text input, focus/blur, then execute Save. Before production edits, capture compiled RED for forward `1.5`, concurrency `0` and `65`; require a validation failure rather than setup/compile failure.
- [ ] Apply the smallest input fix that retains raw invalid text for Save validation and supports valid integer/spinner edits.
- [ ] Run the same regressions GREEN plus valid integer/spinner, Close/Esc discard and write-failure/retry controls. Assert no persistence callback or live mutation on invalid input.

## Task 2: isolate adapter resources and prove dispatch (test worker; independent of Task 1)

**Files:** `LLPlayer.Avalonia.Tests/EngineKeyBindingAdapterTests.cs` and a focused existing/new test file only if needed; production changes only for a separately reproduced defect.

- [ ] Use guaranteed controller-then-Player teardown, including the construction/assertion failure path; assert Player removal from the engine registry through a safe observable seam.
- [ ] Extend real-adapter coverage from row metadata to key-down/key-up behavior after reapply, and preserve/invoke an engine-owned custom delegate. Keep the existing FFmpeg environment gate and global-engine collection.
- [ ] Execute the env-enabled focused suite and confirm deterministic assertions and cleanup; report skipped execution plainly if native fixtures are unavailable.

## Task 3: reconcile source docs, review and deliver (docs worker + coordinator)

**Files:** this plan, `f13-status-2026-10-18.md`, `f13-next-session.md`, the current F-13 slice of `backlog.md`, and historical receipt clarification in `2026-10-26-settings-parity.md`.

- [ ] Record PR #185 MERGED at `d1f5427`; bind FINAL3 only to `6e73ad8`, with later `e28e76c` test-only xUnit1031 fix and final PR/exact-main CI recorded separately. Preserve last historical delta FAIL verdicts and absence of a later independent final-tree PASS.
- [ ] Replace the obsolete Sidebar/premerge continuation with this already-approved correction and actual remaining boundaries; keep historical checkpoints as links.
- [ ] Run unfiltered WPF `scripts/codex/verify.ps1`, Windows Avalonia `scripts/windows/verify-avalonia.ps1`, package contracts and publish/content verification. Run Linux `scripts/linux/verify.sh` plus publish in CI; local Linux status is reported separately. Totals are observations, never a fixed-count gate.
- [ ] Run explicit spawned `/review` covering the review matrix; address Critical/Important findings, then verify the changed final inputs. Record each gate/review against its actual commit or input hashes.
- [ ] Coordinator commits the reviewed source, publishes through the authorized GitHub flow, then runs default fresh `scripts/windows/update-desktop-test-build.ps1` on the final clean source HEAD. Verify receipt `sourceCommit=HEAD`, payload/manifest and direct-EXE/no-args/working-directory/blue-icon shortcut readback; preserve prior payloads.
- [ ] Coordinator updates the exact project-scoped Second Brain allowlist, runs `session-close`, commits the vault and writes the final handoff. No vault publication is implied.

## Execution receipts

Plan baseline: pre-fix `d1f5427`; existing ordinary suites passed WPF 1440/0, Windows portable engine 1622/13 skipped and UI 979/1 skipped, while the three extra input probes were RED (3 failed, 0 passed). Those are audit facts, not acceptance of corrective code.

Corrective source commit, RED/GREEN, final gates, spawned reviews, PR/main CI, Desktop refresh and vault closeout: **PENDING**. Add only observed receipts; at plan creation Desktop still selects `0811f615` (PR #184), and the living vault still points to that stage.
