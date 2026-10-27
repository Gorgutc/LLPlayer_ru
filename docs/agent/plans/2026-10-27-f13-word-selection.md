# F-13: word and phrase selection in subtitles (shared Avalonia UI), 2026-10-27

## Summary

- Adds the already-documented WPF subtitle word behavior to the shared Windows/Linux Avalonia UI: pressing a
  word pauses playback, dragging from one word to another selects the phrase with a visible highlight, and the
  release performs the current `WordClickAction` (Translation popup, Clipboard phrase copy, or ClipboardAll cue
  copy). A simple click remains a single-word action; a phrase takes the source text between the two word
  boundaries, with hard line breaks rendered as one space.
- Does not change WPF, `FlyleafLib`, `LLPlayer.Core`, config/defaults, dependencies, package scripts, or any
  persisted preference. `SelectionBrush`/`SelectionOpacity` are transient visual properties of the control,
  theme-bound in XAML; selection state is not saved.
- Scope is the shared `LLPlayer.Avalonia` subtitle text control, its two consumers (overlay and sidebar rows),
  the existing word action path, and headless Avalonia tests.

## Key Changes

- `LLPlayer.Avalonia/Controls/SubtitleText.cs`: word-range lookup, pointer capture and selection state,
  `WordPressed` event, normalized phrase extraction, per-line selection highlight, and an `IsWord` flag on the
  click event. The highlight is drawn after the optional subtitle background and before shadow/outline/fill,
  and is not hit-testable.
- `LLPlayer.Avalonia/Views/MainWindow.axaml` / `.axaml.cs`: bind the existing `Shadcn.RingBrush` token to the
  selection brush on both overlays and sidebar rows; pause on press for both overlay controls. The existing
  masked-sidebar-row guard is preserved.
- `LLPlayer.Avalonia/ViewModels/MainWindowViewModel.cs`: explicit `PauseForWordSelection()` for the press path;
  the release path keeps the existing popup/clipboard dispatch unchanged.
- `LLPlayer.Avalonia.Tests/SubtitleSelectionTests.cs`: deterministic headless coverage for forward/reverse
  drags, wrapped and hard-broken lines, single click, release outside words, highlight pixels, pause-on-press,
  phrase popup translation, Clipboard/ClipboardAll, sidebar rows and the masked-row guard.

## Affected Frozen Contracts

- `product-behavior-contract.md`: **no change.** Left-drag phrase lookup, pause-on-selection and the word action
  set are already documented product behavior; this slice brings the shared Avalonia UI to it.
- `config-data-contract.md`: none — no new persisted key or default.
- WPF design, media runtime, dependency baseline and packaging: none — WPF is untouched and no native/package
  surface changes.
- `manual-smoke-matrix.md`: new future native GUI rows for selection. Native desktop acceptance remains
  **NOT_RUN** under the no-Computer-Use restriction; headless render/pixel tests do not close it.

## Verification

- Deterministic RED/GREEN at the existing seams: the public word/selection event contract of `SubtitleText`, and
  the real `MainWindow` over the existing fakes. Tests must fail behaviorally on the pre-change tree and pass
  after the change; no skips, timeout changes or weakened assertions.
- Local gates, sequential from the repository root:
  `verify-fast.ps1`, `verify.ps1`, `verify-avalonia.ps1`, `test-package-avalonia.ps1`, `publish-avalonia.ps1`.
  Observed counts and log hashes are receipts, never a fixed quality threshold.
- Local Linux is **NOT_RUN** (WSL1 without a distribution); the Linux gate is the PR/actual-main CI job.
- Review routing from `subagent-review-matrix.md`: `LLPlayer.Avalonia/**` and its tests require
  `dotnet_quality_guardian`, `media_runtime_mapper`, `verification_reviewer`, plus `wpf_xaml_reviewer` for
  `*.axaml`; docs require `codex_infra_architect`, `instruction_drift_auditor`, `verification_reviewer`.
  Completion requires an explicit spawned `/review` with zero open Critical/Important findings.
- Delivery (separate receipts): PR and required CI, merge, exact merged-main CI, default Desktop updater from
  the final clean HEAD without launching the player, and a scoped Second Brain closeout. No player launch or
  Computer Use is part of this plan.

## Assumptions

- WPF is the behavioral reference (`SelectableSubtitleText` phrase selection, `WordPopup.OnWordClicked` action
  dispatch). Avalonia selection is word-bounded; the WPF sidebar's native character-level `TextBox` selection is
  an additive simplification for the single shared control, recorded as a boundary rather than claimed identical.
- The visible highlight reuses the existing `Shadcn.RingBrush` token at 35% opacity; no new theme token or
  setting is introduced.
- Native desktop visual/keyboard acceptance remains **NOT_RUN**; headless pixel assertions are not native
  acceptance and do not establish DPI or real-hardware behavior.
- No new config keys and no persisted selection state; a cue change or window close drops the selection.
