# Windows CI test isolation repair implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: use `superpowers:subagent-driven-development` for the disjoint test tasks below. Preserve other workers' edits; the coordinator owns all test commands and Git operations.

**Goal:** repair the test-barrier and Avalonia UI-service isolation weaknesses identified during triage of the two open Windows CI findings, then verify the changed tree on Windows and Linux.

**Architecture:** change test code only. Recognition settings tests await worker entry asynchronously and always release/drain accepted work. Tests that actually access Avalonia UI services use the existing headless session/dispatcher; pure tests retain their current independent execution.

**Tech Stack:** C#/.NET 10, xUnit v3, Avalonia 12.1.3 headless tests, existing PowerShell and Linux verification scripts.

**Spec:** the owner's explicit approval on 2026-10-26, [verification policy](../verification.md), [review matrix](../subagent-review-matrix.md), and [config/data contract](../config-data-contract.md). The earlier local proposal `artifacts/dsh-two-session-audit-2026-10-26/next-windows-ci-repair-block.md` is historical provenance in ignored local artifacts, not a portable source document or a later implementation receipt. Its proposal-only status predates this approval.

## Baseline and scope

Base main is `9b717ef8f242f0affa4b6b06529fcba682e5ce45`; working branch is `codex/windows-ci-test-isolation`.
Windows run `40367590447`, attempt 1, failed in `RecognitionSettingsTests.Failed_save_leaves_live_state_untouched_and_stale_work_is_dropped` and `WordLibraryVisualTests.Manager_and_dictionary_popup_render_in_dark_light_and_small_windows`.
The first timed out at its worker-entry barrier; the second reported concurrent corruption in the `AvaloniaLocator` registry, before dictionary operations. Exact historical scheduling and concurrent-writer identities remain **UNKNOWN**. Mechanism regressions and later green runs do not identify those historical actors.

## Global constraints

- No Computer Use, player launch, native UI smoke, provider/model work or new F-13 features.
- Keep the existing 5-second entry limits, cancellation behavior, assertions and skip rules. Do not increase timeouts, add skips or disable runner/collection parallelism. UI-bound tests may use the existing headless dispatcher; do not add serialization switches.
- Preserve failed-save/live-state, FIFO, stale-generation, queued-work and async-disposal assertions.
- Keep production behavior, dependency versions, schemas, defaults, release packaging and global runner settings unchanged. A production seam or wider architectural change requires a separate scope decision.
- Keep one shared Avalonia test source for Windows/Linux and retain WPF compatibility. Do not mistake headless evidence for native acceptance.
- Root serializes all builds/tests and owns Git/publication. Test workers have disjoint ownership; documentation is committed separately from test changes.
- Preserve original FAIL receipts and each later result. No automatic repeat until green; inspect a failing log before deciding any diagnostic follow-up.

## Review focus

- Early assertion failure or cancellation must release every accepted blocking worker before its resources are disposed.
- Async entry signaling must not consume a waiting test worker while preserving the same deadline and test cancellation.
- FIFO/save/stale-generation checks must exercise the original actions rather than becoming unconditional success assertions.
- Plain VM/font tests must not mutate global Avalonia services while another headless session owns their scope. The failing visual test already has `[AvaloniaFact]`; changing only that test cannot close the gap.
- A deterministic regression must exercise the real scheduling/service boundary, not inspect source text or merely assert the presence of an attribute.

## Task 1: Recognition settings barriers and cleanup

**Owner/files:** WPF test worker, `FlyleafLibTests/Core/RecognitionSettingsTests.cs`; a small recognition-test-only helper is allowed only after the coordinator confirms its exact path and need. Existing cancellation tests provide a pattern, not another rewrite target.

- [ ] Inspect all three entry barriers in the two affected tests and their accepted-task ownership.
- [ ] Propose a controlled early-failure/cancellation regression through a safe real seam. Coordinator and independent reviewer check the mechanism before implementation.
- [ ] Capture intentional RED with its expected failure and guaranteed cleanup. If a deterministic safe seam is unavailable, record the exact limit instead of claiming historical reproduction.
- [ ] Await entry through a completion source with `RunContinuationsAsynchronously`, the unchanged deadline and test cancellation. Release and drain accepted workers in `finally`, including failure paths.
- [ ] Run the same focused regression GREEN and retain every original behavioral assertion. Review the diff for timeout, skip and parallelism changes.

## Task 2: Avalonia UI-service test isolation

**Owner/files:** Avalonia test worker, only `LLPlayer.Avalonia.Tests/**`. First map plain `[Fact]`/`[Theory]` paths through `TestVm`, Sidebar/settings/font resolution and `FontManager.Current`; record the exact affected files before changing them. Preserve unrelated pure tests.

- [ ] Verify existing headless session/threading semantics and map actual global UI-service users.
- [ ] Design and capture a controlled RED for the service-scope boundary. Do not repeatedly race until failure or claim that its writer is the original CI writer.
- [ ] Move only mapped UI-service users to the existing Avalonia fact/theory/dispatcher mechanism; keep assertions and global parallelism intact.
- [ ] Run the same mechanism GREEN and relevant VM/Sidebar/settings/visual tests. If safe isolation would require production redesign, stop that expansion and report it.

## Task 3: verification, review and delivery

- [ ] Root runs focused GREEN and the unfiltered WPF `scripts/codex/verify.ps1`, Windows `scripts/windows/verify-avalonia.ps1`, package contracts and full Linux `scripts/linux/verify.sh` gates. Publish packages using existing scripts and record native/local prerequisites honestly.
- [ ] Spawn independent .NET, runtime and verification reviews for the changed test paths. Review documentation through infrastructure/instruction reviewers. Resolve Critical/Important findings before handoff.
- [ ] Update backlog/continuation/status from actual changed files and receipts. Keep a separate documentation commit; do not backfill the old audit or frozen contracts.
- [ ] Reconcile project-scoped Second Brain before publication/merge, using a new session and exact allowlist. Inspect actual PR CI logs and later exact merged-main CI logs; preserve baseline failures separately.
- [ ] Refresh the Desktop build from the reviewed final clean HEAD with the normal fresh updater and verify its receipt, payload and direct blue EXE shortcut. Do not launch the player.
- [ ] Finish the new scoped Vault session/gate/local commit and final spawned `/review`. Product CI findings close only with fresh evidence of the repaired tree; native/full F-13 acceptance remains separate.

## Evidence status

This is an approved implementation plan. RED, fixes, GREEN, new CI, final Desktop and Vault completion are **pending** until their actual receipts exist. The prior audit's Linux/local passes do not verify this new tree.
