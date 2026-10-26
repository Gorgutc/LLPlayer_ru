# F-13 Settings parity: Translation / Word / Anki (2026-10-26)

Owner GO: «остальные страницы Settings parity - Translation/Word/Anki — работаем над всеми тремя». One block, three bounded slices; WPF/engine contracts preserved except additive Core translation parameters.

## Scope and status

### Translation — implemented
- WPF «Translation Parameters» ported into `LLPlayer.Core/Services/TranslationSettings.cs`: `TranslateCountBackward` (default 1, 0–1000), `TranslateCountForward` (12, 0–1000), `TranslateMaxConcurrency` (2, 1–64). Validation, round-trip in `LLPlayer.Translation.json`, `Apply` to the live engine `Subtitles`, and Translation-dialog numeric editors.
- Boundary (not ported, values retained untouched): LLM sampling/region-mapping editors, endpoint reset/check, Get Models, Hello API, provider wiki, «No Supported Service» badge.

### Word — implemented
- Additive `AppPrefs.WordClickAction` (`Translation` default, `Clipboard`, `ClipboardAll`), normalized case-insensitively with `Translation` fallback; Settings → Tools «Word» editor. Behavior in `MainWindowViewModel.OnWordClicked`: pause first (WPF parity), copy word/cue with blank guard, or open the existing dictionary popup; copy shows a toast.
- Boundary: WPF word context-menu action editor, copy-on-select, last-search modifier, PDIC/Japanese, auto text copy. Not exposed as dead controls.

### Anki — implemented
- Additive `AppPrefs.AnkiDeck` (trimmed, blank means `LLPlayer`, max 64, control characters rejected), Settings → Tools «Anki» editor; seeds the next Word Manager only (an open manager owns its session deck). Word Manager shows count (filtered/total aware), read-only Source/Language/Saved-at metadata, accessibility names and a two-step Clear-all confirmation that disarms on filter/delete/export and refuses an empty library.
- Boundary: AnkiConnect endpoint/key stays the WPF-compatible fixed `http://localhost:8765`; no APKG options.

## Verification

- Product commit `593b106915d12445a1249b1dff54e79ad66f8aa4`; review-response commit `0c70849bbb78bce690fb1d35a5265c94748cff54`.
- Focused settings/translation/word suites: 80 passed, zero failed. Historical FINAL3, superseding FINAL2, covers `6e73ad8c37f7a49e2989600a89ed7f61de707bc9`: Avalonia 966/14 skipped, WPF 1440/0, Windows engine 1622/13 + UI 979/1, package 53 fixtures + 38-file readback, exits 0. The later `e28e76c190e380e7d7f085a883dc265149387f66` changes a test for xUnit1031; later changes were not all docs-only.
- PR #185 MERGED at `d1f5427ff9a5793b358d6154507af69d2870c6e4`. Final PR-head Windows 40330061298/Linux 40330061376 and exact-main Windows 40331221672/Linux 40331222038 passed separately; these receipts do not erase earlier CI failures. See [F-13 status](../f13-status-2026-10-18.md).
- Contract/status/backlog/manual-smoke updated in the docs commit; plan committed here because ignored `artifacts/` is not evidence.

## Reviews

Round 1 on `593b106`: dotnet_quality_guardian PASS; media_runtime_mapper PASS (Medium pause divergence → fixed); wpf_xaml_reviewer PASS (clear-confirm/disarm, numeric editors, deck help, a11y → fixed); verification_reviewer FAIL (pause, deck-export non-vacuity, routing, invalid-Apply snapshot, blank copy, hub binding, absent-key tests → fixed in `0c70849`); codex_infra_architect FAIL (docs slice and dual deck owner → docs commit and next-open-only semantics). Historical last delta verdicts seq 7006/7008/7042 were FAIL; later fixes have no later independent final-tree PASS in that journal. New audit/corrective reviews are separate evidence.

The later read-only audit on `d1f5427` reproduced three numeric-control RED probes despite passing ordinary suites, and found adapter-test cleanup and Desktop/vault gaps. The owner's approved correction is implemented in `b36655d` with BOM-only `0e4635a`; clean `116a4ba` has its own local gates/reviews in [the audit-fixes plan](2026-10-26-settings-audit-fixes.md). That checkpoint preserves a new OpenAL attempt-1 FAIL and one unchanged full diagnostic attempt-2 PASS, cause UNKNOWN; publication/CI/Desktop/vault completion must be resolved from later actual receipts. This plan remains the historical three-page scope.

## Limits

At the original three-page stage: no real keypress/desktop acceptance, live provider/AnkiConnect/APKG import or Linux local gate; native GUI/DPI was NOT_RUN / DEFERRED BY OWNER and the player was not launched. The owner's later direct GO led to the [bounded candidate Windows audit](../f13-native-visual-audit-2026-10-26.md) on `fcf4b49`, with actual numeric UIA Save, deck/word/export behavior and explicit mock/seed/open-cell limits. Its results do not accept a later final-main payload or establish full Settings parity. Linux/provider/model/Anki/GPU/audible-output acceptance remains separate.
