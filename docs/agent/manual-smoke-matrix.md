# Manual Smoke Matrix

Automated tests do not cover every LLPlayer behavior. Use these manual checks when touching related areas.

## Playback

- Open a local video file.
- Open media from a command-line path/URL.
- Play, pause, seek, stop, change volume, fullscreen, and close.
- Confirm taskbar progress and play/pause thumbnail action update.
- A-B repeat (F-12): during playback click the A-B button (repeat icon) to set A, then B; confirm playback loops between them, the seek bar shows the A/B markers + highlighted band, and the button icon lights. Click again to clear and confirm normal playback resumes. Optionally bind Set A / Set B / Clear in Settings ▸ Keys and verify the shortcuts; confirm the points reset when a new file is opened.
- Seek-bar waveform (F-12, since 0.3.28): open a local video/audio file, click the Waveform toggle in the player bar; confirm an amplitude envelope appears behind the seek track after a brief background build (it should track loud/quiet sections), playback is not blocked while it builds, and the toggle lights when on. Toggle off → the envelope disappears. Open another file → the waveform rebuilds for the new file (no stale envelope). Confirm a file with no audio / a live stream shows no waveform and does not error.

## Subtitles

- Load embedded text subtitles.
- Load external text subtitles.
- Load bitmap subtitles when relevant.
- Enable primary and secondary subtitles together.
- Use subtitle seek, sidebar current/previous/next behavior, and sidebar search.
- Confirm overlay subtitle placement, separator, sizing, and bitmap positioning.
- Place a sidecar subtitle file beside a video (e.g. `movie.ru.srt`, `movie.en.ass`), open the video, and confirm it is auto-detected and appears under External files with its language/source.
- Open the right-click Subtitles ▸ Subtitle Tracks menu and confirm BOTH Primary (1st) and Secondary (2nd) submenus list Embedded / External files / ASR; assign tracks to each slot.
- Open the sidebar "Subtitle tracks" quick switcher and confirm it lists embedded + external + ASR with language/source; assign ① primary / ② secondary and use per-slot Off; confirm the checked state follows the active selection.

## Sidebar And Word Actions

- Toggle sidebar, move it left/right, resize it.
- Use spoiler mask and original/translated toggles.
- Left-click a subtitle word and confirm word lookup pauses playback and opens/copies according to settings.
- Left-drag across subtitle words and confirm phrase lookup, including reverse-direction selection.
- Middle-click subtitle text and confirm sentence lookup.
- Right-click a subtitle word and confirm configured word actions/search/copy menu.
- Use the configured last-search modifier and confirm it opens the previous search action.
- Resume playback and confirm open word popups close.
- **HC-22 owner acceptance — ⚪ WAIVED / NOT RUN (explicit owner decision, 2026-07-20):** the expanded
  observable-provider/UI scenarios below were not executed and are not `PASS`. They remain the standard protocol
  for removing the residual risk in the future. Overall: `ACCEPTED WITH OWNER WAIVER`; task status:
  **DONE WITH RESIDUAL RISK**.
- HC-22 precondition: choose distinct source and target languages, enable an observable local translation provider and `Definition Source = LLM`, clear their request logs, and use unique uncached words. Record separate translation and definition request counts for overlay and sidebar; focus a text field inside the popup before pressing Esc.
- HC-22 live-instance cache: in each host, look up one word and record one translation/definition request. Dismiss it separately through Close, Esc, and resumed playback. Reopen the same word after every dismissal; the displayed result must return without either request count increasing.
- HC-22 host teardown: start slow translation and definition requests for a fresh sidebar word, then hide the sidebar before either completes. No old popup/result, cancellation snackbar, global dispatcher error, or stuck spinner may appear. Show the sidebar, retry the same word, and require both provider request counts to increase: the new host must perform a fresh lookup rather than inherit the discarded instance's cache.
- HC-22 supersession and snapshots: start slow lookup A and immediately start lookup B. Only B may control source/translation/definition text, spinner, cache, Save payload, and notifications. While A is still pending, right-click word C and use a configured menu action; that menu must use C while the popup and Save payload remain B.
- HC-22 settings invalidation: during separate slow uncached lookups, change the translation provider, source language, target language, and definition provider one at a time. Each change must close/reset the old popup. Retry the same word and require a new request under the changed configuration; only that new result may appear or be saved.
- HC-22 window-root stress: toggle the sidebar 20 times. After every cycle, open a popup, move/deactivate/reactivate the main window, and confirm only the current popup follows it and receives one mouse action. The automated `NonTopmostPopupLifetimeTests` WeakReference control separately proves that omitting `Unloaded` keeps a popup rooted by `MainWindow`, while the real unload path permits collection.
- HC-22 overlay retention: cache an overlay lookup, then round-trip fullscreen and batch-enabled tray hide/restore. These operations retain the same host; the cached lookup and normal Close/Esc behavior must still work without a new provider request.
- HC-22 PDIC ownership: invoke PDIC from both hosts and record the spawned process ID. Both hosts and a sidebar toggle must keep using that one process. After Exit App, that exact process ID must be gone; popup/host teardown must not terminate it earlier.
- Word Manager (F-10): left-click a subtitle word, click the popup's Save (bookmark) button, and confirm a "Saved" snackbar. Open right-click Subtitles ▸ Word Manager and confirm the word appears; edit its Translation/Definition in place and confirm the edit persists across a dialog reopen/restart. In AI Insights, generate vocabulary and click "Add to List", then confirm the words appear in the Word Manager (duplicates by term are skipped). Export TSV and import it into Anki (File ▸ Import). Export `.apkg` and double-click it to import into Anki; confirm the deck/cards appear with the five fields. With Anki running and the AnkiConnect add-on installed, click "Push to Anki" and confirm cards are added; with Anki closed, confirm a clear "could not reach Anki" message rather than a crash.
- Word definitions (F-11): in Settings ▸ Subtitles ▸ Word Action, set Definition Source to `Auto`. Left-click an English subtitle word and confirm a definition row appears below the translation; click the popup's Save and confirm the saved word's Reading/Definition are filled in the Word Manager. Click a word the dictionary lacks (e.g. a proper noun) and confirm the popup still shows the translation with no error (the definition row just stays hidden). With a non-English source language and an LLM configured, confirm the definition appears in the target language; with no LLM configured, confirm a non-English word shows only the translation. Set Definition Source back to `Off` and confirm the popup is unchanged (no definition row).
- Sidebar language badge (T-10 follow-up, since 0.3.38): with default config (Detect Language Per Segment OFF), run interactive ASR and confirm NO language badge appears on any sidebar row (byte-identical default). Turn Settings ▸ Subtitles ▸ "Detect Language Per Segment" ON and confirm badges (lower-case language codes, e.g. `en`/`ru`; a language without an ISO 639-1 code shows its 3-letter code, e.g. `haw`/`yue` — not a bug) appear on the already-transcribed ASR rows immediately — without reopening the video — and that hovering a badge shows the full language name. Turn it back OFF and confirm all badges disappear immediately. Load an external `.srt` and confirm its rows never show a badge at any toggle state.

