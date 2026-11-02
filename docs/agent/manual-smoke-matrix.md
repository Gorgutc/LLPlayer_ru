# Manual Smoke Matrix

Automated tests do not cover every LLPlayer behavior. Use these manual checks when touching related areas.

## ASR/OCR, resource management and language workflows — authorized sequence, 2026-11-02

The owner authorized the [complete first coordinated delivery](plans/2026-11-02-f13-language-workflows.md),
then commands/service screens plus media backends with a second full delivery ritual, then Computer Use for
the entire F-13 matrix and necessary fixes. This updates the earlier deferred authorization below: native
acceptance is authorized **after the second ritual**, has **NOT_RUN**, and must not be claimed from source,
headless/native-library tests, package checks or earlier screenshots. Record the actual installed source/payload,
OS and available devices/models/providers; unavailable cases retain precise NOT_RUN reasons.

- Open Recognition and all three resource managers with an isolated profile. Verify engine-specific editors,
  installed state, language/region lists, all runtime preference add/remove/reorder controls and loaded-runtime
  status. Saving preferences alone starts no model/tool/provider. Check detached cancel, invalid paths/numbers,
  persistence failure/retry and latest shared fallback preservation; unavailable GPU/native libraries are honest.
- Explicitly download/cancel/delete a suitable small model where network/resources are available; check byte/total
  progress, errors, original-target preservation and refresh. Windows XXL installation adopts its EXE path only
  after that engine download; ordinary model actions preserve a custom engine choice. Linux automatic XXL
  installation is explicitly unavailable and a valid executable manual route remains. Verify in-use deletion
  rejection, close/quit drain and preservation of foreign files; do not mutate a user installation as a test fixture.
- Exercise ASR on each slot, pause/resume/cancel, source replacement and shutdown with actual available models.
  Exercise Tesseract per-language managed/legacy fallback and explicit TESSDATA_PREFIX. On Windows, separately
  record real Microsoft OCR language enumeration/activation and bitmap recognition; absent package identity or
  language support must produce a recoverable message. Linux retains Tesseract and explicit Microsoft unavailability.
- Scan a natural-order recursive batch with mixed inclusion, preferred Russian audio and explicit row override;
  verify output skip/overwrite policy, transcript/counts/time/ETA, Retry, cancellation and error recovery. Each new
  run uses the existing Playback/export BOM preference and retains that snapshot if it changes during processing;
  Batch exposes no independent BOM setting. Watch slow copies/rename/duplicates, add files during Scan,
  repoint the root with queued files, remove the root and
  toggle off. Old-session files must not auto-run under the new folder/options. Fail the combined options/voice
  save: both prior settings remain, while a foreign replacement is preserved with a visible rollback conflict.
- Close main and Batch windows while running/watching, restore via available tray/taskbar and verify hidden
  playback pause, progress/notifications and reachable Cancel. Explicit Quit and concurrent close paths drain
  watchers/runs and leave no owned background work. On desktops without tray/global idle support, verify the
  visible-window/conservative-CPU fallback rather than reporting measured inactivity.
- With an already provisioned local dubbing environment, check settings, custom/default voices, per-line default/
  assignment/clear in both slots, equal-timing latest-edit behavior, rapid subtitle replacement and opt-in companion
  restoration. Immediate batch voice input and saved companion agree. Existing dub attachment stays selectable
  without automatic playback. Cancel assembly and quit during work: earlier output survives and owned children
  drain without late output resurrection. Missing engine/models remain recoverable; do not auto-provision them.
- In AI Insights check both slots/original-translated/modes and configured provider/model. Opening sends no request.
  Generate/Cancel, late results after source replacement, temporary unready captures, long-transcript coverage,
  malformed/truncated replies and close remain safe. Verify summary/vocabulary Copy/Save and deduplicated Add to
  List with provenance; Word Manager/Anki paths continue to work. Real provider results require actual available
  credentials/service and explicit run, not a fake-service regression result.

## Settings, Subtitles/words and Playlists/online video — owner-deferred native checks, 2026-11-02

These checks cover the single coordinated [approved delivery](plans/2026-11-02-f13-settings-subtitles-online.md).
All native Windows/Linux UI, real provider/helper/PDIC, physical audio/input and DPI scenarios below are
**NOT_RUN / DEFERRED BY OWNER** until separately authorized. Use an isolated profile and record the installed
source/payload identity. Automated/publish/PR/Desktop/Vault results require their own actual receipts; none is
declared by this checklist. Preserve earlier dated observations below as historical evidence.

- Open Settings → Player → Playback settings. Check every Audio/software Video/Player/Subtitles/Startup editor
  against the plan mapping, legal boundaries and invalid raw text. Close/Escape discards; Save applies after durable
  persistence; an unwritable playback/prefs file preserves live state and the retry draft. Exercise the secondary
  remembered-volume write failure and compensating rollback without overwriting a different concurrent file.
- With old/absent files, confirm remembered volume `100`, max `150`, buffer `500/30000` ms, frames `5/4`, timeouts
  `300000/10000/20000/8000` ms and previous defaults. While the editor is open, change transport volume/mute and
  subtitle sync/viewport from other controls. Unedited fields keep fresh state; a changed Default Volume updates
  current/remembered volume after Save. A lower maximum clamps safely; unrelated Save must not set volume to `75`.
- Exercise Audio enable, filters, delay and ordered languages, then default/explicit output selection and refresh.
  Unplug the selected USB/Bluetooth device while playing: effective output should fall back to default or the
  temporary silent clock, and recover on the two-second serialized refresh without replaying lost PCM. Reconnect:
  return to the remembered target. Change the OS default; test failed enumeration/open and AL operational errors.
  Audio Off stays off; forced Null stays silent. Pause/seek/close during recovery must remain bounded. Record audible
  continuity and A/V timing as physical observations, not inferred unit-test success.
