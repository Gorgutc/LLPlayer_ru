# F-13 Avalonia Recognition UI Slice

## Summary
- Add a first Avalonia UI path for interactive whisper.cpp CPU recognition and OCR on the currently selected bitmap subtitle track, backed by shared Core recognition settings and canonical Flyleaf engine actions.
- Keep Windows WPF behavior, engine defaults, and existing configuration intact. This slice does not add model downloads/installers, GPU selection, faster-whisper UI, or full settings parity. Host shutdown drains/disposes queued recognition actions; it does not migrate or autosave the live player configuration.

## Key Changes
- Reuse the existing engine recognition settings/types and the Core model catalog. Persist only UI-owned selections in a separate `LLPlayer.Recognition.json` under `AppPaths.ConfigDir`; load/save tolerantly and atomically, and do not overwrite corrupt input during startup/shutdown. Keep edits detached until Save & Close; Close discards them.
- Provide an optional `WhisperCppConfig.ModelDirectory` resolver, default `null`, reused by ASR validation, model factory, and batch snapshot. The snapshot retains the field. Explicit Core catalog discovery is read-only; the existing no-argument loader keeps its current behavior. The user's existing full `large-v3` weights stay outside the repository.
- Add a recognition dialog/menu path in Avalonia using existing shadcn token controls. It selects an existing whisper.cpp model directory/model and auto/explicit language; starts ASR on primary or secondary slot; pauses/resumes globally; cancels a selected slot; and runs OCR on an already-selected bitmap subtitle track. OCR falls back to the selected language only when track metadata is unknown. Show text status/errors and no fabricated OCR progress percentage.
- Bind UI actions through canonical Commands rather than calling engine `Execute` directly. Keep operations routed through existing app/engine ownership and cancellation boundaries.

## Affected Frozen Contracts
- `product-behavior-contract.md`: additive Linux interaction; keep dual-slot subtitles and existing ASR/OCR semantics. WPF behavior is unchanged.
- `media-runtime-contract.md`: call existing recognition/OCR lifecycles via canonical commands; preserve per-slot cancellation, global ASR pause/resume, selected-bitmap OCR, and engine ownership.
- `config-data-contract.md`: new isolated, additive recognition settings file; current-session dialog edits are detached, Save & Close writes atomically before applying, Close discards, and corrupt data is never replaced by shutdown serialization.
- `wpf-design-contract.md`: no WPF UI change; Avalonia follows its existing dialog/menu and shadcn-token patterns.
- `dependency-baseline.md` and packaging: no new model payload, download, installer, or dependency is introduced by this UI slice.

## Verification
- Establish deterministic regression tests for recognition-settings defaults/round-trip, atomic save, corrupt-file preservation, Save & Close versus Close, catalog discovery without mutation, and command routing/cancel/pause semantics. Record intentional RED evidence where the baseline exposes missing behavior; the captured initial RED specifically showed two missing resolver/catalog APIs and is not evidence that all regression cases were run RED. Tests must not substitute synthetic native calls for GUI acceptance.
- Run focused Core/Avalonia tests and `scripts/linux/verify.sh` (including WPF/plugin cross-compilation); build the Windows-target projects. Windows-native full tests remain pending unless run on Windows.
- Run `scripts/linux/publish.sh` and positive/negative package-content validation; verify the package adds no weights or unintended runtime files.
- Use headless Avalonia UI/controller with a real `Player` and existing `/models` whisper.cpp `large-v3` weights to verify CPU ASR through the UI path and real cue output. Render dialog PNGs headlessly for visual inspection; do not open live desktop windows. This is distinct from direct old-service inference. Perform real-media OCR only if a safe, already-available bitmap fixture exists without download; otherwise record that boundary. Native GUI/owner acceptance remains pending. Never report synthetic native tests or direct old inference as full UI acceptance.
- Route changed paths using `docs/agent/subagent-review-matrix.md`; obtain cumulative reviews for shared Core, media runtime, Avalonia UI, docs/package paths and a final `/review` including `verification_reviewer`. Resolve Critical/Important findings before handoff. Do not commit or push.

**Current receipts (2026-10-03):** captured RED is limited to two missing resolver/catalog APIs. Fourteen focused Core recognition-settings/action tests passed. The fresh Linux gate passed (FlyleafLib 1554 passed/6 skipped; Avalonia 139 passed/1 skipped); the real-Player headless UI-command smoke passed 2/2 in 14 seconds with large-v3 CPU ASR and English/Russian PGS OCR. Dialog dark/light/small/lower renders passed visual inspection. Windows test-project cross-compilation passed. Publish/package validation passed; the archive `artifacts/f13-recognition-2026-10-03/package/LLPlayer-0.3.62-linux-x64.tar.gz` is 179,121,762 bytes, SHA-256 `b2ad514a4eaa53ad0aecd4e188af934d5e22af88682a4db9f5ca060d5a5651a6`. A package-negative harness rejected `LLPlayer.Recognition.json`, its `.bad` variant, and the leading-dot atomic-write `.tmp` variant; harness exit 0 is recorded in `package-negative.exit` / `.json`. The independent final verification review passed with zero Critical/Important findings. Native desktop GUI and Windows runtime/owner acceptance remain pending. Receipts are under `artifacts/f13-recognition-2026-10-03/`.

## Assumptions
- The existing model catalog and engine settings are authoritative; Core reuses engine recognition types rather than duplicating defaults or migrating the live player `Config` graph.
- The optional model-directory resolver must default to the existing behavior, so Windows and current installs remain unchanged.
- Runtime model paths and recognition selections are user data; weights remain outside the repository and package.
- This is an incremental F-13 recognition UI slice. Fuller recognition settings parity and faster-whisper UI remain later work.
