# F-13: remaining Translation / Word / Anki settings and hotkeys

Status: APPROVED by owner 2026-11-01: Yes, implement the proposed composition. Approval covers the explicit adaptations below. Implementation and fresh verification are tracked separately; native/live acceptance remains deferred.

Source: clean local/GitHub main `f84f70676d2fa526073c061292ac8f252f2e6de4`, 2026-11-01. PR191 is delivered and is not repeated. Owner selected these settings and hotkeys as the next block.

## Intent and approach

Complete the source-proven remaining controls and actions within the existing common Windows/Linux Avalonia UI. Reuse existing provider records, settings transactions, word popup/library, playback commands and key dispatch. WPF remains supported with its current behavior, persistence and packaging.

The owner selected WPF parity. There is one suitable implementation approach: extend the existing shared Avalonia editors and services. A separate Linux/Windows UI would contradict that decision. New Anki product options and Windows PDIC are not required to implement the shared settings below and are proposed as separate work.

This proposal includes several explicit adaptations rather than silently copying WPF side effects: optional clipboard/browser automation starts disabled; Search templates accept only HTTP(S); model discovery preserves an entered custom model; existing Avalonia shortcut timing remains unchanged. Approval of the proposal includes these boundaries.

## Source corrections and existing coverage

- Existing Translation already exposes providers, dictionary mode, target language, endpoint/model/key/region/chat path, timeouts, translation scheduling, chat methods/context/prompts and resets. Advanced provider values already survive detached JSON clone/save/apply.
- Existing WordClickAction supports Translation / Clipboard / ClipboardAll. Word library, manager, TSV/APKG export and explicit AnkiConnect sending exist.
- WPF has no Anki endpoint/API-key/APKG-options editor. Both hosts share fixed localhost AnkiConnect and the fixed five-field card model. Avalonia already adds a persisted next-open default deck. The previous backlog answer incorrectly inferred missing WPF parity from an old stage's exclusions.
- Existing engine shortcuts support chord, enabled/release flags, delete/reset, persistence and two-way app/engine collision checks. Existing app shortcuts have stable IDs, default chords and legacy overrides.
- WPF Custom actions are predefined C# application commands, not scripts or arbitrary executable actions.

## A. Translation

Add the missing local editors to the existing Translation window:

- Temperature, top_p, frequency penalty and presence penalty, each with its existing manual flag. Validate raw decimal drafts before assigning to engine records; ranges/defaults come from the existing settings types.
- Nullable max_tokens and max_completion_tokens. Blank means unset; positive integral values only. Invalid/overflowing drafts stay visible and block Save.
- Google/Bing/Azure Chinese/French/Portuguese region maps. Preserve unknown/custom map entries when editing known entries or switching providers.
- Narrow endpoint resets and OpenAILike chat-path reset. Preserve credentials, model and all unrelated fields.
- Required/optional model indication, provider wiki link and unsupported-target advisory.

Include explicit Check / Get Models / Hello API commands. These never run on opening/loading/saving. Hello visibly identifies that it sends a model request. The new diagnostics use a captured detached provider snapshot, cancellable Task ownership, sanitized status and injectable fake transport. One operation at a time; provider/editor changes and close/shutdown cancel/drain or reject late results. Save cannot race an active diagnostic. Discovery failure/unsupported provider is a clear outcome; no invented success. Refreshing models never erases a typed custom model. Do not reuse WPF async-void commands or raw exception/status output.

No new provider, automatic probing, editable discovery protocol, secret-storage redesign or live agent-run provider acceptance.

## B. Word/dictionary actions

Extend the existing Settings Tools Word group with detached fields and ordered menu rows:

- Enabled/title/action configuration for Search, Clipboard and ClipboardAll; lowercase option where WPF provides it; add/clone/delete/reorder rows.
- Search substitutions `%w`, `%lw`, `%s` escape inserted values. Only valid absolute HTTP(S) templates launch through the host service. No arbitrary shell URI/path execution.
- Independent right-click menu snapshots versus the active word lookup. Preserve actual IsWord and modifier provenance through the view event; a stale menu must not act on a newer popup selection.
- Optional copy-on-select and exact-modifier last-search. Both default false for absent settings, including older preferences, to preserve current Avalonia clipboard/browser behavior. This intentionally differs from WPF's true defaults. Ctrl is the initial last-search modifier when enabled. Search continues to use the existing popup path.
- Optional automatic subtitle copying, default false, target Primary; Primary/Secondary/All choices reuse displayed source text semantics and ignore empty updates.

Clipboard and browser operations use host seams and can be tested with fakes. Masked/sidebar/control/popup input ownership stays intact. No middle-click behavior expansion, library import/recovery or PDIC in this block. PDIC requires a separately chosen Windows process adapter and native acceptance; a shared preference alone would not make it portable.

## C. Anki compatibility within this settings block

Retain the existing default-deck setting and all current export/send behavior; test that new Tools edits preserve it. An open Word Manager remains owner of its session deck, while the saved default seeds its next opening. Keep fixed localhost/version-6 AnkiConnect, current five-field TSV/APKG/model and existing cancellation/snapshot ownership.

No new endpoint/key, remote access, connection-test button, card templates, tags/media, filtered export or drag-reorder library feature. These are additive product decisions, not source-proven WPF settings parity. Push is never used as a connection test because it creates a deck/model and adds notes.

## D. Hotkeys

Extend the existing Tools editor and cheat sheet:

