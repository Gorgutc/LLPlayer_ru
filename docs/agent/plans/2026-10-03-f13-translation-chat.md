# F-13 Linux Translation Chat Settings

## Summary
- Expose the existing `TranslationSettings.Chat` options in the Avalonia Translation dialog as a detached draft, with Save & Close applying saved values and Close discarding edits.
- Preserve engine defaults, provider records, and ordinary translation semantics; keep the Windows UI/defaults unchanged. This is a settings UI slice, not full F-13 translation parity.

## Scope
- Expose `TranslateMethod`, `ContextWindowBefore`/`ContextWindowAfter`, `GrammarCheckEnabled`, `IncludeTargetLangRegion`, the four existing mode/grammar prompts, and `KeepContext` conversation count/retention policy.
- Context-before, context-after, and KeepContext count accept whole nonnegative `Int32` values. Zero context-before/after disables that side; zero KeepContext count retains no prior subtitle lines. Validate raw UI text on Save. Explicit, isolated reset-to-engine-defaults actions must not affect provider credentials, target language, provider selection, or other saved preferences.
- Preserve existing prompt text and engine-provided defaults unless the user explicitly resets them. Preserve provider-specific regions and advanced settings even when they are not surfaced here.
- Reuse detached Save & Close / Close lifecycle. Saving may resume ordinary subtitle translation only for a slot explicitly enabled in the current session. Do not issue endpoint health, model discovery, or probe requests.
- Keep sampling controls, provider-specific region mappings, custom WordActions/gestures, live provider acceptance, and remaining full point-3 parity out of scope.

## Affected Contracts
- `config-data-contract.md`: document the existing `TranslationSettings.Chat` record and detached draft lifecycle; no new file/schema or engine-default migration.
- `media-runtime-contract.md`: document the editor as a settings surface over existing engine chat semantics; only add overflow-safe indexing for the extreme `Int32.MaxValue` context-window edge.
- `manual-smoke-matrix.md`: cover chat modes, prompt editing/default reset isolation, KeepContext count/retention, Save/apply and Close/discard, and no-probe behavior.
- `LLPlayer.Avalonia/README.md`: describe implemented settings and evidence boundaries without claiming full parity.

## Verification
- Add deterministic UI/store lifecycle coverage for load/edit/save round-trip, Close discard, each mode/grammar prompt, count/retention and target-region-toggle persistence, and isolated resets that restore engine defaults without changing provider/target/credentials. Preserve custom prompt text exactly; only explicit reset normalizes engine default constants to LF.
- Cover overflow-safe context-window index calculation at `Int32.MaxValue` with a focused regression; preserve ordinary/default behavior and avoid broader engine redesign. Windows compilation/runtime is deferred, so do not claim byte-identical behavior for every WPF edge.
- Verify no health/probe calls during load/save and explicitly enabled subtitle translation resumes only through the existing save/apply lifecycle; no live provider calls are required.
- Run the applicable Linux fast verification gate and record results below. Render relevant dialog states where supported; headless evidence does not establish live desktop acceptance.
- Publish and install through the approved continuation. Require independent package review before install; verify launcher readback, package hashes, prior installation and XDG preservation afterward.
- Keep Windows Avalonia implementation/live acceptance deferred. Do not claim full translation parity.

## Assumptions
- `TranslateChatConfig` and its current engine property initializers/constants define defaults and semantics; no engine or WPF defaults change.
- Existing absent-defaulting and serialization behavior remains unchanged; this slice exposes the already-persisted chat record.
- Artifact base: `/models/codex-builds/LLPlayer_ru/artifacts/f13-translation-chat-2026-10-03`; installed package root: `/models/codex-builds/LLPlayer_ru/installed`.

## Execution Receipt (fill after implementation and verification)
- Focused UI/settings suite: 49 passed (`focused.log`); the three negative-count validations had genuine RED evidence in `red.log` before the validation fix. Context-window overflow regression: one genuine RED (`overflow-red.log`) for the missing forward cue at `Int32.MaxValue`, then 34 passed in the targeted GREEN run (`overflow-green.log`).
- Linux `verify.sh --fast` passed after restore (`linux-fast.exit` 0): FlyleafLib 1593 passed/6 skipped; Avalonia 196 passed/1 skipped; shell tests 64 passed; portable builds and validators passed. Windows build/runtime remains deferred.
- Publish and package coherence passed (`publish.exit` 0). Archive: `/models/codex-builds/LLPlayer_ru/artifacts/f13-translation-chat-2026-10-03/package/LLPlayer-0.3.62-linux-x64.tar.gz`, 180,005,849 bytes, SHA-256 `4bb2f0545bae7c3cd2544f46adec4e94f539d4f90a816696613b6f76bd029b90`. The package archive's 473 regular files matched staging; apphost and three release DLL hashes matched Release output. A specifically enumerated 333-source-file projection was stable before/after publish; it is not an exhaustive whole-tree manifest, and Markdown is excluded.
- Independent source/UI/runtime review and final package review passed. The parent inspected six mode/theme screenshots for readability and footer visibility.
- Installation/readback passed. The launcher now targets `/models/codex-builds/LLPlayer_ru/installed/LLPlayer-0.3.62-linux-x64-chat-4bb2f0545bae`; only `Exec`/`Icon` changed and its SHA-256 is `512ad9035b5f75004b0f1889ba563ac0ecff02854f3d9a5c0da1af0465e9724b`. Seven installed payload hashes match; all seven hashes in the previous subtitles installation match the preserved prior package. XDG state hashes are unchanged, no process was running before/after, and the app was not launched or killed. Receipts: `/models/codex-builds/LLPlayer_ru/artifacts/f13-translation-chat-2026-10-03/installation-receipt-chat.json` and `install-readback.json`.
- Deferred: Windows build/runtime/UI acceptance, live provider acceptance, sampling/provider-specific region mappings, custom WordActions/gestures, remaining full point-3 parity.
