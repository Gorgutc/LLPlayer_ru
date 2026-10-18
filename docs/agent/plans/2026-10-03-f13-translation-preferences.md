# F-13 Linux Translation Preferences and WordPopup Lifecycle

## Summary
- Add Linux/Avalonia word-translation provider preferences backed by existing Flyleaf translation settings and a detached Core model, with atomic persistence in `LLPlayer.Translation.json`.
- Add configuration for provider/Off, target language, and the selected provider's existing endpoint, model, API key, timeout, and relevant option forms. Keep current WPF behavior and existing engine provider defaults intact.
- This slice does not add dictionaries, Save Word, Word Manager, Anki, or automatic sentence/subtitle translation; those depend on this provider/preferences foundation.

## Key Changes
- Add Core `TranslationSettings` and a tolerant `TranslationSettingsStore` for `LLPlayer.Translation.json`. Preserve concrete `ITranslateSettings` implementations and `TranslateChatConfig` through round-trip. Writes are atomic. Mask secrets in UI; loading, validation, and saving perform no endpoint/model probes and no network calls.
- When the translation file is absent, seed the new settings once from the legacy `AppPrefs.WordTranslationService` and `TranslateTargetLanguage`. Once the new file exists, it is authoritative. On malformed/corrupt data, preserve its original bytes and show a warning; do not silently replace it from legacy preferences.
- Add a main-menu translation-preferences entry and a settings entry from the WordPopup `NotConfigured` state. Save applies a validated detached snapshot; Close discards edits. `Off` remains a supported provider choice.
- Invalidate WordPopup/translator cache and provider state when effective provider settings or source/target language change. Advance a configuration generation so stale operations cannot populate caches or UI. Cancellation must prevent late UI/cache writes, and each operation owns its CTS until all token consumers complete; teardown drains/cancels before disposing popup-owned services.
- Keep changes Linux/Avalonia-facing; do not alter WPF provider defaults, engine providers, dependency versions, or release runtime payload.

## Affected Frozen Contracts
- `product-behavior-contract.md`: additive Linux word-provider controls and safe invalidation of the existing word-popup translation; no dictionary/word-manager/sentence-translation behavior in this slice.
- `media-runtime-contract.md`: reuse `ITranslateService`, `ITranslateSettings`, `TranslateServiceType`, `TranslateServiceFactory`, and `TranslateChatConfig`; retain operation-owned cancellation and block stale cache/UI writes after provider/language changes or teardown.
- `config-data-contract.md`: new isolated `LLPlayer.Translation.json`, absent-file legacy seed, authoritative existing file, atomic save, corrupt-file preservation, and secret handling. Do not migrate or serialize the full Flyleaf `Config` graph.
- `wpf-design-contract.md`: no WPF changes; Avalonia controls follow existing shadcn-token patterns.
- Dependency/native/package baseline: no new dependency, downloaded model, native asset, or runtime library.

## Verification
- Add deterministic provider fakes and regressions for provider/Off and target round-trip; concrete engine settings forms including secret masking; `TranslateChatConfig` preservation; absent-file legacy seeding and existing-file authority; atomic failed-save and corrupt-file byte preservation; no endpoint probe during load/save; and provider/language changes invalidating cache and preventing an unchanged-provider result from a prior generation from reaching UI/cache.
- Exercise concurrent/superseding popup operations and teardown: cancel old work, hold a fake consumer until completion, and prove its CTS/service is not disposed early and no late result changes text, status, cache, or notification state.
- Capture intentional RED evidence at baseline for the missing settings/model/store or stale-result behavior actually demonstrated; do not imply every regression test had a RED run.
- Run `scripts/linux/verify.sh --fast`: portable projects, unfiltered tests, and validators. On the current Linux execution host this fast command does not perform the Windows app/plugin cross-compilation; report that gate as skipped/deferred by the owner’s Linux-priority direction, not passed. Do not run Windows runtime/UI acceptance.
- Publish to a unique path such as `artifacts/f13-translation-preferences-2026-10-03/package/` with `scripts/linux/publish.sh --out <path>`; validate positive contents and reject `LLPlayer.Translation.json`, secrets, and temporary files from the package. Do not put credentials in fixtures or receipts.
- Render and inspect headless Avalonia settings and WordPopup states in dark/light themes, including `Off`, missing configuration, masked key, validation failure, and changed provider. Headless provider fakes prove configuration/lifecycle only; no live endpoint request or live desktop/Windows runtime proof is in scope.
- Route changed paths with `docs/agent/subagent-review-matrix.md`; request cumulative Core, runtime, WPF/XAML, docs/package reviews and finish with `/review` including `verification_reviewer`. Resolve Critical/Important findings. Do not commit or push.

## Assumptions
- Existing engine provider classes and defaults are authoritative; the app stores and applies a detached selection without rewriting or migrating the whole player config.
- No endpoint verification, model discovery, or secret validation that contacts a provider occurs during load, save, or tests.
- The owner’s 2026-10-03 direction keeps Windows Avalonia implementation and live acceptance deferred until Linux parity is complete; existing Windows compatibility contracts and CI policy remain in force.
- Implementation and scoped source/domain reviews are complete. The Linux `--fast` gate passed: FlyleafLib 1554 passed/6 skipped, Avalonia 173 passed/1 skipped, shell tests 60 passed; portable builds had zero warnings/errors and validators passed. Publish and package validation passed. The archive is 179,141,059 bytes, SHA-256 `0b9505e7de323e23c2f943904da94142fcf0eed94d1d68034b317985c078603a`; package positive content validation passed and translation settings/backup/dotted atomic-temp negative fixtures were rejected. This command does not perform Windows app/plugin cross-compilation; Windows app/test compilation and runtime/UI acceptance are deferred for this slice by the current Linux-priority direction.

**Current status:** the focused deterministic suite is green at 35 tests. Captured RED evidence for this slice covers premature operation-CTS disposal and save admission applying a stale snapshot (not every added regression). Core/runtime/UI/package source reviews and final `verification_reviewer` passed with zero Critical/Important findings; 21 source hashes matched. Headless dark/light/small/lower dialog renders and the popup's Not Configured → Translation settings action passed visual review with fake credentials only. No provider endpoint was contacted. Linux `--fast` and package validation passed; archive SHA-256 and package positive/negative/frozen-validator receipts are recorded above.
