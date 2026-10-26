# F-13 native Windows visual audit — 2026-10-26 candidate

This is a **bounded candidate audit**, not full manual-smoke/F-13 acceptance or acceptance of a later merged-main
package. The owner's new direct Computer Use/Windows visual GO superseded the earlier method restriction after
the local code/gate phase. Root was the sole GUI actor; spawned agents independently inspected source, screenshots
and persisted artifacts. No application/test source changed after the clean local-gate checkpoint `116a4ba`.

## Candidate and evidence identity

- Source: `fcf4b4912c23f080f4db1a5a005e669d6372a566`; fresh-published schema-2 build
  `fcf4b4912c23-20261026T151130963Z-1aca0724`. Deployment receipt SHA-256
  `d0f05437168ad861cb31287c6e5eb37ef0539660ba4f6e9ec766b2eb3b2109f2`; EXE SHA-256
  `5dea04936c3decba81781c40c9764e375123c999918ce971ce965be5d99f4a56`.
- Native Windows, .NET 10.0.7/CoreCLR; app-local FFmpeg/Skia/OpenAL modules observed. The stable Desktop delivery
  points directly to this payload's EXE. The audit launched that EXE with an isolated process-only profile, not the
  user's normal profile: `artifacts/dsh-two-session-audit-2026-10-26/visual/profile-fcf4b491-d62fe5f6`.
- Generated 12-second `test-720p.mp4`, synthetic English cues/words and an explicit loopback DeepLX mock at
  `http://127.0.0.1:63600`; no real provider credentials or user vocabulary. Native process IDs were 40460, 27668,
  13156 and 13608. A sandbox launch PID 37292 exited without an interactive window; this establishes a launch
  boundary, not a diagnosed product failure.
- Evidence root: `artifacts/dsh-two-session-audit-2026-10-26/visual/`. `screens-manifest.json` binds 29 images by
  bytes/hash/dimensions. Their actual format is **JPEG**, despite the legacy `.png` filenames. Root action
  observations establish timing, clipboard/keyboard behavior and window identity; static screenshots alone do not.

| Receipt | SHA-256 |
| --- | --- |
| `native-visual-candidate.json` — formal case observations | `506c0f5b40eeb87be1d0677fef4a2940fc62564fdf50bdc109e82740b4a2bee1` |
| `screens-manifest.json` — screenshot byte identities | `3d3a07e6a09b495fed8bb489249ec0aa3cefc111038a0290b59495ab7d670efe` |
| `native-artifact-readback.json` — early profile and three-note exports | `803298421ab9ee14448b7b559e72f742eba5b61c8ee6f191c793ddeb43c0f203` |
| `native-final-profile.json` — later four-word profile | `1a2152168086af497d6b9fb85f66a29e554d4a44570803a0ec544aaaa03bcaf3` |
| `mock-final-receipt.json` — owned mock shutdown | `28d295814ce2a375f1582df10148b515271a9b188f104983f42edf0faa078d9f` |

## Case-level results

The IDs/statuses below follow the formal receipt; they are observations, not a fixed passing-case quality gate.
Screenshot references are filenames under `visual/screens/` in the manifest.