- Change aspect/custom ratio, software deinterlace/double-rate, decoder threads, zoom unit, latency, HDR method/nits
  and all four colour filters/reset. Exercise session zoom/pan/rotation/flips/crop. Check delayed/paused rendering,
  default reset and current-media guards. Hardware acceleration/D3D/profile mismatch/super resolution/VSync remain
  honestly unavailable/ineffective on the software backend; selecting a stored field must not claim GPU rendering.
- Change buffers/frame/resolution/timeouts/accurate seek and validate buffer/latency constraints. Edit log output,
  levels/cache and FFmpeg profile: changes are labelled next-start and do not restart the engine. Restart within an
  authorized smoke, check effective values and `LLPLAYER_LOG_LEVEL` precedence. Main-profile filter limitations are
  visible; saved filter intent survives a profile that cannot load filters.
- Exercise subtitle enable/local search/paths/cache/automatic captions, ordered languages, auto-detect and shared
  primary/secondary fallback. Recognition shows the hosted fallback read-only and points to Playback settings;
  stale Recognition Save must not undo new values. Test all eleven cue-generation policy/limit editors without
  starting a model merely to save them. Both numeric subtitle delays stay session-only and reject stale-source edits.
  Change BOM default, then verify later SRT/VTT/TXT export and manual provider save; an open export keeps its snapshot.
- With cached bitmap subtitles in each selected sidebar slot, scroll/current-highlight/seek and spoiler masking
  work without OCR or hidden-image leaks. Change track, media, slot and same-URL media while images load: no stale
  thumbnail, disposed resource or unbounded retained image should publish. Preserve existing overlay bitmap layout.
- Middle-click a rendered primary/secondary text cue: one sentence lookup uses the full displayed text and correct
  translated/source language. Left word/drag/right menu/popup/toolbar actions remain independent. For Japanese and
  Chinese overlays test repeated words, whitespace, punctuation, mixed Latin/CJK, supplementary Han and exact word
  offsets; Japanese uses the packaged managed dictionary. Preserve mask/original/display selection provenance.
- Open Download subtitles: no request occurs before explicit Search. Enter session API key/account, search/title/
  language and page; verify preferred-language order and filename/format/year/downloads/rating/release. Duration
  explicitly reads Unavailable. Change media/title: query refreshes and results cancel/retire without auto-search.
  Verify rejected credentials/quota/rate limits, malformed responses, cancellation and retry without secrets in logs.
- Use Load in Primary and verify downloaded-language/source metadata, `ManualDownloaded`, subtitle seek and primary
  assignment while the secondary slot remains intact. Cancel/change media/same-URL reopen across HTTP/temp waits:
  stale results cannot load. Adopted temp files remain available after dialog close and are removed only after
  full player shutdown; failed/unused files are cleaned by exact ownership. Text save uses the explicit picker/BOM
  and overwrite confirmation; cancelling the picker consumes no download. Bitmap/archive downloads are explicitly
  unsupported by this REST adaptation. Closing clears credentials and drains pending requests.
- On Windows, configure PDIC by manual path/Browse/explicit Auto Set; startup and opening settings must not probe or
  start the bridge. Select PDIC for a single Japanese word: repeated overlay/sidebar actions share one app process,
  receive the unchanged sentence and exact one-based UTF-16 offset, and retain word/popup routing. Invalid offsets,
  missing/dead executable, stalled writes and changed paths fail recoverably. Popup close keeps the singleton;
  full app shutdown sends Close where possible and removes its owned process. On Linux, verify the explicit
  unavailable state without process execution or dictionary substitution.
- Open a safe configured online single video and playlist/multi-video fixture. Check title, actual/expected counts,
  selected mark, choose/reopen and no-wrap previous/next in the dialog/menus/transport. Unavailable entries are
  reported honestly. Exercise cancellation/replacement/close, removed/foreign items and repeated identical URLs;
  old item tokens/results cannot mutate new media. Verify formats/languages/headers/chapters/automatic VTT captions.
- Edit shared plugin height/prefer-combined/extra-argument options in both entry points. Validate quoted allowed
  extraction/network/authentication options and bounded closed playlist ranges; reject config/plugin/runtime/output/
  file-producing switches without starting a helper. Direct local/HTTP playback and Ctrl+V still work with tools unset.
  No yt-dlp/Deno install/update/PATH probe is introduced. Provider/network credentials used for this smoke must stay
  outside logs/receipts/packages. This acceptance does not guarantee DRM or newly authored-caption support.
- Use title Open next/prev on files numbered `1`, `2`, `10` with mixed-case matching extensions and a different
  extension nearby: natural same-extension order, no wrap. Copy uses the selected file/page rather than a collection
  URL or signed CDN URL. Open path reveals an existing local path or opens the selected safe credential-free HTTP(S)
  page in the browser. A changed source during adapter waits cannot retarget the click. Missing local, relative,
  malformed, credential-bearing and other-scheme destinations report a contextual error; unavailable/failing browser
  or file-manager adapters remain recoverable. No folder scan occurs merely by starting the app or opening the menu.
- Inspect both platform packages for `LibNMeCab.dll`, `LibNMeCab.IpaDicBin.dll`, all six IpaDic data/legal assets and
  notices. Reject user Playback/PDIC/OnlineVideo/preferences/temp files, external helper executables/archives/caches
  and unrelated native/model files; retain required app deps/runtimeconfig. Package checks do not replace the native
  dictionary/audio/provider/GUI cases above.

## Player UI, Mouse and Always On Top — owner-deferred native checks, 2026-11-02