## Translation

- Verify target language selection.
- Smoke the touched provider only.
- For LLM-like providers, check context-aware sequential behavior if changed.
- F-13 Avalonia chat-settings native desktop acceptance (not run): in the installed Linux app, edit each translation mode and prompt plus grammar prompt/toggle; verify context counts and KeepContext count/retention policy, including zero, and target-region toggle persist. Confirm malformed/negative count text is rejected on Save; reset exposed chat defaults and confirm only chat values reset while provider selection, credentials, and target remain unchanged. Save & Close applies the persisted draft; Close discards edits. Confirm loading/saving causes no provider health/probe request. Headless tests/render evidence do not establish native desktop or live-provider acceptance.

## ASR/OCR

- Download/select model or engine through existing dialogs when changing download/settings code.
- Linux F-13 Recognition Advanced: expand the collapsed section and confirm the six whisper.cpp CPU fields; edit values, then Close/Esc and confirm current engine settings remain unchanged. Save blank numeric fields and confirm library defaults; try malformed, zero, negative, and overflowing input and confirm Save is blocked with a visible error and no old value is silently retained. Load a legacy file with zero/negative values and confirm the numeric controls display blank while persisted/source/live config remains unchanged; explicitly Save and confirm blank becomes `null`. Reset Advanced and confirm the detached draft resets only the six fields, preserving model/directory/language/translate/OCR/runtime/GPU/thresholds/temperatures/audio context; Close/Esc must discard it. Verify active recognition or subtitle loading blocks Save, media generation invalidates queued work, and no active ASR is restarted. Focused tests, Linux `--fast`, headless screenshots, scoped reviews, and final independent verification passed with zero Critical/Important/Minor findings. Package coherence and independent package/installer reviews passed; installation/readback passed with stable `current`, payload, launchers and XDG state verified. Native GUI acceptance remains pending; Windows is deferred.
- Start ASR and cancel it.
- ASR pause/resume (F-04): start interactive ASR on a longer video, left-click the player-bar ASR chip to pause — confirm new subtitles stop appearing within ~one chunk, the already-generated subtitles REMAIN, and the chip icon switches to the resume (play) state; click again to resume and confirm transcription continues from where it left off. Verify pausing then seeking (which restarts ASR) does not leave the new run stuck paused, and that disabling ASR while paused clears as before. Smoke both whisper.cpp and faster-whisper, and dual (primary+secondary) ASR.
- T-10 per-segment language smoke: use mixed-language media with auto-detect ASR. With `Detect Language Per Segment` OFF, confirm language stays pinned after the first non-empty segment/chunk; with it ON, confirm later segments/chunks can switch language. Smoke both interactive ASR and Batch Subtitles when the engines are available.
- F-19 word timing smoke: with faster-whisper interactive ASR and re-segmentation enabled, toggle `Word-Level Cue Timing` ON and confirm split cue boundaries follow real speech pace instead of flashing on dense speech; toggle it OFF and confirm timing falls back to proportional cue splitting. Missing/mismatched JSON must not crash ASR.
- F-19 VAD snapping smoke: with `Assets\silero_vad.onnx` present, run interactive ASR on speech with audible pauses/music/noise; toggle `Snap Cue Timing to Speech` ON and confirm split cue changes move to nearby pauses where possible, then OFF and confirm the word/proportional timing path remains. Smoke both faster-whisper and whisper.cpp for the VAD toggle; batch and loaded subtitles should not run VAD.
- Run OCR on bitmap subtitles and cancel it.
- Linux F-13 engine OCR (partial native verification): retained-process smoke passed 23 checks (0 skipped), including
  real English/Russian multiline recognition, `Process`/`Do`, two-track ownership/reuse, secondary reinitialization,
  timing/native-bitmap-free checks, and deterministic cancellation between cues. The receipt and synthetic bitmap
  fixtures/results are under `artifacts/f13-ocr-2026-10-02/`. An earlier unload/reload run exited 139; do not infer
  the exact native teardown mechanism. Full Linux gate passed (1534 engine tests, 5 opt-in OCR tests skipped there but
  passed separately in the native run, 138 Avalonia tests, 53 shell tests, validators and WPF/plugin cross-compile).
  The published package passed content/SHA checks and a console probe using its engine assemblies, with the package
  app base and cwd under `/tmp`; it recognized English and Russian samples (three cues each), without bundled OCR
  models/overlay dependencies. This was not the Avalonia GUI. Receipts: `final-full-gate.exit`,
  `package-verification.json`, and `package-native-ocr.json` in `artifacts/f13-ocr-2026-10-02/`. Source and artifact
  verification review passed with zero Critical/Important findings. Real-media-file-to-overlay/UI and Windows
  runtime/owner smoke remain pending.
