# F-13: subtitle search/export and video mouse actions — 2026-10-28

Owner approved option 1 (advanced subtitle search and SRT/VTT/TXT export) and reported missing video right-click
menu and left-click pause. One shared Avalonia implementation serves Windows and Linux. Base: PR189/main
`a9f57c23def397f1d64ee40b4e7b3866d0723779`; work branch `codex/f13-search-export-input`.
The initial phase used headless UI input and native-library gates. The owner's later explicit "Computer Use
разрешаю" authorizes a bounded native Windows smoke of this stage after the build, in an isolated profile.
That smoke remains NOT_RUN until its actual receipt. Native Linux, real providers/models and GPU acceptance
remain outside this desktop check; headless checks and native acceptance are reported separately.

## Design and boundaries

- Reuse existing `SubtitleSearcher` and `SubtitleExporter`; preserve WPF and existing engine defaults/dependencies.
- Search the existing primary-only sidebar's original/display source, never spoiler-mask glyphs. Add match-case,
  Unicode whole-word and regex switches (default false), trim the query like WPF, and adopt its ordinal literal
  matching. Three switches persist in Avalonia preferences; the query and navigation are session-only. Invalid
  patterns give zero results and a visible error. Compile once; abort a complete filter pass on its first regex
  timeout, using the existing per-match bound. Preserve cue identity, translation refresh and masking.
- Add next/previous match commands with wrapping seek and Enter/Shift+Enter bindings; Escape clears the query and
  restores existing focus behavior. Sidebar secondary-slot redesign is excluded; export supports both slots.
- Export offers primary/secondary slot, Original / Translated only / Translated with fallback, SRT/VTT/TXT, and
  UTF-8 BOM (initially on). Reuse WPF text/style semantics; copy detached cue strings/times/source styles before
  awaiting the native save picker. No silent bitmap conversion, no translation/provider work. Show partial-output
  warning during recognition. An explicitly confirmed save can replace its chosen destination atomically; picker
  cancellation writes nothing. Closing/shutdown cancels and drains export before player disposal. Existing words
  TSV/APKG export is separate and unchanged.
- Existing video tap only dismisses a word popup; no generic video context menu is attached. Wire unmodified bare
  video left click to existing guarded play/pause and right click to shared subtitle/track/settings commands.
  The unmodified first left click immediately toggles play/pause, or only dismisses an open word popup; the second
  tap toggles fullscreen without a second play/pause toggle. Do not debounce single-click playback. Preserve
  subtitle press-pause/drag/cancel, masked guards, popup/button/slider ownership and wheel volume. Add VideoView
  hit-test changes only if genuine routed input reproduces a hit-test failure.
- Keep product source separate from agent/documentation commits. No new package, timeout increase, skip or runner
  parallelism change. No other F-13 feature, GPU, ASR/OCR runtime or Linux desktop integration is included.

## Implementation and verification

1. Capture clean base and toolchain; run existing relevant headless tests as baseline.
2. Add deterministic input/search/export tests first. Root runs .NET commands serially on pinned SDK 10; preserve
   actual RED failures separately from compile/setup errors. Implement scoped workers' changes after actual RED.
3. Root integrates shared sidebar controls/navigation, preference merge, export singleton/menu/storage lifecycle
   and video pointer/menu routing. Do not duplicate exporters or bypass shared track commands.
4. Run focused regression suites, independently review actual UI/.NET/runtime/config/docs changes and address
   Critical/Important findings. Run full unfiltered WPF and Windows Avalonia gates, package contracts and publish.
5. Commit reviewed product and documentation separately; verify PR and exact merged-main Windows/Linux CI.
   Refresh the Desktop test build from clean final source with the default fresh updater and static readback;
   preserve earlier payloads and verify sourceCommit/direct EXE/no arguments/working directory/blue icon.
6. Run the newly authorized bounded native Windows smoke with its own receipt; report exactly what was exercised
   and keep untested GUI/DPI surfaces, native Linux and real models/providers/GPU NOT_RUN. Perform independent
   final /review and project-scoped Second Brain session-close/isolated local commit. Stop after this approved stage.

## Evidence home

`artifacts/f13-search-export-2026-10-28/` contains actual commands, exit codes, RED/GREEN logs and delivery receipts.
Historical PR189 receipts are context only and do not accept this new stage. Counts are observations, not gates.
