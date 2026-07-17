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
- Word Manager (F-10): left-click a subtitle word, click the popup's Save (bookmark) button, and confirm a "Saved" snackbar. Open right-click Subtitles ▸ Word Manager and confirm the word appears; edit its Translation/Definition in place and confirm the edit persists across a dialog reopen/restart. In AI Insights, generate vocabulary and click "Add to List", then confirm the words appear in the Word Manager (duplicates by term are skipped). Export TSV and import it into Anki (File ▸ Import). Export `.apkg` and double-click it to import into Anki; confirm the deck/cards appear with the five fields. With Anki running and the AnkiConnect add-on installed, click "Push to Anki" and confirm cards are added; with Anki closed, confirm a clear "could not reach Anki" message rather than a crash.
- Word definitions (F-11): in Settings ▸ Subtitles ▸ Word Action, set Definition Source to `Auto`. Left-click an English subtitle word and confirm a definition row appears below the translation; click the popup's Save and confirm the saved word's Reading/Definition are filled in the Word Manager. Click a word the dictionary lacks (e.g. a proper noun) and confirm the popup still shows the translation with no error (the definition row just stays hidden). With a non-English source language and an LLM configured, confirm the definition appears in the target language; with no LLM configured, confirm a non-English word shows only the translation. Set Definition Source back to `Off` and confirm the popup is unchanged (no definition row).
- Sidebar language badge (T-10 follow-up, since 0.3.38): with default config (Detect Language Per Segment OFF), run interactive ASR and confirm NO language badge appears on any sidebar row (byte-identical default). Turn Settings ▸ Subtitles ▸ "Detect Language Per Segment" ON and confirm badges (lower-case language codes, e.g. `en`/`ru`; a language without an ISO 639-1 code shows its 3-letter code, e.g. `haw`/`yue` — not a bug) appear on the already-transcribed ASR rows immediately — without reopening the video — and that hovering a badge shows the full language name. Turn it back OFF and confirm all badges disappear immediately. Load an external `.srt` and confirm its rows never show a badge at any toggle state.

## Translation

- Verify target language selection.
- Smoke the touched provider only.
- For LLM-like providers, check context-aware sequential behavior if changed.

## ASR/OCR

- Download/select model or engine through existing dialogs when changing download/settings code.
- Start ASR and cancel it.
- ASR pause/resume (F-04): start interactive ASR on a longer video, left-click the player-bar ASR chip to pause — confirm new subtitles stop appearing within ~one chunk, the already-generated subtitles REMAIN, and the chip icon switches to the resume (play) state; click again to resume and confirm transcription continues from where it left off. Verify pausing then seeking (which restarts ASR) does not leave the new run stuck paused, and that disabling ASR while paused clears as before. Smoke both whisper.cpp and faster-whisper, and dual (primary+secondary) ASR.
- T-10 per-segment language smoke: use mixed-language media with auto-detect ASR. With `Detect Language Per Segment` OFF, confirm language stays pinned after the first non-empty segment/chunk; with it ON, confirm later segments/chunks can switch language. Smoke both interactive ASR and Batch Subtitles when the engines are available.
- F-19 word timing smoke: with faster-whisper interactive ASR and re-segmentation enabled, toggle `Word-Level Cue Timing` ON and confirm split cue boundaries follow real speech pace instead of flashing on dense speech; toggle it OFF and confirm timing falls back to proportional cue splitting. Missing/mismatched JSON must not crash ASR.
- F-19 VAD snapping smoke: with `Assets\silero_vad.onnx` present, run interactive ASR on speech with audible pauses/music/noise; toggle `Snap Cue Timing to Speech` ON and confirm split cue changes move to nearby pauses where possible, then OFF and confirm the word/proportional timing path remains. Smoke both faster-whisper and whisper.cpp for the VAD toggle; batch and loaded subtitles should not run VAD.
- Run OCR on bitmap subtitles and cancel it.
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
- **HC-27b owner acceptance (automated fix complete; manual run pending) — A/B isolation:** with persistence ON, assign a voice on local media A, immediately open local media B and assign a different voice. Wait for background writes to settle. Confirm both `A.ru.voices.json` and `B.ru.voices.json` contain only their own final assignments, then reopen both media and confirm the matching voices are restored.
- **HC-27b owner acceptance (automated fix complete; manual run pending) — latest-wins:** on one media, rapidly choose an old and then a new voice. Wait for writes to settle, confirm the companion contains the newest state, reopen the media, and confirm the newest voice is restored.
- **HC-27b owner acceptance (automated fix complete; manual run pending) — OFF:** while persistence is still ON, wait for any current companion write to settle and record the stable file's timestamp/content (or its absence). Turn `PersistPerLineVoices` OFF, click **Save & Close**, make another per-line assignment, wait for background work to settle, and confirm the companion was not created or changed while OFF; media and subtitle files remain unchanged. The queued-behind-save race itself is covered by a deterministic test.
- **HC-27b owner acceptance (automated fix complete; manual run pending) — clear:** with persistence ON and a pending/previous assignment, choose "Use default voice" for every overridden line. Wait for writes to settle; confirm the expected companion is removed and does not reappear, then reopen the media and confirm defaults are restored.
- **HC-27b owner acceptance (automated fix complete; manual run pending) — exit/restart:** change an assignment and immediately choose **Exit App** from the application menu (or **Quit** from the tray if the app is there). Restart, reopen the media, and confirm the last allowed state is restored and no other media companion was touched. The internal Dispose-wait guarantee is covered by deterministic tests, not this visual check.
- **HC-27b owner acceptance (automated fix complete; manual run pending) — responsiveness:** on a writable slow/network-backed media path with a large subtitle list, repeat assignment/clear. Confirm the UI remains interactive without a visible pause, then confirm the companion was updated successfully and the final assignment is restored after reopening the media.
- Ear-test CosyVoice2 Russian on real content: voice is Russian, ducking is audible, and the original audio remains present.
- Launch the published `.exe` on the target RTX 5090 machine with `GenerateDubbing=false`, then run one mock/real dubbing smoke if the local engine is provisioned.

## Packaging

- Run `scripts/codex/ship.ps1`.
- Confirm publish output contains `LLPlayer.exe`, `lib/7z.dll`, all copied `FFmpeg/*.dll`, `Plugins/YoutubeDL/YoutubeDL.dll`, and `YoutubeDL.pdb`.
- Confirm publish output contains committed dubbing sidecar source (`dub_sidecar/server.py`, `pyproject.toml`, `uv.lock`, `README.md`) and does not contain dubbing runtime/model/output artifacts (`DubEngine`, `dubmodels`, `*.ru.dub.*`, `*.ru.voices.json`).
- Do not require network download of `yt-dlp.exe` for local smoke unless explicitly shipping a release.
- **T-13e Stable controlled run — PENDING, explicit owner approval required:** on a post-T-13e tag, retain the tag-to-SHA mapping, run URL, successful full-preflight/package/release-tail steps, draft-release state, and asset name/size/SHA-256. Pushing a release tag and deleting any test tag/release are external mutations and are not part of normal verification.
- **T-13e Testing controlled run — PENDING, explicit owner approval required:** dispatch from the default branch for an exact post-T-13e commit; before the run record the current `v0.0.1` asset/rollback state, then retain the prepared immutable SHA, run URL, full-preflight/package/verified-artifact evidence, and final asset name/size/SHA-256. The `--clobber` upload overwrites external state and must never run without separate approval.
