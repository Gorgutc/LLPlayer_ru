# F-13 Incremental Core Extraction: Whisper Model Catalog

## Summary
- Move the existing WPF-independent `WhisperCppModelLoader` catalog into a new `LLPlayer.Core` project targeting `net10.0-windows10.0.18362.0` and `net10.0`, matching FlyleafLib's target split.
- Keep the WPF callers on the shared implementation and add the Core project reference to Avalonia for later UI integration. This slice does not move download, model lifecycle, or other app logic, and does not change product defaults or configuration.

## Key Changes
- Characterize the current catalog behavior with portable tests: offered model/quantization ordering, filenames, downloaded status (`Size > 0`), and `EnsureModelsDirectory` side effects/error behavior. Use only a minimal injectable filesystem/directory seam needed for deterministic tests.
- Create `LLPlayer.Core`, move the existing catalog implementation there, and wire project references and solution configurations for WPF, Avalonia, and tests. WPF must call the shared implementation rather than retain a duplicate.
- Probe the actual `/models` `large-v3` catalog from portable tests/read-only test execution to establish Linux model availability. This checks catalog discovery only; it does not deliver Avalonia ASR UI or establish inference acceptance.
- Run the full Linux gate, then run `scripts/linux/publish.sh` and validate package contents include the Core assembly. Route changed paths through the review matrix and finish with explicit `/review`, including `verification_reviewer`.

## Affected Frozen Contracts
- Preserve the relevant config/data and media behavior: model defaults, offered ordering and filenames, downloaded status, directory side effects/error behavior, and existing WPF behavior do not change.
- The project dependency graph changes by adding multi-target `LLPlayer.Core` references. Release packaging must include its managed assembly. No new external package dependency or native/runtime asset is authorized.
- App architecture changes incrementally by introducing the first `LLPlayer.Core` slice; download and lifecycle ownership remain where they are. This slice does not deliver Avalonia ASR/OCR UI.

## Verification
- Add deterministic portable regression tests with a minimal filesystem/directory seam to characterize current behavior.
- Build both Core target frameworks through the solution/project graph, run focused portable tests, then run `scripts/linux/verify.sh`. Run `scripts/linux/publish.sh` separately and confirm the published package contains the expected Core assembly.
- **Characterization evidence:** after the extraction, an isolated temporary harness against the original HEAD loader passed (receipt: `artifacts/f13-core-2026-10-03/original.exit`, exit 0; log: `/tmp/llplayer-f13-core/original.log`). Seven focused tests against the final code passed, including an opt-in read-only `/models` catalog probe that confirmed `large-v3` metadata at 3,095,033,483 bytes; this was not an inference run. The package sensitivity check was intentionally performed after extraction by temporarily removing the Core requirement; the missing-Core fixture failed as expected (exit 1), and the requirement was restored. The published staging package with only `LLPlayer.Core.dll` removed was rejected (exit 1); restored package positive validation passed (exit 0). These package negatives are validator sensitivity evidence, not a pre-fix RED regression phase.
- **Gate results:** `scripts/linux/verify.sh` passed (FlyleafLibTests 1541 passed with 5 opt-in OCR-native skips, Avalonia 138 passed, shell suite 54 passed, validators and WPF/plugin cross-compilation). The separate Windows-target test-project build passed without warnings. `scripts/linux/publish.sh` and positive package validation passed; archive `artifacts/f13-core-2026-10-03/LLPlayer-0.3.62-linux-x64.tar.gz` is 179,089,582 bytes with SHA-256 `5cb03bb673c575766398592f51efd64678356234f17c41154bdbdaf19c8d357d`; it contains `LLPlayer.Core.dll` (7,168 bytes, SHA-256 `1e294d44fd5fdd3dbdc6ad0969eb8cbbaa9f63bfa89df0c6869be6fb1fc43b60`).
- Linux verification and package checks are complete for this catalog slice. Independent final verification review passed with zero Critical, Important, or Minor findings. The repository's Windows `scripts/codex/verify.ps1` gate and Windows-native runtime/GUI smoke remain pending on a Windows host.

## Assumptions
- This is a narrow first extraction chosen to unblock upcoming Avalonia UI work; subsequent Core extractions remain incremental and separately scoped.
- Linux `/models` availability is an environment observation, not a committed model payload or packaging change. Do not download or bundle model weights.
- Preserve the current directory creation and error semantics; adjust implementation details only if characterization proves the proposed seam needs refinement.