| Case | Result | Native observation and boundary |
| --- | --- | --- |
| N01 startup/delivery | PASS | Fresh candidate/direct EXE, isolated profile and loaded runtime/native modules observed (`01-startup-0.png`). This does not accept a later payload. |
| N02 open/play/seek | PASS | Native media picker opened the generated clip; video rendered/played to end, paused/sought; three English cues/highlight followed time. Audible output was not checked. |
| N03 fullscreen | PASS | F entered 1920×1080 fullscreen; Esc restored the ordinary window; overlay/sidebar/transport readable (`03-fullscreen.png`). |
| N04 A-B/waveform | PASS | A ≈1.133 s/B 4 s loop returned inside the interval; third click cleared. Waveform shown then hidden. Timing is root-observed action evidence. |
| N05 Sidebar | PASS | Save applied left dock, font 22, padding 9 and future mask; `fox` filter found 1/3; selecting a masked cue sought 3.5 s/revealed it (`04-sidebar-mask-left.png`). |
| N06 themes | PASS_WITH_MINOR | Dark/Light controls and text readable. Existing heavy black Light-sidebar shadow is P3/Minor (`05-light-sidebar.png`), deferred below. |
| N07 numeric input | PASS | Native inner-TextBox UIA edit/focus plus pointer Save retained forward `1.5`/`1001` and concurrency `0`, showed error/kept window open. Corrected values persisted as 1/1000/64 (`02-numeric-error-screenshot-1.png`). Ordinary physical typing into the owned dialog was not established. |
| N08 dual overlays | PASS | Both slots readable with primary yellow/secondary cyan, outline 5/1, navy outline, background `#102040` opacity 80/40 and bottom 60; values independently persisted (`06-dual-colors-background.png`). |
| N09 fonts | PASS_RENDER_ONLY | While app closed, isolated profile seeded primary Segoe UI/Bold/Italic and secondary Consolas/Normal/Normal, inheritance off. Primary style rendered; Settings read both tuples. GUI ComboBox editing and rendering the seeded secondary tuple were not established; initial dual-slot render used shared Inter inheritance. |
| N10 deck draft/ownership | PASS | Tools→Word Manager discarded unsaved default-deck draft; manager opened with `LLPlayer Visual Fixture`. Session deck B affected export; manager reopen restored default (`16-words-reopen-screenshot-1.png`). |
| N11 word edits/metadata | PASS | `zorbula` Reading/Translation edits survived restart/reopen; Term/metadata read-only; Source Word click/language en, saved 12:00 UTC shown 15:00 MSK (`20-word-metadata-screenshot-1.png`). |
| N12 clear safeguard | PASS_NONDESTRUCTIVE | Filter `flarnix` showed 1/3; first Clear armed explicit all-words warning without deletion; filter change disarmed. Destructive second confirmation NOT_RUN (`08-clear-armed-screenshot-1.png`). |
| N13 exports | PASS_DEFAULT_PATH | Native TSV/APKG exports at actual default filenames verified as three rows/notes/cards, APKG session deck B and five vocabulary fields. Requested custom filename/path NOT_ESTABLISHED; UIA displayed text did not commit in picker. No product path defect established. |
| N14 basic shortcuts | PASS | F1 showed 43 shortcuts; `Visibility` filter 1/43 H/V. Main H toggled overlay, Ctrl+B sidebar (`09-shortcuts-screenshot-1.png`). |
| N15 engine disable | PASS | Native Enabled checkbox disabled H, Save; H inert, V worked, filtered sheet showed only V (`18-sheet-disabled-h-screenshot-1.png`). |
| N16 reset/live sheet | PASS | Reset engine defaults + Save restored H/V in the same open sheet window 461, retaining `Visibility`/1-of-43; Main H worked (`19-sheet-reset-live-screenshot-1.png`). |
| N17 capture/collision/key phase | NOT_RUN | Native capture/rebind, app↔engine collision editing and separate held-key/key-up phase unestablished: keyboard automation activated Main before input to owned windows. Deterministic headless/adapter regressions remain separate evidence. |
| N18 restart | PASS | Light, Sidebar values, 1/1000/64, word edits/default deck restored. Translation slots started Off; primary was explicitly enabled later. Readbacks identify each phase. |
| N19 original/translated text | PASS_MOCK_ONLY | Loopback returned three known Russian cue translations; Sidebar translated, then native Original checkbox Save showed English while overlay stayed Russian (`12-original-sidebar-translated-overlay.png`). Real provider NOT_RUN. |
| N20 word clipboard | PASS_SEEDED_MODE | Clipboard mode seeded only while isolated app closed; native `world` click copied `world`, no popup; value pasted into own Main search (`13-native-clipboard-word.png`). GUI mode ComboBox editing NOT_RUN. |
| N21 cue clipboard | PASS_SEEDED_MODE | ClipboardAll likewise seeded; click copied full English cue, no popup; pasted into own search (`14-native-clipboard-all.png`). |
| N22 translation popup/save | PASS_MOCK_SEEDED_MODE | Translation mode seeded; native `world`→`мир` mock popup and Save created a fourth synthetic word; manager reopened (`15-word-popup.png`). |
| N23 minimum size/DPI | NOT_ESTABLISHED | Two corner-resize attempts left 1282×792 unchanged. `10-narrow.png` is a failed resize attempt, not minimum-size evidence. Narrow dialogs/high-DPI/native Linux NOT_RUN. |
| N24 external subtitles | NOT_RUN | New external RU SRT selection not completed; existing English file selected as secondary and two independent overlays rendered. |
| N25 external/runtime acceptance | NOT_RUN | Real providers/models, ASR/OCR, GPU, audible output, AnkiConnect and native Linux not exercised; full desktop/manual-smoke parity remains open. |