- For Whisper/ASR native-runtime issues, check Microsoft Visual C++ Redistributable 2022 or newer as a troubleshooting prerequisite.
- VC++ preflight (T-02): on a machine WITHOUT the x64 VC++ Redistributable, enable whisper.cpp ASR and confirm a non-blocking "INSTALL" snackbar opens the Microsoft download page instead of the app crashing; enable Tesseract OCR and confirm a clear "requires the Microsoft Visual C++ Redistributable" message (with the download URL) appears instead of a crash. Confirm faster-whisper ASR and Microsoft OCR are NOT gated by this check, and that with the runtime installed both ASR and OCR start normally.

## Batch Subtitles

- Open the batch subtitles dialog from the sidebar and from the context menu.
- Scan a local folder with multiple videos; repeat with recursive scan enabled.
- Run batch with whisper.cpp and faster-whisper on real media; confirm `video.ru.srt` is saved beside each completed video.
- Smoke a local LLM provider such as Ollama, LM Studio, KoboldCpp, or LiteLLM; confirm LLM keep-context translation stays ordered within each file.
- Cancel while ASR is running and while translation is running; confirm completed outputs remain and pending files do not start.
- Include a no-audio video and confirm it fails in the queue while later files continue.
- Open a generated `video.ru.srt` in LLPlayer and confirm playback/sidebar/subtitle UI still works.
- Restart LLPlayer and confirm batch last folder, recursive scan, and overwrite policy are restored.
- Start a batch run, then CLOSE the main video window: confirm the app does not quit, a tray icon appears showing overall progress, the batch keeps running, and `video.ru.srt` files still appear; double-click the tray icon (or pick Open) to bring the player back.
- Start a batch run, then close the BATCH window itself (X): confirm it does NOT prompt to cancel — instead the window hides to the tray and the run continues; reopen it via the tray "Batch subtitles…" menu and confirm progress advanced. With the batch idle (not running), closing the batch window closes it normally.
- With the player minimized to the tray, double-click a video row in the batch list and confirm the player reopens and plays that file (with its `video.ru.srt` picked up).
- Pick Quit from the tray (or App ▸ Exit App) while a batch is running and confirm the app exits without a close prompt and the tray icon is removed.
- Confirm the batch window's own taskbar button shows overall progress while running.
- While a file transcribes, confirm the live timing updates every second (the active file's "elapsed" ticks up and an approximate "~MM:SS left" ETA shows in the row + transcript pane; the summary shows a rough overall "~MM:SS left") — visibly moving even between ASR segments, including on the slower CPU fallback.
- With "Smooth (no ASR/translate overlap)" on, run a batch and confirm ASR and translation never run at the same time (status moves file-by-file: RunningASR → QueuedForTranslation → Translating → Saving → Completed → next file's RunningASR), and outputs are still correct.
- With faster-whisper (CUDA) and "Run ASR on CPU while I work" on, start a run and keep using the mouse/keyboard: confirm the next chunk runs on CPU (GPU frees; the dialog summary shows "ASR on CPU (you're active)" and the tray tooltip shows a "CPU …%" marker) while progress continues, and that it switches back to GPU after the idle threshold; the chunk in flight finishes on its current device, and the transcript/output is unaffected by the switch.

## Config

- Open Settings, change a setting, use `Close`, restart, confirm it was not persisted.
- Open Settings, change a setting, use `Save & Close`, restart, confirm it was persisted.
- Check key binding edit/apply/load workflows when shortcut code changes.
- In Settings Keys, add/clone/delete a row, capture a key, commit with Enter, create and clear a duplicate, confirm Apply is blocked only while duplicates exist.
- Open CheatSheet with F1, switch Keyboard/Mouse tabs, search by shortcut/description, and execute an action button.
- On a fresh config (no `LLPlayer.Config.json`), confirm the Win11 Mica backdrop is on by default (Settings ▸ Themes shows it checked); toggle it off, `Save & Close`, restart, and confirm it stays off (the migration does not re-enable a saved-off value). On Windows 10 confirm the app still launches normally (Mica no-ops).
- With `Theme.ShowM3Theme` off (default, opt-in from T-05 v0.3.29), confirm the app shows the stock MaterialDesign2 look; enable Settings ▸ Themes ▸ the M3 theme toggle, `Save & Close`, restart, and confirm the M3 overlay is applied and persists. Toggle it back off and confirm the look returns to byte-identical stock (no residual M3 primary-container accent, including under the light theme).

## Dubbing

- First-run provisioning UX: confirm the user opts in before local TTS engine/model setup and no model weights are committed.
- Start a dubbing batch on real media and confirm ASR -> translate -> save SRT -> dub -> completed runs serially when `GenerateDubbing=true`.
- Run a multi-file dubbing batch and confirm the local sidecar/model is started once for the run, then stopped at the end.
- Kill/cancel during dubbing and confirm no orphan Python process remains, VRAM is released, and no partial `video.ru.dub.*` output remains.
- Open a video with an existing non-empty `video.ru.dub.flac`; confirm it appears under Audio ▸ External and plays in sync at 0:00, mid-video, and near the end.
- Re-run batch with an existing `video.ru.srt` but no `video.ru.dub.*`; confirm the default run renders the dub from the existing SRT without re-running ASR/translation.
- On an open local video, assign a sidebar per-line dub voice on one subtitle row, then run a dubbing batch for that same file and confirm the generated dub uses the row voice for that line while unassigned lines use the default voice. Repeat with an existing `video.ru.srt` and no `video.ru.dub.*` to confirm the current-session assignment also reaches the SRT-only render path. With `Subtitles.PersistPerLineVoices` OFF (default), restart the app and confirm the assignment is not persisted. Then enable Settings ▸ Subtitles ▸ Dubbing ▸ "Persist per-line voices", click **Save & Close**, re-assign a voice, confirm `video.ru.voices.json` appears, restart, reopen the same video, and confirm the sidebar restores the voice and a fresh dub uses it.
- **HC-27b detailed owner protocol:** use [`hc-27b-owner-smoke.md`](hc-27b-owner-smoke.md) for exact preparation, UI paths, companion inspection, expected results, and the PASS/FAIL report template. A row icon that remains Primary/full-opacity at rest, after mouse hover and keyboard focus move elsewhere, indicates an override; inspect JSON to confirm the exact voice id.
- **HC-27b owner acceptance — A/B isolation — ✅ PASS (owner, 2026-07-19):** with persistence ON, assign a voice on local media A, immediately open local media B and assign a different voice. Wait for background writes to settle. Confirm both `A.ru.voices.json` and `B.ru.voices.json` contain only their own final assignments, then reopen both media and confirm the matching voices are restored.
- **HC-27b owner acceptance — latest-wins — ✅ PASS (owner, 2026-07-19):** on one media, rapidly choose an old and then a new voice. Wait for writes to settle, confirm the companion contains the newest state, reopen the media, and confirm the newest voice is restored.
- **HC-27b owner acceptance — OFF — ✅ PASS (owner, 2026-07-19):** while persistence is still ON, wait for any current companion write to settle and record the stable file's timestamp/content (or its absence). Turn `PersistPerLineVoices` OFF, click **Save & Close**, make another per-line assignment, wait for background work to settle, and confirm the companion was not created or changed while OFF; media and subtitle files remain unchanged. The queued-behind-save race itself is covered by a deterministic test.
- **HC-27b owner acceptance — clear — ✅ PASS (owner, 2026-07-19):** with persistence ON and a pending/previous assignment, choose "Use default voice" for every overridden line. Wait for writes to settle; confirm the expected companion is removed and does not reappear, then reopen the media and confirm defaults are restored.
- **HC-27b owner acceptance — exit/restart — ✅ PASS (owner, 2026-07-19):** change an assignment and immediately choose **Exit App** from the application menu (or **Quit** from the tray if the app is there). Restart, reopen the media, and confirm the last allowed state is restored and no other media companion was touched. The internal Dispose-wait guarantee is covered by deterministic tests, not this visual check.
- **HC-27b owner acceptance — responsiveness — ⚪ WAIVED / NOT RUN (explicit owner decision, 2026-07-19):** the standard protocol remains: on a writable slow/network-backed media path with a large subtitle list, repeat assignment/clear, confirm the UI remains interactive without a visible pause, then confirm the companion was updated successfully and the final assignment is restored after reopening the media. This waiver is not a PASS. On exact ProductVersion `0.3.61+66113e343537919c0e3e5208af2467264228c330`, the accepted closeout is **5/5 executed local scenarios PASS; Overall: ACCEPTED WITH OWNER WAIVER; DONE WITH RESIDUAL RISK**. Residual risk: live WPF responsiveness under slow SMB/UNC latency and 5000+ cues was not observed; reopen HC-27b or create a performance follow-up if real network use shows input pauses or `Not Responding`.
- Ear-test CosyVoice2 Russian on real content: voice is Russian, ducking is audible, and the original audio remains present.
- Launch the published `.exe` on the target RTX 5090 machine with `GenerateDubbing=false`, then run one mock/real dubbing smoke if the local engine is provisioned.

## GitHub Merge Governance

- **T-13d required-check acceptance — ✅ PASS (2026-07-19):** active ruleset `Test` (`17995732`, version
  `43588410`) targets `~DEFAULT_BRANCH`, preserves deletion/non-fast-forward protection, has no bypass actors, and
  requires exact context `LLPlayer Build & Test` from GitHub Actions integration `15368` with strict policy enabled.
  On exact feature head `df23af3`, [run 29695174848](https://github.com/Gorgutc/LLPlayer_ru/actions/runs/29695174848)
  passed fast/restore/app/plugin/test and [PR #161](https://github.com/Gorgutc/LLPlayer_ru/pull/161) became `CLEAN`.
  Intentional-red [PR #162](https://github.com/Gorgutc/LLPlayer_ru/pull/162) was `BLOCKED` while its required check
  was still pending and remained `BLOCKED` after [run 29695396710](https://github.com/Gorgutc/LLPlayer_ru/actions/runs/29695396710) failed the expected
  `plugin.json name must be llplayer-codex` marker in the fast gate; restore/build/test were skipped. The PR was
  closed without merge and its temporary branch was removed.

## Packaging

- Run `scripts/codex/ship.ps1`.
- Confirm publish output contains `LLPlayer.exe`, `lib/7z.dll`, all copied `FFmpeg/*.dll`, `Plugins/YoutubeDL/YoutubeDL.dll`, and `YoutubeDL.pdb`.
- Confirm publish output contains committed dubbing sidecar source (`dub_sidecar/server.py`, `pyproject.toml`, `uv.lock`, `README.md`) and does not contain dubbing runtime/model/output artifacts (`DubEngine`, `dubmodels`, `*.ru.dub.*`, `*.ru.voices.json`).
- Do not require network download of `yt-dlp.exe` for local smoke unless explicitly shipping a release.
- **T-13e Testing controlled run — ✅ DONE (2026-07-19):** owner-approved [run 29680768395](https://github.com/Gorgutc/LLPlayer_ru/actions/runs/29680768395) bound remote `main`, workflow control, dispatch input, run `head_sha` and selected commit to exact `66113e343537919c0e3e5208af2467264228c330`. All four jobs succeeded with .NET SDK 10.0.302, full `verify.ps1` (**1380/1380**), `7z t`, and yt-dlp `2026.07.04` (`18226085` bytes, SHA-256 `52fe3c26dcf71fbdc85b528589020bb0b8e383155cfa81b64dd447bbe35e24b8`). Direct tag `testing-66113e343537` maps to that commit; draft prerelease `356299722` remains unpublished with exactly `LLPlayer-testing-66113e343537-x64.7z` (`218743845` bytes, SHA-256 `56e6efaa5675b27022972533bf611e73c45fc60db754f949b7e14d1d10a48fb6`). Remote digest and a local control download matched. The earlier partial M2 tag/draft remains unchanged and assetless as audit evidence. The run executed the real `gh release upload --clobber` command and all post-upload readbacks on a new exact tag. A duplicate run that would replace an already existing same-tag asset was not part of this controlled-run DoD and was not executed; future reruns remain separately owner-gated, while their exact-tag/exact-SHA/draft-only constraints remain validator-covered.
- **T-13e Stable controlled run — ✅ DONE, DRAFT ONLY (2026-07-19):** only after the successful Testing readback, owner-approved [run 29681166913](https://github.com/Gorgutc/LLPlayer_ru/actions/runs/29681166913) used the same exact commit and `v0.3.61`. All four jobs succeeded with a fresh .NET 10 full preflight, **1380/1380**, package verification, `7z t`, and the same yt-dlp evidence. Direct tag `v0.3.61` maps to exact M3; Release `356301697` remains `draft=true`, `prerelease=false`, `published_at=null` with exactly `LLPlayer-v0.3.61-x64.7z` (`218743831` bytes, SHA-256 `aa64902c57f52b48d8577ceb9002bedffac49ad9e8819c97acd8ffaab9076014`). Remote digest and a local control download matched. The Stable draft was not published; publication remains a separate owner-only action.

## Shared Windows/Linux Avalonia desktop interactions (F-13)

For a future owner-resumed native run, use the rebuilt package and an isolated test profile on each platform.
Record package identity, platform and actual results separately; headless tests and an older package do not
close these rows. The current rebuilt-package native recheck is deferred by owner; do not auto-launch it.

- Open the Media menu and the native file picker; cancel safely, then open local media with Unicode and spaces
  in its path. Confirm video, duration and primary cues. Exercise explicit subtitle loading for supported text
  formats including `.txt`, malformed/unsupported input, rapid media replacement and primary/secondary slots.
- Seek from each subtitle slot with its own delay and fractional cue timing; toggle subtitle visibility and
  confirm text and bitmap overlays follow it. Late subtitle completion must not affect replacement media.
- Open Settings from the gear/menu; Close/Esc discard the draft. Invalid or out-of-range raw numeric input
  must block Save with a visible error and preserve live/persisted preferences; valid Save must persist before
  applying changes and preserve unrelated preferences. Reopen to verify the saved values.
- Enter fullscreen with F and return with Esc; confirm normal-window restoration. Resize the window and
  inspect transport controls and sidebar bounds without hidden or overlapping controls.
- **Persisted text-subtitle position, owner-approved 2026-10-18 slice — NOT_RUN / DEFERRED BY OWNER:** on each
  native Windows/Linux host, use an isolated profile and confirm a missing `SubtitleBottomOffset` starts at
  24 DIP. Edit the Settings text-subtitle bottom offset, Save & Close, reopen Settings and restart the app;
  confirm the saved base offset is retained. Exercise existing Up/Down commands in 8-DIP steps, including
  clamping at 0 and 400, then restart and confirm the command-updated preference survives the normal save path.
  Close/Esc must discard draft edits. Invalid/out-of-range input must block Save; a persistence failure must
  retain live/persisted values and keep the dialog open for retry. Check fractional input under the host's
  number format, minimum-window usability, and 100%/150%/200% DPI. Toggle fullscreen and transport controls;
  confirm clearance is transient and reopening/restarting retains only the base offset. Confirm primary and
  secondary text move together while bitmap positioning is unaffected. Native GUI/DPI/fullscreen checks remain
  deferred under the owner's no-Computer-Use instruction; focused headless results do not close this row.
- **Text-subtitle outline thickness, separate owner-approved 2026-10-18 slice — NOT_RUN / DEFERRED BY OWNER:**
  on each native Windows/Linux host, use the new package and an isolated profile. Missing primary/secondary
  keys must retain the existing 3/2.5-DIP widths. Edit each Settings outline value independently, Save & Close,
  reopen Settings and restart; confirm both persisted widths return on their own text-overlay slots. Check
  fractional input under the host's number format, 0 and 8 DIP, and rejection of malformed, non-finite or
  out-of-range raw input. Close/Esc must discard both draft edits; a persistence failure must preserve live and
  persisted values and keep Settings open for retry. At zero the black outline is off and the existing shadow
  remains; its pen width still derives from the selected outline, so do not expect an unchanged shadow footprint.
  Inspect long primary/secondary cues, word clicks, minimum-window usability and 100%/150%/200% DPI after changing
  widths, including a fractional 3 → 3.1-DIP change. Confirm bitmap subtitles, sidebar styling and WPF settings
  remain outside this slice. Focused regressions and local WPF/Windows Avalonia gates passed separately; this is
  a future new-package GUI/DPI acceptance row, which those source tests and prior package receipts do not close.
- **Text-subtitle foreground colors, separate owner-approved 2026-10-18 slice — NOT_RUN / DEFERRED BY OWNER:**
  on each native Windows/Linux host, use the new package and an isolated profile. Missing primary/secondary
  color keys must retain opaque `#FFFFFF`/`#E8E8E8`. Edit each Settings HEX field independently and inspect its
  adjacent swatch; Save & Close, reopen Settings and restart, then confirm both colors return on their own text
  overlay slots. Confirm outer whitespace is trimmed and lowercase six-digit HEX becomes uppercase. Reject
  names, short HEX, alpha forms, non-HEX digits and other malformed input with a visible error and no Save.
  Check independent fallback for null/invalid stored strings without changing a valid other slot; malformed JSON
  and wrong JSON types must retain their existing whole-file behavior. Close/Esc must discard draft edits;
  a persistence failure must preserve live/persisted values and keep Settings open for retry. Confirm unrelated
  volume/mute/recents survive Save. Inspect primary/secondary text, long cues, word clicks, minimum-window
  usability and 100%/150%/200% DPI; only the text `Fill` changes, retaining outline/shadow, positioning, bitmap
  subtitles and sidebar styling. WPF settings, font/alpha controls and full F-13 parity remain outside this slice.
  This is a future new-package native GUI/DPI acceptance row; headless source/render results and prior package
  receipts do not close it. No Computer Use or application launch is authorized for this stage.
- **Common text-subtitle outline color, separate owner-approved 2026-10-18 slice — NOT_RUN / DEFERRED BY OWNER:**
  on each native Windows/Linux host, use the new package and an isolated profile. A missing `SubtitleOutlineColor`
  must retain opaque black (`#000000`) on both text overlays. Edit the single Settings raw HEX field and inspect
  its adjacent swatch; Save & Close, reopen Settings and restart, then confirm the same saved outline color on
  primary and secondary cues. Confirm outer whitespace is trimmed and lowercase six-digit HEX becomes uppercase.
  Names, short HEX, alpha forms, non-HEX digits and other malformed raw input must visibly block Save. Missing,
  null and invalid stored strings must fall back to black; malformed whole-file JSON and wrong JSON types retain
  their existing policy. Close/Esc must discard the detached draft; a persistence failure must preserve live and
  persisted values and keep Settings open for retry. Save must preserve the other eleven Settings fields and
  unrelated volume/mute/recents. Inspect both overlays, long cues, word clicks, minimum-window usability and
  100%/150%/200% DPI with distinct foreground colors and independent outline thicknesses, including zero.
  Only the common text `Outline` color changes; `Fill`, thickness, shadow, positioning, bitmap/sidebar styling,
  engine/native behavior, WPF settings and dependencies remain outside this slice. This is a future new-package
  native GUI/DPI acceptance row; headless source/render results and prior package receipts do not close it.
  No Computer Use or application launch is authorized for this stage.
- **Text-subtitle font block, owner-approved 2026-10-19 — implemented/local source gates PASS; native acceptance NOT_RUN / DEFERRED BY OWNER:**
  when native acceptance is separately authorized, use a new package and isolated profile on each native Windows/Linux host. Older files
  missing the seven font keys must keep the existing app Inter, `SemiBold` / `Normal`, primary size 34 DIP,
  secondary scale 0.8 and secondary family/weight/style inheritance enabled. For the six family/weight/style
  strings only, check per-field defaults for missing/null/invalid string values, trimmed valid family names, and
  trimmed/canonicalized valid weight/style spellings. A missing `SecondarySubtitleInheritsFont` key defaults true.
  A null/wrongly typed flag or wrong JSON type for any of the six strings must keep the existing malformed-input
  whole-file behavior: load warning, original file retained, best-effort `.bad` copy, and whole-file defaults.
  Confirm no migration of the other twelve Settings fields or WPF settings.
  Select an installed family, each offered weight (`Normal`, `SemiBold`, `Bold`) and style (`Normal`, `Italic`).
  Inspect both draft-preview lines before Save: they must show draft primary and effective secondary without
  changing live overlays or writing preferences. Family lists may differ by OS. A valid unavailable requested
  family and unsupported face must remain visible/stored, including after unrelated Save, while a clear fallback
  status explains effective rendering through the existing app Inter/renderer. Reject arbitrary font URIs/files;
  no font download, asset or dependency is authorized. Native exact-face/glyph acceptance remains separate.
  Check inheritance on/off and retained secondary overrides. Copy Primary must snapshot the requested tuple and
  disable inheritance; later primary edits must not change that copy. Reset Secondary must enable inheritance
  without erasing overrides. Reset Fonts must reset only all seven new fields; size, scale, Fill, outline,
  position and other prior fields must survive all three actions. Save & Close must persist a fresh candidate
  before applying live fonts; reopen/restart must restore the requested values. Close/Esc must discard the draft;
  write failure must preserve live/persisted state and leave Settings open. Preserve concurrent volume/mute/recents.
  With long primary/secondary cues, inspect actual family/weight/style rendering, wrapping, clipping, cached
  geometry and word click/selection hits after warm-cache edits, inherited-primary edits and resize. Compare a
  changed control with a fresh control using the same tuple. Italic needs visible shaping/render evidence with
  the existing bundled Inter/renderer (synthesis permitted); persisted `Italic` alone is insufficient. If a new
  font/package is needed, pause italic acceptance for an owner scope decision. Check minimum-window usability,
  dark/light preview, 100%/150%/200% DPI and fullscreen clearance when native acceptance is separately authorized.
  Sidebar/bitmap subtitles, engine/Core/WPF behavior and the prior twelve Settings fields remain preserved.
  No Computer Use, application launch, native/model/provider run or acceptance is authorized by this row.
  Focused headless regressions observed Inter italic, actual installed-family glyph rendering and routed word hits;
  local WPF/Windows Avalonia and package-contract/publish checks passed at product `37fbe776e556e26ce6e55fb841ebedf7637c8a2a`.
  The locally built ZIP is a pre-product-commit QA artifact, not exact-main delivery. Linux is NOT_RUN locally;
  public candidate/main CI and exact-main delivery/merge remain PENDING at that local checkpoint. WPF `ship.ps1`
  is NOT_RUN and WPF packaging is unchanged. These source/headless/package results do not close this native row;
  see [the font checkpoint](f13-status-2026-10-18.md#bounded-text-subtitle-fonts).
- **2026-10-18 follow-up:** local WPF/Windows Avalonia source gates, focused regressions, package contracts,
  ZIP validation/CRC readback and cumulative independent review passed. Published-source Windows/Linux CI
  is tracked in [PR #169](https://github.com/Gorgutc/LLPlayer_ru/pull/169). On the original main `f71ed59` Windows package, picker cancellation, Unicode/spaces media open
  (video and 24 primary cues), gear Settings/Close draft discard, F/Esc fullscreen restoration and Media menu
  visibility were observed. The rebuilt package was not launched; rebuilt interactions and secondary subtitles
  are **NOT_RUN / DEFERRED BY OWNER**, following the request for no Computer Use while working. No new Linux
  desktop, DPI-transition, hardware-audio listening or ASR/OCR acceptance is claimed.
  Current results: [F-13 status](f13-status-2026-10-18.md).

## Linux (F-13)

- **Avalonia Settings hub (headless/render verification passed; native GUI/owner acceptance pending):** focused tests passed 20/20, covering detached cancel, Save & Close, persistence failure/retry, unrelated preference preservation, secondary-font recomputation, discoverability, singleton modal semantics, all three link routes/draft discard, and numeric bounds. Linux `--fast` with restore passed (FlyleafLib 1593 passed/6 skipped; Avalonia 216 passed/1 skipped; shell tests, portable builds, and validators passed). Fifteen dark/light normal/minimum/error screenshots are hashed in `artifacts/f13-settings-hub-2026-10-07/screenshots-manifest.json`; independent source/UI reviews passed with no Critical, Important, or UI-minor findings, and root visually reviewed seven representative images across the set. This is rendered/headless evidence, not native desktop acceptance. Package archive/content review passed: `LLPlayer-0.3.62-linux-x64.tar.gz`, 180,020,698 bytes, SHA-256 `337a37210ec0bf1897df06b550f7f6cf2e8b47695b06e47997fbdb8711cdbe4b`; staging and source projection checks passed. Installation/readback passed: target `/models/codex-builds/LLPlayer_ru/installed/LLPlayer-0.3.62-linux-x64-settings-337a37210ec0` was current at the Settings Hub checkpoint and was superseded by the Recognition Advanced install recorded in the Linux F-13 smoke entry above; both existing desktop files remained byte/hash unchanged and resolve Exec/Icon through `current`; prior package and all seven critical payload hashes were preserved; three XDG state files were unchanged (`xdg_concurrent_delta={}`). Receipts: `/models/codex-builds/LLPlayer_ru/artifacts/f13-settings-hub-2026-10-07/installation-receipt.json` and `installation-readback.json`. No GUI launch/process stop occurred. Independent final verification review passed with zero Critical/Important findings; native GUI and Windows compile/runtime acceptance remain open; this slice does not complete full Settings parity. Details and logs: [`plans/2026-10-07-f13-settings-hub.md`](plans/2026-10-07-f13-settings-hub.md) and `/models/codex-builds/LLPlayer_ru/artifacts/f13-settings-hub-2026-10-07/`.

Automated coverage: `scripts/linux/verify.sh` (portable engine smoke on the generated clip, headless Avalonia tests,
null audio). These checks cover what the headless suite cannot.

- **Xvfb run (agent-runnable):** `scripts/linux/publish.sh`, unpack the tar.gz, then
  `LLPLAYER_AUDIO_BACKEND=null xvfb-run -a -s "-screen 0 1280x800x24" <pkg>/llplayer ~/.cache/llplayer/media/test-720p.mp4`
  with `LLPLAYER_FFMPEG_DIR` unset. Confirm the window opens, duration reads 0:12 (FFmpeg loaded from the bundled
  `FFmpeg/`), frames play, the cue "Hello world, this is a test." shows at 0.5 s, and playback reaches the end.
  Capture a screenshot (`ffmpeg -f x11grab`) into `~/.cache/llplayer/evidence/`, never into the repository.
- **OpenAL wave backend (agent-runnable, no sound card):** write an `alsoft.conf` with `[general]` `drivers = wave`
  and `[wave]` `file = /tmp/llplayer.wav`, then run the app with `ALSOFT_CONF=<that file>` and
  `LLPLAYER_AUDIO_BACKEND=openal` on the test clip. Confirm the WAV holds a 440 Hz tone for roughly the played
  duration (`ffprobe` duration; `ffmpeg -af astats` shows non-silent RMS), and that pause/seek/volume/mute change it.
- **Direct whisper.cpp CPU inference — ✅ PASS (Linux, 2026-10-02):** `WhisperCppASRService` transcribed the 11-second JFK sample with `large-v3` CPU (`UseGpu=false`, 8 threads) in 10.32 s, producing three coherent English cues at 0–3, 3–8, and 8–11 s. Model size: 3,095,033,483 bytes; SHA-256 `64d182b440b98d5203c4f9bd541544d84c605196c4f7b845dfa11fb23594d1e2`. Receipt: `artifacts/f13-asr-2026-10-02/large-v3-cpu-inference.json`. This is a direct engine-service smoke, not interactive/batch UI acceptance.
- **Avalonia recognition commands and dialog (headless integration, ✅ PASS 2026-10-03):** the headless test created a real Flyleaf `Player`, invoked the Avalonia `MainWindowViewModel` commands with existing `large-v3` CPU weights, observed actual ASR cues, then started a second attempt and paused/canceled it. It also OCR'd real English PGS cues on the primary slot and Russian PGS cues on the secondary slot; the test recorded zero error toasts. Receipt: `artifacts/f13-recognition-2026-10-03/real-ui.log` (2 passed, 0 skipped, 14 s) and `.exit` (0). Rendered dark/light/small and lower-scrolled dialog PNGs were inspected and passed visual review; the lower image includes the intentional failed-save validation message. Independent final verification review passed with zero Critical/Important findings. This is headless command + Player integration, not live desktop or Windows owner acceptance.
- **ASR engine workflows (Linux, partial/pending):** the Avalonia UI path above verifies whisper.cpp CPU, but batch ASR and installed faster-whisper-XXL default/manual paths still need their real transcript/cancellation smokes. Confirm missing model/runtime and a non-executable XXL path produce actionable diagnostics. The direct service inference and package native probe have separate receipts and do not substitute for these workflows.
- **Recognition settings persistence:** in the headless UI, change the model directory/model, ASR language/Auto, and OCR fallback language, then Save & Close and confirm the atomic `LLPlayer.Recognition.json` is reloaded and applied. Reopen and Close to confirm the draft is discarded. Feed malformed JSON and confirm the original bytes remain unchanged, a warning is surfaced, and startup/shutdown does not rewrite it. Attempt Save & Close during recognition/subtitle loading and confirm the operation is blocked; when an ASR slot remains enabled after inference, cancel/clear that slot before retrying save.
- **Word library/dictionary/export (F-13 current Linux slice; final runtime proof pending):** with a safe local subtitle and known source language, confirm dictionary mode defaults Off; enable the configured mode and verify translation and definition complete independently, definition failure does not suppress translation, and Save is disabled until both settle. Compare overlay `Data.IsTranslated` with sidebar `SubtitleData.UseTranslated`; a translated overlay click must save the displayed target-language term with other lookup fields blank, while a same-language source click may still show a definition. Save a word, reopen the app, and verify persisted fields in `$XDG_CONFIG_HOME/LLPlayer/LLPlayer.WordList.json` (or the default `~/.config/LLPlayer/LLPlayer.WordList.json`). Exercise Word Manager filter, read-only Term, immediate edit of Reading/Translation/Definition/Example, delete and clear. In an isolated config directory, use malformed JSON and confirm original bytes remain and mutations stay blocked; no import or recovery UI exists. Export TSV and APKG through their save pickers and validate fields plus APKG ZIP/SQLite contents. Automated AnkiConnect verification uses an injected HTTP fake; only the explicit Send to Anki button can contact localhost. These checks do not imply a live Anki or desktop GUI smoke.
- **Subtitle/sentence translation (F-13 bounded Linux slice; implementation and package verification passed):** the deterministic fake-provider, lifecycle, portable-engine and dialog tests cover the `SentenceProvider` absent-default behavior, independent Word/Sentence choices, session-only slot toggles, cancellation/drain, cache invalidation, stale late-result rejection, media/track replacement, shutdown, provenance and notification coalescing. Load/save performs no provider probe; only session-enabled slots may resume ordinary translation after apply. Dark/light renders passed and the Linux package is installed with a verified launcher readback. Native desktop/Windows owner acceptance remains unrun; no live provider request was part of this slice.
- **Desktop launcher refresh (after every completed reviewed Linux slice):** after the package has passed verification, update the existing launcher to the fresh package. Read back its target and package hash, confirm the previous package and all user XDG data are preserved, and record a deployment receipt. Publishing alone is not proof of launcher update.
- **Native desktop / owner acceptance (pending):** launch the packaged Avalonia app on a Linux desktop, open Recognition, inspect the dialog at actual window sizes/themes, run CPU ASR and selected-bitmap OCR, verify text statuses/errors and menu actions, and verify Close versus Save & Close in the real config directory. Windows Avalonia rendering/audio/package support is implemented; broader interactive and recognition owner acceptance remains separate, with bounded results tracked in the current F-13 status above. Headless rendered PNGs and command integration do not close this owner-smoke boundary.
- **Packaged CPU/runtime compatibility — ✅ PASS (Linux, 2026-10-02):** published-package staging loaded Whisper through the default CPU runtime loader and `AsrVadDetector` loaded the bundled ONNX runtime/model. Positive content validation passed; withholding `libwhisper.so` and adding a CUDA runtime tree each failed with the expected diagnostic; archive SHA validation passed. Runtime baseline is glibc 2.34+, `GLIBCXX_3.4.29`, `libgomp.so.1`, AVX/AVX2/FMA/F16C. Receipts: `artifacts/f13-asr-2026-10-02/final-package-native-smoke.json` and adjacent `final-package-*.log` / `.exit` files.
- **Real desktop (owner):** on a Linux desktop with the .NET 10 runtime and `libopenal1`, install the package as in
  `RUN_INSTRUCTIONS.md`, start it from the application menu (`llplayer.desktop`), and check open file, drag & drop,
  play/pause/seek/stop, volume/mute, fullscreen, dual subtitles, word click popup, and real sound output. Repeat on
  X11 and on a Wayland session (Avalonia runs through XWayland; native Wayland is a later opt-in) and at 100 % and
  200 % scaling.
- **IME (owner):** with fcitx5 or ibus, type Japanese/Chinese/Korean into the app's text fields (search, settings);
  confirm the pre-edit text appears at the caret and commits correctly.
- **Missing prerequisites:** without `libopenal1` the app still plays video silently and logs the fallback; without
  the .NET 10 runtime the launcher fails with the runtime's install hint.
- **Packaging:** `scripts/linux/publish.sh` passes its positive/negative content validation; the tar.gz contains no
  `LLPlayer.*.json`, logs, dub runtime data, or models, `FFmpeg/` holds exactly the seven 8.1 sonames plus
  `LICENSE.txt` and `SOURCE.txt` (build asset + sha256), and `THIRD-PARTY-NOTICES.md` is at the package root.
  F-13 engine slice package SHA-256 (`LLPlayer-0.3.62-linux-x64.tar.gz`, 2026-10-02):
  hash: `artifacts/f13-asr-2026-10-02/package.sha256`; see the evidence bundle for native-load and positive/negative
  content checks.
- **Audio hot-plug (owner, known gap):** unplug the USB/Bluetooth output during playback. Today audio stays silent
  until the file is reopened (the app does not call `AudioEngine.RefreshDevices()`; backlog F-13 parity item 14);
  record the result until that item lands.
