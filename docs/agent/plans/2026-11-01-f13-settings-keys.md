# F-13 Settings and Hotkeys Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development. Steps use checkbox syntax for tracking. The owner already approved execution of the concrete design; do not ask for another generic plan/execution approval.

**Goal:** Implement the remaining approved Translation/Word settings and editable engine/app hotkeys in the shared Windows/Linux Avalonia app, retaining current Anki behavior.

**Architecture:** Extend the existing editors, detached preferences, host seams and playback commands. Keep native engine keyboard dispatch and existing WPF behavior. Module ownership is sequential where shared Settings/MainWindow files overlap; translation is independent.

**Tech Stack:** C#/.NET 10, Avalonia 12.1.3, CommunityToolkit.Mvvm 8.4.2, existing Flyleaf/Core and xUnit/headless tests. No new package/native dependency.

**Spec:** [Approved design](2026-11-01-f13-settings-keys-design.md). Original reviewed proposal SHA-256 `cfef2d461d14c5d065ab4d9d40bac7a2aff204ea401e15f7ff3235c2ee1462e4`; owner explicitly selected implementation in this chat.

## Global Constraints

- One shared Windows/Linux Views/ViewModels/Services implementation; WPF remains supported and unchanged.
- New clipboard/browser automation absent-default false; HTTP(S)-only search templates; PDIC and new Anki endpoint/key/APKG options excluded.
- Preserve existing app action IDs, legacy overrides, default timing, engine custom delegate identity and Anki session-deck ownership.
- Explicit Save atomically persists fresh owned fields before live application. Reset is draft-only until Save. Close/Escape discard. Loading/saving never probes providers or Anki.
- Explicit diagnostic requests use detached snapshots, cancellation/drain, generation checks and sanitized statuses. Model discovery preserves a custom model.
- All C#/XAML files retain required UTF-8 BOM. Existing files' intentional line endings stay intact. Runtime JSON, downloaded tools, models/logs/packages stay out of Git.
- No agent-run real browser, clipboard mutation, Anki/provider request, GUI/model acceptance or install. Native acceptance remains separately deferred.
- Root owns Git/index/commits and exclusive build scheduling. Workers do not commit, revert others, or run concurrent dotnet builds.

## Review Focus

1. Invalid raw decimal/null token cap and custom region values must not silently save a prior coerced value.
2. Provider/selection changes and close during asynchronous work must not publish stale results or leak secrets.
3. Fresh settings merges must preserve unrelated concurrent preferences and the open Word Manager session deck.
4. Held-key/focus/capture transitions must not execute twice, swallow engine releases or reconstruct custom delegates.
5. Malformed or legacy preferences must preserve prior working defaults, while Search never launches a non-HTTP(S) target.

## Task 1 — Translation advanced editor and owned diagnostics

**Own:** `LLPlayer.Avalonia/ViewModels/TranslationViewModel.cs` (optional focused partials), `Views/TranslationWindow.axaml[.cs]`, new `Services/TranslationDiagnostics.cs`; relevant Translation tests. Shared Core/Flyleaf cancellation overloads only where necessary, preserving old callers. Do not edit Settings/MainWindow/AppPrefs.

**Interfaces:** Consume `TranslationSettings.Clone/Validate`, existing atomic Save callback `Func<TranslationSettings, Task>`, provider settings types and current window lifecycle. Produce a test-injectable diagnostics interface with a Task result and CancellationToken; keep existing constructor callers source-compatible. The implementer owns exact internal type names, records them in the task report, and does not change the other modules' public interfaces.

- [ ] Add compiled RED for missing advanced control/raw validation and a fake explicit diagnostic that must not run on open/save; retain actual failure logs.
- [ ] Implement sampling/manual/ranges (temperature 0..2, top_p 0..1, penalties -2..2), nullable positive integral token caps, selected known region-map editing preserving unknown entries, narrow reset, model requirement/help/advisory.
- [ ] Add cancellable explicit Check/Get Models/Hello over immutable draft snapshots with injectable transport. Block conflicting editor/save state; drain on close; reject late results and sanitize errors, including responses containing synthetic credentials.
- [ ] Prove actual typed invalid values, switch/discard/reset/write failure, zero automatic calls, snapshot/cancel/reentrancy/custom model behavior and 400x360 layout; GREEN plus existing translation/lifecycle suites.
- [ ] Independent task review, fix confirmed Critical/Important; report exact files/interfaces/RED+GREEN and restore exclusive build lock. Root commits later with the whole product allowlist.

## Task 2 — Word actions, menus and opt-in copying

**Own independently of Task 1:** new `Services/WordActions.cs` (typed menu/policy) and focused VM partials if useful; `Services/AppPrefs.cs`, `ViewModels/SettingsViewModel.cs`, `Views/SettingsWindow.axaml`, `ViewModels/MainWindowViewModel.cs`, `Views/MainWindow.axaml[.cs]`, `Controls/SubtitleText.cs`, actual `IAppShell` declaration/host implementation and fake shell. Translation owns disjoint files, so production edits can proceed in parallel after compiled RED. Root serializes dotnet and freezes both authors' writes for compile-coherent snapshots. Hotkeys wait for the Word shared-file handoff.

