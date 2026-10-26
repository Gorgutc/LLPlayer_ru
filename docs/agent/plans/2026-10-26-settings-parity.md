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
- Focused settings/translation/word suites: 80 passed, zero failed. Full Avalonia, WPF, Windows Avalonia and package facts are recorded in the FINAL2 logs and `docs/agent/f13-status-2026-10-18.md`.
- Contract/status/backlog/manual-smoke updated in the docs commit; plan committed here because ignored `artifacts/` is not evidence.

## Reviews

Round 1 on `593b106`: dotnet_quality_guardian PASS; media_runtime_mapper PASS (Medium pause divergence → fixed); wpf_xaml_reviewer PASS (clear-confirm/disarm, numeric editors, deck help, a11y → fixed); verification_reviewer FAIL (pause, deck-export non-vacuity, routing, invalid-Apply snapshot, blank copy, hub binding, absent-key tests → fixed in `0c70849`); codex_infra_architect FAIL (docs slice and dual deck owner → docs commit and next-open-only semantics). Delta re-review is recorded in the PR/vault handoff.

## Limits

No real keypress/desktop acceptance, no live provider/AnkiConnect/APKG import, no Linux local gate (CI runs it), native GUI/DPI — NOT_RUN / DEFERRED BY OWNER; player never launched.