- Typed action catalog with stable persisted names for built-in engine actions and implemented portable application commands. Include unbound engine actions such as the four A-B actions; no controls for unavailable WPF windows/features.
- Add, clone and reassign engine rows; independent mutable clones, enabled/release flags, delete/reset and multiple free chords per action.
- Add an explicit app-row representation supporting multiple chords, enabled and On Release. Absent new representation retains all legacy AppShortcutOverrides behavior, dual F1 defaults and immediate-on-down timing. New app rows can opt into release behavior; existing defaults are not silently retimed.
- Preserve app-first dispatch and the Flyleaf engine keyboard path. Do not reconstruct engine-owned custom delegates from strings; retain their object/delegate identity.
- Enabled duplicate chords and app/engine conflicts block Save in both directions regardless of release phase. Preserve capture cancellation, text-input suppression, held-key/repeat/matching-release handling and missed-release cleanup.
- Refresh the cheat sheet from effective live bindings after successful Save, including Reset followed by Save, while retaining its filter. Reset alone changes only the detached draft.

No arbitrary user code, shell commands or unsupported application actions are introduced.

## Persistence and lifecycle

TranslationSettings owns detached translation records. AppPrefs owns the additive word/action/key UI preferences. Defaults and legacy normalization are explicit. Preserve unknown/unowned provider values and fresh-merge unrelated preferences.

All editors validate raw input. Explicit Save atomically persists a fresh owned-field candidate before publishing live changes; failure leaves disk/live state unchanged and the draft available for retry. Close/Escape discard drafts. New probes or UI controls must not bypass translation cancellation/drain/generation invalidation. Credentials remain masked; messages/logs/tests use synthetic values and never echo provider response bodies, secrets or complete config JSON.

One shared Views/ViewModels/Services implementation for Windows/Linux. OS-specific code is limited to existing host adapters. Existing Appearance/Subtitles/Tools navigation and separate Translation dialog remain; group advanced fields in expanders, footer outside scroll. Verify Settings 560x440 and Translation 400x360 minimum-size reachability, keyboard navigation and capture escape.

## Readiness and tests

- Deterministic compiled RED then GREEN for new behavior where a safe seam exists, not compilation errors as behavioral RED.
- Translation actual raw input/range/unset/manual/map preservation, switching, narrow reset, masked secrets, discard/write failure; load/open/save zero diagnostic calls; fake-owned diagnostics snapshot/cancel/late result/sanitization/custom model preservation.
- Word template encoding/type validation/order/clone, exact modifier, right-click snapshot independence, clipboard/menu/popup ownership, automatic slot copy and old-file defaults. Browser/clipboard tests use fakes, not real launches.
- Anki next-open default/session ownership and unchanged explicit exports/send with fake HTTP; zero provider/Anki requests from load/save.
- Hotkey Add/reassign/clone/unbound action actual delegate dispatch, collisions both directions, legacy default/override round-trip, write failure, app key-up/repeat/focus transitions, native custom delegate preservation, cheat-sheet refresh.
- Actual headless controls, smallest window, focus/blur, keyboard access and close/save failure tests. No global test-count threshold.

## Verification and delivery after approval

Work on `codex/*`; keep product and contracts/tooling changes in separate commits. Required gates: full WPF `scripts/codex/verify.ps1`, Windows Avalonia verification and package contracts, Linux full verification in actual Linux CI, fresh Windows/Linux publish/package-load checks as affected. Infrastructure/contracts changes require their fast validators. Independent spawned reviews follow the path matrix; resolve Critical/Important and perform a fresh final `/review`.

Use the normal authorized PR/CI/merge lifecycle, verify actual-main runs, then default fresh Desktop publish and exact final-HEAD blue-shortcut receipt/readback, preserving earlier builds. Complete scoped Second Brain session/index/tests/state sync with a fresh exact allowlist, concurrency checks, session-close and isolated local commit. No Vault push or Dashboard publication is inferred.

Native GUI, physical playback/audio, live providers/Anki/browser/clipboard acceptance and PDIC remain separate owner-selected acceptance. Automated/fake/headless gates never substitute for it. Existing WPF 7-Zip tool absence remains an environment limitation unless independently resolved within authorized tools; do not install tools implicitly or reuse old results as fresh evidence.

## Reference source

- `LLPlayer/Controls/Settings/Trans/OpenAIBaseTranslateControl.xaml`, `SettingsSubtitlesTrans.xaml`, `FlyleafLib/MediaPlayer/Translation/Services/ITranslateSettings.cs`.
- `LLPlayer/Controls/Settings/SettingsSubtitlesAction.xaml`, `LLPlayer/Controls/WordPopup.xaml.cs`, `LLPlayer/Services/AppConfig.cs`, `LLPlayer/Services/PDICSender.cs`.
- `LLPlayer/ViewModels/WordManagerDialogVM.cs`, `LLPlayer.Core/Services/AnkiConnectSender.cs`, `AnkiApkgWriter.cs`, `FlyleafLib/MediaPlayer/AI/AnkiApkgModel.cs`.
- `LLPlayer/Controls/Settings/SettingsKeys.xaml.cs`, `LLPlayer/Services/AppActions.cs`, `FlyleafLib/MediaPlayer/Player.Keys.cs`.
- Existing Avalonia TranslationViewModel/TranslationWindow, SettingsViewModel/SettingsWindow, MainWindowViewModel/MainWindow, AppPrefs/AppShortcuts/EngineKeyBindings/FlyleafPlaybackController and their tests.
- `docs/agent/config-data-contract.md:109-118`, `docs/agent/f13-status-2026-10-18.md:391-431`.