**Interfaces:** Consume current word-selection data and `IAppShell.SetClipboardText`; add a Task browser host seam accepting a validated absolute HTTP(S) URI with a fake implementation in tests. Produce typed Search/Clipboard/ClipboardAll preference rows and detached Settings fields, cloning/normalization, runtime menu snapshots and safe template expansion. AppPrefs Save callback remains the fresh owned-field transaction. Exact internal names go in the task report for Task 3.

- [ ] Add compiled behavioral RED against existing UI/event/prefs behavior for menu editing, opt-in selection copy and exact Ctrl last-search. No compile error is RED evidence.
- [ ] Implement ordered enabled/title/action/lowercase rows with add/clone/delete/reorder and valid `%w`/`%lw`/`%s` expansion; enforce HTTP(S), URI-escaped replacements and independent right-click/lookup snapshots.
- [ ] Implement copy-on-select/last-search default false; auto-copy default false with Primary/Secondary/All and nonempty update ownership. Preserve mask/drag/control/popup routing; no middle-click expansion/PDIC.
- [ ] Merge/persist/apply all new owned fields transactionally and preserve Anki default plus open session deck. Reset remains draft-only.
- [ ] GREEN: real pointer/menu/input routing, legacy/malformed prefs, template encoding/refusal, failed Save/retry/discard and concurrent field preservation; Settings 560x440/Dark/Light/keyboard reachability.
- [ ] Independent task review and scoped fixes; report new interfaces and exact files. Release shared files/build lock for Task 3. Root owns commits.

## Task 3 — Add/clone/action hotkeys and explicit app rows

**Own after Task 2:** `Services/EngineKeyBindings.cs`, `Services/AppShortcuts.cs`, optional focused action-catalog file; shared `AppPrefs`, Settings VM/window, MainWindow VM/window, cheat-sheet wiring and key tests. Preserve all Task 2 fields/handlers. Do not edit translation or Word policy.

**Interfaces:** Consume existing app IDs/handlers, engine `KeyBindingAction` metadata, `IPlaybackController.Get/ApplyEngineKeyBindings`, native `Player.KeyDown/KeyUp` path. Produce a typed action catalog and app-row representation with action ID/chord/enabled/release; absent rows use legacy override behavior. App-first dispatch remains in current handlers; no new process/script actions.

- [ ] Compiled RED: expose unbound `ABLoopSetStart`, assign a free chord, Save/reload and invoke the real delegate; reassign/clone changes actual dispatch.
- [ ] Implement engine add/clone/reassignment and catalog, including unbound actions. Keep custom native binding objects/delegates untouched.
- [ ] Implement explicit portable app rows with multiple chords/enabled/release. Preserve legacy dual F1 defaults and on-down timing when new list absent.
- [ ] Prove duplicate/app-engine rejection both directions regardless of release, disabled-row behavior, matching-up/repeat/focus transitions, capture Escape/text suppression, failed write/retry and engine custom identity.
- [ ] Refresh live cheat sheet only after successful Save, including Reset then Save; preserve filter. GREEN new and existing key/settings/word compatibility suites plus 560x440 keyboard/UI tests.
- [ ] Independent task review and scoped fixes; root freezes product snapshot and separates product commit from docs/tooling.

## Task 4 — Contracts, integrated review and fresh gates

**Own root/docs worker:** matching `docs/agent/config-data-contract.md`, product/runtime contracts where behavior changes, `manual-smoke-matrix.md`, F-13 status/backlog current overlays. Infrastructure validators only if an actual declared structural contract changes. Never erase historical receipts or relax gates to hide a failure.

- [ ] Map every approved design requirement to implemented/tested files; cover Anki unchanged compatibility, explicit adaptations and unsupported/NOT_RUN boundaries.
- [ ] Review the integrated diff with real spawned spec/runtime/.NET/XAML/native/packaging/reuse/instruction/infra agents, bounded waves using available slots; fix confirmed findings and re-review deltas.
- [ ] Root: preserve exact reviewed source manifest, BOM/diff checks, stage exact product allowlist then separate contracts/tooling allowlist; create reviewed clean commits.
- [ ] Actual clean-head `scripts/codex/verify.ps1`, fast validators for docs/tooling, Windows Avalonia full verification, package contract tests and fresh publish/package-load checks. Retain exact commands/head/exit/full logs; no passing-count thresholds.
- [ ] Inspect any failure before changing source or rerunning. A new change invalidates affected earlier claims; preserve original logs and failure reasons.

## Task 5 — Authorized GitHub / Desktop / scoped Second Brain delivery

- [ ] Fresh GitHub target/base/open-PR readback; push normal feature branch, open PR and attach artifact; inspect complete Windows/Linux CI at final head. Linux runs full verify/publish/packaged loads.
- [ ] Fresh final independent whole-branch `/review`; close Critical/Important. Merge exact reviewed head using ordinary authorized flow; independently verify actual main and separate push CI.
- [ ] Default fresh `scripts/windows/update-desktop-test-build.ps1` on clean final main with existing SDK, no player launch. Verify direct EXE/no args/cwd/blue icon/current/receipt/source commit, fresh package bytes and prior payload preservation.
- [ ] Read fresh Vault state/preimages, name exact scoped allowlist, update completed session/index/tests/state snapshot, independent diff review, actual scoped session-close and isolated local commit with concurrency guards. No Vault push/Dashboard publication.
- [ ] Final independent actual receipt/source/CI/Desktop/Vault review; write final closeout with PASS/NOT_RUN/limits and one owner-selected continuation. Stop after this approved block.