All scenarios below are **NOT_RUN / DEFERRED BY OWNER** on native Windows and Linux. Execute only within separately
agreed native acceptance, recording the installed payload/source identity. Current implementation and automated
evidence are tracked in the [current status](f13-status-2026-10-18.md#player-ui-mouse-and-always-on-top--owner-approved-2026-11-02);
the [plan](plans/2026-11-02-f13-player-ui.md) maps the eleven controls. These rows add no controls or product stages.

- With old/absent preferences, confirm idle `2500` ms, fades `200/200` ms, bar/thumb/track `20/12/4` DIP, hover-only/Topmost off and all three mouse actions on. Edit every Player field, Save, reopen/restart and confirm committed values. Close/Escape must discard edits; a controlled save failure leaves live state unchanged and permits retry. An unrelated preference changed while Settings is open must survive Save.
- Enter blank/malformed/out-of-range numeric drafts, non-finite dimensions and `track > thumb` or `thumb > bar`. Save must reject them without applying a partial draft; correction restores Save. Check legal boundary values, including idle `0` (no wait under normal idle conditions) and each fade `0` (immediate in that direction).
- With hover-only off, confirm idle hiding/cursor behavior still requires fullscreen and playing, respects seeking and an open word popup, and responds to the committed timeout. Use different fade-in/out values, then reverse activity during a transition; each direction must use its own committed duration.
- Enable hover-only, move into and out of the stable transport reveal region, and confirm hidden controls remain reachable. Exercise keyboard reveal, focus within transport, seek drag/capture loss and open transport/word popups; no premature hiding or unrelated control activation should occur. Verify min/default/max geometry retains seek input plus aligned non-interactive waveform/A-B overlays.
- Toggle Always On Top, Save and restart; confirm the main player follows committed `Topmost` in normal/fullscreen transitions and remains usable with owned dialogs. Discard an opposite draft and confirm it does not change the committed window state.
- Toggle each mouse switch independently. Bare-video single-click play/pause, second-tap fullscreen and wheel volume must follow their switches. The first click still dismisses an open word popup without playback; no second playback toggle occurs. Subtitle words/phrase drags, popup/toolbar/seek/volume controls and right-click menu retain their own input. Existing keyboard-only help stays keyboard-only.

## Playback

- F-13 shared Avalonia video input (2026-10-28): with media playing, single-click bare video and confirm immediate pause; single-click again to resume. With a word popup open, the first bare-video click must only dismiss it. Double-click bare video: the first tap performs its immediate action and the second changes fullscreen without a second playback toggle. Right-click bare video and check subtitle/track/settings menu commands and current track checks. Subtitle words, phrase drags, masked rows, popup content, buttons and seek/volume controls must retain their own pointer actions; wheel volume remains usable. Bounded native C2 Windows evidence confirms video opening, left pause holding time and right-video menu; it does not accept all remaining pointer/DPI scenarios or C3. Native Linux NOT_RUN.

- Open a local video file.
- Open media from a command-line path/URL.
- Play, pause, seek, stop, change volume, fullscreen, and close.
- Confirm taskbar progress and play/pause thumbnail action update.
- A-B repeat (F-12): during playback click the A-B button (repeat icon) to set A, then B; confirm playback loops between them, the seek bar shows the A/B markers + highlighted band, and the button icon lights. Click again to clear and confirm normal playback resumes. Optionally bind Set A / Set B / Clear in Settings ▸ Keys and verify the shortcuts; confirm the points reset when a new file is opened.
- Seek-bar waveform (F-12, since 0.3.28): open a local video/audio file, click the Waveform toggle in the player bar; confirm an amplitude envelope appears behind the seek track after a brief background build (it should track loud/quiet sections), playback is not blocked while it builds, and the toggle lights when on. Toggle off → the envelope disappears. Open another file → the waveform rebuilds for the new file (no stale envelope). Confirm a file with no audio / a live stream shows no waveform and does not error.

## Subtitles

- F-13 search/export (2026-10-28 historical primary-only sidebar checkpoint; the 2026-11-01 selected-slot checks below extend that sidebar boundary): test trimmed case-insensitive literal search, case-sensitive and Unicode whole-word modes, valid/invalid regex and timeout recovery, original/display selection and masked rows. Next/previous and Enter/Shift+Enter must wrap and seek; Escape clears and restores focus. Restart and confirm only the three search switches persist at that checkpoint. Export both slots in SRT/VTT/TXT, Original / Translated only / Translated with fallback and BOM on/off; compare WPF `DisplayText`, timing and Original italic styles. Change media/translation while the save picker is open and confirm the captured output is stable. Cancel the picker or close the export/app during a pending save and confirm existing destination preservation, no partial file or late write; explicitly confirm overwrite and inspect the output. During recognition confirm the partial-output warning. Empty/bitmap-only slots report no text rather than running OCR. Existing word TSV/APKG export remains usable.
- F-13 bounded native C2 Windows receipt: complete preinstalled ProgramFiles .NET10.0.7 loaded primary SRT4 cues; `cat`3/4, case2/4, whole-word1/4, invalid regex zero/error, corrected regex2/4; Enter2:14/Shift+Enter0:44 (existing offset), Escape4/focus. Export dialog/native picker opened; actual save is **PENDING manual user action**, original overwrite declined, saved-file/overwrite acceptance unclaimed. CUA can enter a filename but buttons/keyboard are unreliable. C3 caption reread pending; native Linux and remaining scenarios NOT_RUN. Initial SRT failure used temp10.0.13 physically lacking Brotli; unchanged payload succeeds on complete installed10.0.7. Preserve that earlier failure/diagnosis, distinguish build/test SDK10.0.402/temp10.0.13, and do not infer a shipped SRT regression or native C3 PASS. Full local C3 gates and receipt details: [current status](f13-status-2026-10-18.md#current-subtitle-searchexport-and-video-input--2026-10-28).

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

### Primary/Secondary Sidebar and sync — owner-deferred native checks, 2026-11-01

All following scenarios are **NOT_RUN** on native Windows and Linux. Focused headless/real-engine regressions and
package checks do not establish physical playback, bitmap timing, native input or DPI acceptance. Run only within
the separately agreed native acceptance scope and record the exact installed payload/source identity.

The app source checkpoint is `c04dd101551ea4ed75cbba94184d7020a5dd38f5`; completed local Windows gates/packages
are listed in the [current status](f13-status-2026-10-18.md#primarysecondary-sidebar-and-cue-sync--owner-approved-2026-11-01).
They do not mark any scenario below executed or authorize native/Desktop acceptance.

- Load distinct text tracks into both slots. With absent preferences, confirm Primary is selected. Choose Secondary by mouse and keyboard; only its cues/count/current highlight should appear. Keep a query active while switching: case/whole-word/regex, original/display choice, mask boundary and next/previous wrap must follow the chosen slot. Enter/Shift+Enter and Escape/focus remain usable. Select an empty/off slot and confirm no fallback or stale highlighted row.
- Change only the sidebar selector, reopen/restart and confirm it restores. Open Settings, change the toolbar selector afterward, then Save Settings; preserve that newer selection and unrelated current preferences. Inspect the existing save-error notification on a controlled persistence failure without inferring a disk save from the live selection.
- On each slot, test sync while paused and playing, using positive and negative pre-existing delay and a fractional raw cue start. Confirm the chosen raw start aligns to the current playhead by `newDelay = CurTime - rawStart`, without an additional old-delay adjustment. The playhead is not explicitly sought, playback state/speed stay unchanged, and the opposite slot delay/track/display stay unchanged. Ordinary time progression while playing is expected. Reopen a subtitle stream and confirm the existing runtime-delay reset; no delay preference or subtitle-file edit is created.
- Seed cached translated text in both slots, choose original/display independently, then sync each slot. Confirm cached text/display choice/provider ownership survives rather than resetting or re-requesting translation. Cancel or replace a pending row by changing slot/media/track/filter or closing the host; no late delay change or stale word lookup may occur. Reopen the same media URL and replace a track/cue with matching index/timings to exercise identity rejection rather than index-only admission.
- With sentence translation off and no translation cache, preserve embedded and external subtitles while switching to an external video source, and separately exercise a failed switch's rollback. For either selected slot, confirm both native subtitle owners/content/delays survive, the previous row's command identity is rejected, and fresh rows appear and can sync/seek without changing the other delay. No incidental translation/text update should be needed to refresh the sidebar.
- With text in one slot and a bitmap in the other, sync the text row while the last track-picker action selected the opposite slot. Confirm the opposite bitmap remains visible and its delay is untouched. Separately exercise selected bitmap timing in paused/playing states under the existing clear/resync path. This block adds no bitmap sidebar renderer or selected-bitmap restoration; deterministic opposite-bitmap identity checks do not accept encoded native bitmap timing.
- Click/keyboard-activate the row Sync button and confirm it does not also seek/toggle playback. Press Sync with pointer or Space, then rebind/recycle its row before release: even if the new row is live, no sync may transfer to it. While holding the press, issue a rejected automation Click and then release; rejection must not drop the original owner and authorize the rebound row. Cancel by releasing the pointer outside the button, then confirm a fresh legitimate activation works. Hold Enter through OS repeat/rebind and verify no command transfers; the next fresh matching key still works. Tap the timestamp to verify the normal selected-slot seek includes its delay/fractional rounding. Word click/phrase drag/right-click retains the selected row's slot/languages/display text; masked rows never send hidden text or underscores to lookup/library. Retire or recycle a row between pointer press and release; it must not pause, seek or look up a newer row. Check dark/light themes, 220 DIP sidebar width, focus traversal and idle auto-scroll; hovering the list continues to suppress automatic scrolling.

Existing WPF checks below retain their scope. Middle-click/CJK/PDIC/export expansion was excluded from this dated additive
Avalonia stage; [plan and current evidence](plans/2026-11-01-f13-sidebar-sync.md) retain its full delivery boundaries.

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

- **Word/phrase selection, owner-approved 2026-10-27 — NOT_RUN / DEFERRED BY OWNER:** on each native
  Windows/Linux host, use a new package and an isolated profile. Press a word and confirm playback pauses before
  the release. Drag forward and backward across words, including wrapped lines and a cue with a hard line break;
  confirm a visible selection highlight covers the word range and clears on release. Confirm the release opens
  the word popup with the selected phrase (Translation mode) or copies the phrase / whole cue (Clipboard /
  ClipboardAll), that a simple click still acts on one word, and that releasing off-text, changing the cue or
  losing focus cancels without a provider call. Repeat in the subtitle sidebar, and confirm a masked sidebar row
  never sends underscores or hidden source text to a provider or the word library. Check both overlay slots,
  fullscreen, minimum-window size and 100/150/200% DPI. Headless pointer/render tests and the local
  WPF/Windows Avalonia gates do not close this native row. No Computer Use or player launch is authorized.
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
  persisted values and keep Settings open for retry. Save must preserve the other current Settings fields (eleven
  at this stage's historical checkpoint) and
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
  Confirm no migration of the other Settings fields (twelve preceding the font block) or WPF settings.
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
- **Text-subtitle background block, separately owner-approved 2026-10-19 — native acceptance NOT_RUN / DEFERRED BY OWNER:**
  when native acceptance is separately authorized, use the new package and an isolated profile on each native
  Windows/Linux host. An older file without the three background keys must retain black (`#000000`) and zero
  primary/secondary opacity, preserving the previous appearance. Check the common raw RGB editor and swatch:
  outer whitespace trims and lowercase six-digit HEX canonicalizes to uppercase; names, short/alpha HEX and
  malformed values visibly block Save. Missing/null/invalid stored color strings independently fall back to black;
  a wrong JSON type retains the whole-file malformed-input policy. Loaded integer opacities clamp to `0..100`,
  absent keys default zero, and fractional/null/wrong-type/overflowing JSON must keep the existing load warning,
  preserved original, best-effort `.bad` copy and whole-file defaults. No silent fractional conversion is allowed.
  Edit each raw percentage `TextBox`: valid whole `0..100` input previews that slot; fractional, non-finite,
  malformed, overflowing and out-of-range raw text blocks Save without changing live/persisted state. Check
  asymmetric primary/secondary opacity with font inheritance both on and off; neither opacity may follow the
  other or the font flag. This deliberately adapts WPF's `SubsUseSeparateFonts` background coupling and leaves
  WPF itself unchanged. Reset Background resets only the three draft fields to black and `0/0`, including invalid
  raw text. Copy Primary, Reset Secondary and Reset Fonts preserve backgrounds and retain their font-only scope.
  The existing two-line detached font-and-background preview must show draft backgrounds without applying to
  live overlays or writing preferences; its size/scale behavior remains unchanged and it is not a full appearance
  preview. Save & Close merges the twenty-two owned fields into fresh current preferences, resolves candidate
  fonts/brushes, persists and only then applies live settings. Close/Esc discards the draft; write failure preserves
  live/persisted values and leaves Settings open for retry. Reopen/restart restores all three saved fields and
  concurrent unrelated volume/mute/recents/last-folder/provider state survives Save.
  Inspect both video text overlays with wrapped and unequal-length centered lines: each gets one compact
  axis-aligned whole-block rectangle from the union of the existing `TextLayout` line boxes, expanded only by
  existing outline pad, without host-width fill, per-line rectangles, new padding or rounded corners. Zero opacity
  must skip drawing entirely; empty, whitespace-only and hidden subtitles show no fill. Warm-cache color/alpha
  edits, outline-pad changes and resize must match a fresh control. Only the background alpha changes; glyph Fill,
  outline, shadow, measure/layout and word-hit geometry must retain their existing behavior. Compare actual
  `InputHitTest`, routed `e.Source`, word taps, video taps and double taps off/on for glyphs, spaces, inter-line and
  off-text regions, including popup dismissal and fullscreen. The custom background draw operation must remain
  non-hit-testable. Inspect dark/light/minimum-window usability and 100%/150%/200% DPI when separately authorized.
  Background binding is limited to the two video overlays and the two detached preview lines; sidebar/bitmap
  subtitles, Core/engine/native runtime, WPF settings, assets/dependencies and packaging retain their contracts.
  No Computer Use, application/launcher launch, WSL or model/provider run is authorized by this row. Headless
  render/input tests and static package checks do not close native GUI/DPI/exact-desktop acceptance or full F-13
  parity. Evidence and pending gates are separate in [the background checkpoint](f13-status-2026-10-18.md#bounded-text-subtitle-background).
- **A-B repeat (F-12 parity), owner-approved 2026-10-24 — native acceptance NOT_RUN / DEFERRED BY OWNER:** on each
  native Windows/Linux host, use the rebuilt package and an isolated profile. Open a media file and confirm the
  transport's A-B button is enabled only with an open file. Left-click it: the first click sets A at the current
  position (the icon lights and one marker appears on the seek bar); play forward, click again to set B (the second
  marker and a translucent band between the points appear, and playback loops back to A when it reaches B, with audio
  and subtitles in sync and no audible gap beyond the existing seek path). Click a third time to clear both points and
  the band; the icon returns to its normal state. Right-click the button and exercise the explicit Set point A /
  Set point B / Clear A-B items: with A set, Set point B at an earlier position must keep the previous B and report
  the "B must be after A" message; Set point B alone (no A) is accepted but does not loop; and A moved past an
  existing B drops that B. Confirm A-B takes priority over whole-file Loop Playback at end-of-file
  and is inert during reverse playback and for HLS-live media; open another file and confirm both points drop.
  No Computer Use, application/launcher launch, WSL or model/provider run is authorized by this row; headless and
  package checks do not close native GUI/DPI/audible acceptance. Evidence and pending gates:
  [the A-B checkpoint](f13-status-2026-10-18.md#bounded-a-b-repeat-parity-f-12).
- **Text subtitle layout (WPF Position / Size text half, additive Avalonia line alignment), owner-approved 2026-10-24 —
  native acceptance NOT_RUN / DEFERRED BY OWNER:** on each native Windows/Linux host, use the rebuilt package and an
  isolated profile. With media open and both subtitle slots populated, edit width to 50 % and confirm the centered box
  halves without touching the bottom offset; set a max width below the computed width and confirm the cap; resize the
  window, toggle the sidebar and fullscreen and confirm the box never goes below 1 DIP and the vertical clearance
  behavior is unchanged. Set alignment Left and Right (an additive Avalonia option; WPF stays centered) and confirm both
  the drawn lines and clicked-word lookup follow; enable Ignore line breaks on a cue with hard breaks and confirm it
  renders as one wrapped line with click targets aligned to the glyphs, and that a dash-list cue keeps its two lines
  (canonical flatten semantics); set the primary/secondary distance and confirm the gap changes while each slot keeps its
  own font/colors. Restart and confirm all five values; Close/Esc must discard edits, invalid raw width/max/distance must
  block Save visibly, and a save failure must keep live/persisted state. Check 100 %/150 %/200 % DPI and the minimum
  window. WPF's own Position / Size page, bitmap scale/position, the dual separator and a dual-specific alignment are
  out of scope and must stay unchanged. No Computer Use or application launch is authorized by this row;
  headless/package checks do not close native acceptance. Evidence:
  [the layout checkpoint](f13-status-2026-10-18.md#bounded-text-subtitle-layout-wpf-position--size-text-half).
- **App shortcut rebinding (hotkeys half, part 1), owner-approved 2026-10-25 — native acceptance NOT_RUN / DEFERRED
  BY OWNER:** use the rebuilt package and an isolated profile. Open Settings → Tools and confirm the app shortcuts
  section lists every app action with its current chord, a capture button and Reset; click a capture button and press a
  new chord (for example Ctrl+Shift+B), then Save and confirm the new chord triggers the action in the main window while
  the default chord no longer does. Confirm a second click, Esc or clicking elsewhere cancels capture and leaves the
  chord unchanged, a modifier-only press does not become a chord, assigning the same chord to two app actions blocks
  Save with a readable error, and Reset restores the default chord(s). Save without touching any shortcut row and
  confirm nothing is persisted as an override and the cheat sheet still opens with both default chords (F1 and Shift+/);
  restart and confirm the saved chords are restored and the cheat sheet shows them. Close/Esc without Save discards the
  draft. Confirm engine actions (play/pause, arrows, A/S/D, F, Ctrl+O) still use their built-in chords; binding an app
  action to an engine-reserved chord is not rejected in this part (documented boundary, separate later block). Check
  windowed/fullscreen and 100 %/150 %/200 % DPI. No Computer Use or application launch is authorized by this row.
  Evidence: [the hotkey checkpoint](f13-status-2026-10-18.md#bounded-app-shortcut-rebinding-hotkeys-half-part-1).

- **App-hotkey correction, owner-approved 2026-10-25 — product `c45fd958` implemented; local GREEN/full/package/publish gates PASS; native acceptance
  NOT_RUN / DEFERRED BY OWNER:** use an isolated profile and, only when separately authorized, the rebuilt native
  package. During capture, try Win/Super/Meta plus a key and an unsupported modifier combination; the existing chord
  must stay unchanged and capture must remain active, then accept a supported Ctrl/Alt/Shift chord or cancel normally.
  Escape must still cancel with unsupported modifiers present.
  Desktop/OS interception of Win/Super keys is a separate observation; it does not replace the deterministic event
  rejection test. In the isolated stored override map, try numeric keys (including defined enum numbers), an undefined
  numeric value, comma-combined names and an unsupported name; restart must restore the affected action's defaults.
  Confirm a defined key name and each supported display alias still work. Open the cheat sheet, apply a filter, then
  change an app chord in Settings and Save: the existing window must show the new chord with the same filter. Reset
  and Save must refresh that same window to defaults while retaining its filter. Close/Esc without Save and a save
  failure must leave both routing and displayed chords unchanged. No Computer Use or application launch is authorized
  by this row. Compiled pre-production RED observed 29 failures and 37 passes (66 total, 0 skipped, exit 1); the same
  focused command then passed GREEN (66 passed, 0 failed/skipped, exit 0). Fresh local WPF and Windows Avalonia full
  gates, package-contract checks and local publish/content validation passed, actual exit 0. Published PR #181 has a
  VERIFIED PREMERGE CHECKPOINT at last-checked head `56117ee774d97f8014926e684e7c860978fa7f4a`: Windows run 40193094026
  and Linux run 40193094196, attempt 1 SUCCESS including package publish/artifact uploads. Prior document review had
  zero Critical/Important findings; this docs-only delta awaits independent infrastructure review. Check live PR/vault
  handoff for latest head, merge/main and final handoff receipts; this checkpoint does not claim post-merge acceptance.
  Old PR #180 receipts do not accept
  this correction; see [the current correction status](f13-status-2026-10-18.md#current-bounded-correction-and-proposed-continuation--2026-10-25).

- **Subtitles Sidebar settings, owner-approved 2026-10-26 — native acceptance NOT_RUN / DEFERRED BY OWNER:** use the
  rebuilt package and an isolated profile. Missing new keys must restore right docking, mask/original off, padding `5`,
  family `Segoe UI`, size `16`, weight `Normal` (width `360` and visibility unchanged); note the approved visible change
  from cue font `14`/padding `8,6` to `16`/uniform `5`. Open Settings → Subtitles → Subtitles sidebar, toggle Show
  Sidebar/width and each option, Save & Close, reopen and restart; confirm saved values and that unrelated
  volume/mute/recents/shortcuts survive. Confirm the width stays usable at the narrow minimum window and after
  100 %/150 %/200 % DPI, the splitter resizes and remembers the width, and fullscreen keeps the sidebar on the same
  side with a usable video column. With translation enabled, show original text: rows and search follow the original,
  word lookup treats the clicked word as source text, and video overlays/engine translation toggles are unaffected.
  Enable the spoiler mask: rows after the playing cue become `_` of the same displayed length, current/past rows stay
  readable, before the first cue all rows are masked, seeking/append/ASR/media replacement keep the mask aligned, and a
  masked row never exposes hidden source text to word lookup. Change family to an installed font and weight/size, then
  confirm only sidebar cue rows change (not the video overlay, header or filter box) and that Linux without `Segoe UI`
  keeps the requested name with the existing fallback instead of installing fonts. Invalid numeric raw input blocks Save
  visibly; a save failure keeps live state, Settings open and persisted bytes unchanged, with retry working; Close/Esc
  discard the draft. No Computer Use or application launch is authorized by this row. Evidence:
  [the sidebar settings checkpoint](f13-status-2026-10-18.md#bounded-avalonia-subtitles-sidebar-settings-block).

- **Engine key-binding editor and app↔engine collision policy, owner-approved 2026-10-26 — native acceptance
  NOT_RUN / DEFERRED BY OWNER:** use the rebuilt package and an isolated profile. Open Settings → Tools and confirm the
  engine editor lists every existing built-in engine binding with its current chord, Enabled and On release flags; capture a new
  chord for one action, Save, reopen and restart, then confirm the saved chord is active and the old one is not. Confirm
  Reset to engine defaults restores all rows in the draft and after Save. Try saving two enabled rows with the same
  chord, an engine chord equal to a default or overridden app shortcut, and an app shortcut changed to an engine chord:
  each must block Save with a readable error naming both actions and change nothing live or persisted; a disabled engine
  row with the same chord must be allowed. Delete a row and confirm it stops firing after Save; Save must keep at least
  one engine row. Trigger duplicated/app-colliding keys by hand-editing the isolated prefs file and confirm the start-up
  conflict toast appears. Confirm the cheat sheet updates an open window after Save, friendly chord names are shown,
  screen readers announce action+chord, and the editor controls stay reachable at the minimum window width and
  100 %/150 %/200 % DPI. A write failure must keep live keys/preferences and Settings unchanged with retry working;
  Close/Esc discard. No Computer Use or application launch is authorized by this row. Evidence:
  [the engine-key checkpoint](f13-status-2026-10-18.md#bounded-avalonia-engine-key-binding-editor-and-appengine-collisions).

- **Settings parity Translation / Word / Anki, owner-approved 2026-10-26 — native acceptance NOT_RUN / DEFERRED BY
  OWNER:** use the rebuilt package and an isolated profile. Translation settings: set Backward/Forward counts and
  concurrency, Save, reopen and restart; confirm the values persist in `LLPlayer.Translation.json` and that
  ahead-translation depth/parallelism changes on a real subtitle file, with invalid values blocked and old files
  keeping 1/12/2. Word: in Settings → Tools choose Clipboard and click a subtitle word — the word must be copied and no
  popup may open; choose ClipboardAll — the cue text must be copied; choose Translation — the dictionary popup must
  appear as before. Anki: set a deck, open Word Manager, confirm the deck is prefilled, export TSV/.apkg and push to a
  running Anki with AnkiConnect; confirm the APKG note fields/source-language metadata and that a second Clear all
  click is required before the irreversible delete. Verify the new Tools sections stay reachable at minimum window
  size and 100 %/150 %/200 % DPI, dark/light themes, and that unsaved edits are discarded on Close/Esc.
  No Computer Use or application launch is authorized by this row. Evidence:
  [the Settings-parity checkpoint](f13-status-2026-10-18.md#bounded-avalonia-settings-parity-translation--word--anki).

- **Audio waveform overlay (F-12 audio half), owner-approved 2026-10-24 — native acceptance NOT_RUN / DEFERRED BY
  OWNER:** use the rebuilt package and an isolated profile. Open media with audio and confirm the waveform toggle sits
  next to the A-B button, starts off, and toggling it on builds the envelope over the seek bar without moving the
  slider thumb; peaks must appear over the whole file and the envelope must be symmetric around the centre line. Seek
  by clicking/dragging the bar and confirm the slider still owns every pointer action (the envelope never blocks it),
  A-B markers remain visible above the envelope, and the envelope respects the current theme/accent. Toggle it off and
  confirm the overlay disappears and no further decode work is requested; toggle it on/off for a silent video and
  confirm it collapses to the centre line; reopen another file and confirm the envelope rebuilds for the new media;
  restart the app with the toggle on and confirm it is restored and the envelope builds on the next open. Check
  Close/Esc isn't affected, windowed/fullscreen and 100 %/150 %/200 % DPI. WPF's own waveform, colours/per-channel
  modes and hotkey rebinding are separate and unchanged. No Computer Use or application launch is authorized by this
  row. Evidence:
  [the waveform checkpoint](f13-status-2026-10-18.md#bounded-audio-waveform-overlay-f-12-audio-half).

- **Bitmap subtitles, dual separator and block alignment (rest of Position / Size), owner-approved 2026-10-24 —
  native acceptance NOT_RUN / DEFERRED BY OWNER:** use the rebuilt package and an isolated profile. Open a bitmap
  subtitle track (PGS/VobSub) and set primary/secondary bitmap scale and primary bitmap position; confirm the drawn
  cue scales around its centre and shifts vertically only for normal horizontal cues, and that values reset after
  reopening media. With dual text subtitles confirm the rendered centred separator line appears only while the
  secondary cue is displayed, its width/opacity follow Settings (default 280 DIP / 30 %), its height equals the
  primary/secondary distance and the primary line does not move. Load both subtitle slots (regular track or ASR) and
  switch block alignment Bottom/Center/Top for single and dual subtitles: the single anchor applies with one loaded
  slot, the when-dual anchor as soon as both slots are loaded (no jump when a secondary cue appears), and the chosen
  edge sits at the configured bottom distance while it fits; otherwise the block is guarded at the viewport bottom and,
  in fullscreen with visible controls, above the transport clearance instead of clipping. Check Close/Esc discard,
  invalid separator/alignment/bitmap input blocks Save, restart restores the four persisted fields while bitmap values
  reset, and 100 %/150 %/200 % DPI plus the minimum window. WPF percentage geometry, fix-overflow margin and the
  inline/absolute bitmap split remain out of scope and unchanged. No Computer Use or application launch is authorized
  by this row. Evidence:
  [the bitmap layout checkpoint](f13-status-2026-10-18.md#bounded-bitmap-subtitle-layout-dual-separator-and-block-alignment-position--size-remainder).

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
- **Audio hot-plug:** the reopen-only gap described by this older Linux checkpoint is superseded in source by the
  two-second serialized recovery policy above. Execute the current device-removal/return/default-change cases only
  under native owner authorization; physical audible recovery remains **NOT_RUN / DEFERRED BY OWNER**.

## Remaining settings/hotkeys — owner-deferred native checks (2026-11-01)

The [approved design](plans/2026-11-01-f13-settings-keys-design.md) and automated/fake/headless evidence do not
establish native acceptance. Every case below is **NOT_RUN / DEFERRED BY OWNER** on both native OSes until
separately authorized. Use an isolated profile and record the actual package/source identity.

- At Translation 400×360 and Settings 560×440 verify Dark/Light scroll/footer reachability, Tab/focus, raw errors,
  Escape and capture cancellation. Save/reopen/restart sampling/manual/null-token and known-region edits; retain
  a custom model and unowned map entries. Check narrow reset/discard and recoverable failed Save.
- Open/load/save must issue no provider diagnostic or Anki probe. Only an explicitly authorized Check/Get Models/
  Hello contacts the selected real provider; Hello visibly sends a model request. Cancel, change provider and
  close/retry without stale models/status, raw error bodies or secrets. Fake/loopback tests do not accept a provider.
- With old-file defaults confirm no automatic clipboard/browser effect. Explicitly opt in and check exact modifier
  versus extra modifiers, ordered menu edits/clones, escaped Unicode word/cue templates, non-HTTP(S) refusal,
  menu A after popup B, masked/sidebar/popup ownership and nonempty Primary/Secondary/All subtitle copying.
  Hold a diagnostic open during owner close: a child veto must preserve the parent until a later accepted retry;
  unfinished browser work must not indefinitely block accepted shutdown or publish a late effect.
- After integrated hotkey gates pass, bind and invoke an unbound A-B action; add/clone/reassign engine/app rows,
  multiple free chords per action, enabled/release flags and both-direction collisions regardless of phase. Check
  held immediate repeats, matching/modifier-first release, focus/owned-child/capture transitions and text-input
  suppression. Hold a key, switch to an external app, release there and return: the first fresh press must work;
  returning alone must not execute canceled work or synthesize a native engine release. Save/restart and keep an
  open filtered cheat sheet; Reset without Save and failed Save leave live
  behavior unchanged. Preserve custom engine delegates through automated real-adapter coverage as well.
- While a Settings draft is open, change Word/Anki fields through another permitted owner; a hotkey-only Save must
  preserve those latest values for each untouched field. Explicit Reset Word actions then Save must intentionally
  replace the menu; Reset alone must not. Retain the saved next-open Anki default and already-open session deck
  through Tools edits. TSV/APKG/send stay
  explicit; a real send creates notes and is never a connection test. No endpoint/key/APKG-options editor, PDIC,
  real-model/GPU/audio acceptance or complete F-13 parity is implied.

## Configured online video — owner-deferred native checks (2026-11-01)

These are acceptance requirements, **NOT_RUN / DEFERRED BY OWNER** until separately authorized on each native OS.

- Record exact app commit/package and external standalone yt-dlp/Deno versions and hashes. Save absolute tool paths containing spaces/Unicode; Close without Save must discard edits. Missing/malformed settings remain untouched; startup must not run tools or install anything.
- With paths unset/invalid, open a local clip, direct HTTP media and an extensionless direct-media URL through their ordinary routes. Safe Ctrl+V must retain its existing replacement guard. Explicit Open online URL reports missing dependencies without breaking subsequent direct opens.
- Open a supported public online URL through the explicit command; verify actual video and audio, seeking where the source permits, current VTT automatic captions in both slots, chapters and required request headers. Do not infer provider support from `--version` or fixture success.
- Cancel a stalled extraction before and after metadata arrives; replace with a different URL, repeat the identical URL, open a local file, close the dialog and shut down. Waits stay bounded, only current results/messages publish, and owned process trees/temp state are released. Confirm nonzero exit/bad metadata/permission errors permit recovery.
- On native Linux repeat executable-permission failure and recovery. This initial single-video checklist did not
  accept playlists or helper arguments; the current coordinated checklist above adds them with bounded options.
  No helper installer/downloader, broader authored-caption support or DRM acceptance is implied.
- Automated package checks separately require the portable managed plugin DLL/PDB at exact-cased `Plugins/YoutubeDL`, verify archive integrity/loadability, and reject helper executables and user runtime state. These checks do not substitute for the native cases above.

## Dated bounded Windows candidate receipt — 2026-10-26

After the owner's explicit Windows Computer Use/visual GO, candidate
`fcf4b4912c23f080f4db1a5a005e669d6372a566`, fresh build `fcf4b4912c23-20261026T151130963Z-1aca0724`,
was exercised in an isolated synthetic profile. This supplements the historical rows above; it is not a blanket
manual-smoke PASS, native Linux acceptance or acceptance of a later merged-main package.
Full case matrix, artifact identities and limits: [native visual audit](f13-native-visual-audit-2026-10-26.md).

- Observed native: generated-media open/play/end/seek, F/Esc fullscreen, A-B loop/clear and waveform toggle;
  left Sidebar/mask/search/typography Save/restart; dual overlay color/outline/background/position;
  numeric UIA inner-text edit/focus then pointer Save rejects raw `1.5`/`1001`/`0` and persists corrected 1/1000/64.
- Word Manager/deck discard/session ownership, immediate word edits/read-only metadata, filter and non-destructive
  first Clear/disarm; actual default-path TSV/APKG three-record bytes/SQLite verified. The later popup Save added
  a fourth word after those exports. Custom picker path/filename NOT_ESTABLISHED; no product defect established.
- Main H/Ctrl+B and F1 sheet; native disable-H Save left V working, reset+Save restored H/V in the same filtered
  open sheet. Capture/rebind/collision/held-key-up native acceptance NOT_RUN; headless regressions remain separate.
- Explicit loopback mock demonstrated translated overlay versus Original Sidebar, Clipboard/ClipboardAll and
  word popup/save with modes seeded only in the closed isolated profile. Primary seeded Bold/Italic rendered;
  secondary font tuple was read back only. Real providers and GUI mode/font ComboBox selection NOT_RUN.
- Minimum-size/high-DPI unestablished (two resize attempts left ordinary 1282×792 unchanged); external RU SRT
  selection, destructive Clear, native Linux, real models/ASR/OCR/GPU/audible output/AnkiConnect NOT_RUN.
  Independent visual review: 0 Critical/Important, one P3 Light-sidebar shadow finding deferred to a separate
  bounded styling proposal. Owned windows/mock closed; final four-word/profile and earlier three-note exports
  have distinct receipts. Corrective PR/main CI, final-main Desktop and vault closeout require their own evidence.