## Export, final state and cleanup readbacks

The actual picker outputs were `artifacts/windows-avalonia-test-media/LLPlayer.tsv` (657 bytes, SHA-256
`4e98d6cc502c39545d6566c044ce69e0804d0a1c677fb7d4132625304c7b7792`) and `LLPlayer.apkg` (2,873 bytes,
SHA-256 `d328f786bfaaf76a0ef31bc2b96ae4906522a6bfba758125c6e0317b436764ab`). Read-only copies are under
`visual/exports/`. APKG ZIP CRC and in-memory/query-only SQLite integrity passed: three notes/cards, model
`LLPlayer`, all cards on `Visual session deck B`. TSV/APKG contain Term/Reading/Translation/Definition/Example;
source/language/timestamp provenance was verified in WordList, not exported in that five-field schema.
Anki application import/AnkiConnect delivery were NOT_RUN.

The earlier three-note exports remain unchanged. The later final profile has four words, including native
popup-saved `world`→`мир` at `2026-10-26T16:01:34.6251262Z`, the original three synthetic records and persisted
`zorbula` edits. Default deck is `LLPlayer Visual Fixture`; unsaved draft never persisted; Original is true,
WordClickAction is Translation, `EngineKeysCustomized=false`/stored bindings empty means engine defaults restored.
Font tuples/modes/provider selections seeded while the isolated app was closed are setup evidence, not GUI editor acceptance.

Only owned native instances were closed through UI; final inventory contained no LLPlayer windows. The owned
loopback mock had four requests/four successes/zero unknown, token-protected shutdown HTTP 200, exit 0 within
five seconds and final stopped state; no forced kill, other-process change or external request. The isolated
profile keeps the loopback endpoint as an audit artifact; this does not configure the real user profile.

## Findings and remaining boundaries

Independent visual reviews reported **0 Critical/Important**. P3/Minor: unconditional black text shadow makes the
Light Sidebar heavy/doubled while readable (`SubtitleText.cs:229`, `MainWindow.axaml:461`). Defer a separate bounded
Sidebar styling/opt-out proposal; do not change overlay shadow defaults or implement it in this correction.
An earlier transient Opening/paint observation did not reproduce as a hang; later frames/cues/native modules do
not establish its cause, which remains UNKNOWN.

Native chord capture/rebind/collision/held-key phase, physical dialog typing/ComboBox choice, custom export path,
minimum-size/high-DPI, external RU SRT selection, destructive clear and N25 remain open as listed above.
The new audit does not overwrite earlier historical NOT_RUN/FAIL/UNKNOWN states or replace the local full gates.
At this candidate checkpoint corrective PR CI, exact merged-main CI, final merged-main Desktop refresh and vault
closeout are **NOT_RUN**. Resolve later results from actual GitHub/main, final Desktop sourceCommit/readback and
the live vault handoff; candidate visual results do not automatically accept that later payload.
