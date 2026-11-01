# Media Runtime Contract

This document freezes runtime boundaries and high-risk invariants from `main`.

## Engine

- `FlyleafLoader.StartEngine()` loads `LLPlayer.Engine.json` or defaults, then calls `Engine.Start`.
- `Engine.Start()` has UI-thread expectations and initializes UI-side pieces before non-UI FFmpeg/plugins loading.
- Default release engine paths are `FFmpegPath = ":FFmpeg"` and `PluginsPath = ":Plugins"`.
- Leading-colon paths are intentional. `:FFmpeg` and `:Plugins` are resolved by searching upward from `AppDomain.CurrentDomain.BaseDirectory`; this supports debug and release layouts. Do not replace this with a single fixed relative path.
- `timeBeginPeriod(1)` and `SetThreadExecutionState` are reference-counted. Do not bypass their counters.

## WPF Dispatcher Boundaries

- `Utils.UI`, `UIInvokeIfRequired`, subtitle collection synchronization, subtitle property updates, and player open/update paths are dispatcher-sensitive.
- Do not remove UI marshalling because a call appears indirect or unused. Playback threads, subtitle updates, and WPF collections must keep their UI-thread boundaries.

## Player

- `Config` is bound to a single `Player`; do not reuse one config instance across players.
- Open/open-async, stop, seek, playback, stream switching, and error events are coordinated by `Player` partial classes.
- Latest open requests clear stale queued opens; preserve this behavior.
- Seek/resync lock ordering across decoder codec contexts and demuxer format context is high risk.
- A-B repeat (F-12, since 0.3.27) checks on the playback thread inside `UpdateCurTime` — after the `lock(seeks)` block, so no nested locking — whether the playhead reached the user's B point and, if so, issues the existing `SeekAccurate` back to A. It adds no new lock and routes through the same queued-seek/resync path as a slider seek; the A/B points are two `long` fields read/written via `Volatile.Read/Write`. The check is inert when no points are set (byte-identical), during reverse playback, and for HLS-live; the points reset on open via `ResetMe`.
- The seek-bar waveform (F-12, since 0.3.28) decodes the whole audio stream once on a background worker via a dedicated `WaveformReader` that opens its OWN isolated `Demuxer` + `AudioDecoder` + `SwrContext` (the same sanctioned "second `avformat_open_input`" pattern the ASR `AudioReader` uses — a second decode must never share the playing `AVFormatContext`); it never touches the playing decoder/demuxer/seek path and adds no lock. The pass resamples to S16 mono 16 kHz and folds peaks into the pure `WaveformPeakBuilder` (`FlyleafLib/Utils/WaveformPeaks.cs`); it carries none of the ASR denoise/silence/chunking state. The build is cancellable (the reader's Interrupter is wired to the token), is started/cancelled by `Player.Waveform.cs` (on audio-open when enabled, on toggle, and reset in `ResetMe`), publishes peaks to the UI through the existing `UI(...)` marshaller, fails soft on a decode error, and is skipped for live/HLS, no-audio, or unknown-duration media. Off by default → no decode runs.

## Portable Runtime Boundaries (Linux, F-13)

Additive: everything above still applies to the Windows target unchanged. These rules cover FlyleafLib's portable
`net10.0` target (`FlyleafLib/Platform/Portable/`) and its host, `LLPlayer.Avalonia`.

- **Windows stays identical.** Shared FlyleafLib files change only inside `#if !WINDOWS` blocks or portable `partial`
  files; the Windows target must compile the same code with the same behaviour.
- **Engine start/stop.** The host installs `Utils.UIDispatcher`, `Utils.HostServices`, and `AudioEngine.Backend`
  before `Engine.Start`, and passes an explicit FFmpeg folder (`--ffmpeg-dir`, `LLPLAYER_FFMPEG_DIR`, else
  `<app>/FFmpeg`) holding the FFmpeg 8 sonames (`libavcodec.so.62`, `libavutil.so.60`, ...). The Windows `:FFmpeg` / `:Plugins` resolution rule is unchanged. Without a
  WPF `Application`, engine shutdown hangs off `AppDomain.ProcessExit` (disposes players, flushes the log).
- **UI dispatcher hook.** `Utils.UI*` marshal through `IUIDispatcher`; with no dispatcher installed they run inline
  (tests only). The WPF Dispatcher Boundaries above apply to the Avalonia dispatcher too: do not remove marshalling.
  `BindingOperations.EnableCollectionSynchronization` only forwards to a host handler, so the host must itself move
  collection change notifications onto its UI thread.
- **Video: software decode + `sws_scale` BGRA path.** No hardware decoding (`ConfigHWFrames` returns false). On the
  playback thread the renderer deinterlaces (`bwdif`, `yadif` fallback, `estdif` for a lone paused frame), tone-maps
  PQ/HLG to SDR (`zscale`+`tonemap` when the FFmpeg build has them), crops, converts to BGRA32 with `sws_scale` at
  min(native, viewport) size per axis, applies rotation/mirroring and the brightness/contrast/hue/saturation filters,
  and hands the ready-to-show frame to `IVideoSurface.PresentFrame(span, width, height, stride)`; snapshots go through
  the same pipeline at native size and are encoded by FFmpeg. The host only scales the frame into
  `Renderer.Viewport`; `Rotation`, `HFlip`, and `VFlip` are informational and must not be applied again.
- **`IVideoSurface` threading.** `PresentFrame` and `ClearFrame` run on the playback thread or on the thread that
  stops/disposes the player, sometimes while a renderer lock is held. The span is valid only during the call. An
  implementation must copy the pixels and `Post` to the UI thread — never block on or `Invoke` into the UI thread
  from these calls (deadlock). `Renderer.Surface` may be set from any thread; setting it repaints the current frame.
- **Audio sinks.** The player always asks the backend for a 2-channel S16 sink. `IAudioSink.Submit` copies before it
  returns; `SamplesPlayed` is monotonic and never exceeds what was submitted; `Flush` drops queued audio without
  counting it as played. If `CreateSink` throws, audio is disabled for that player, as on Windows. `NullAudioSink`
  consumes at wall-clock speed, so A/V sync works without a sound device; OpenAL Soft (system `libopenal.so.1`) is the
  desktop backend; `LLPLAYER_AUDIO_BACKEND=null|openal|auto` selects, and CI/headless runs use `null`.
  `AudioEngine.RefreshDevices()` re-enumerates the devices and moves players whose explicitly selected device
  disappeared to the default device, but only when the host calls it: there is no OpenAL hot-plug notification and
  the Linux app does not call it yet. A disconnected OpenAL device (also the one behind "default") makes every
  `Submit` throw, and `Audio.AddSamples` turns that into `ClearBuffer`, so audio stays silent until the stream is
  re-initialised (reopen, or a device change). Known gap, tracked in backlog F-13 (parity list).
- **Other hooks.** Modifier keys come from `Player.KeyStateProvider`; filtered views refresh through
  `CollectionViewSource.RefreshHandler`. On portable Linux, engine bootstrap registers the `IOCRService` adapter
  through `SubtitlesOCR.ServiceFactory`; it uses the system Tesseract 5 C API and system tessdata. `TESSDATA_PREFIX`
  is honored for data discovery. Initialization reports missing library/data/language or corrupt model; the native
  binding remains loaded for process lifetime while each track owns and frees its API engine. An in-flight native
  OCR call cannot be interrupted, so cancellation is observed between cues. Cursor hiding, screen-saver inhibition,
  and timer resolution are no-ops in the engine and belong to the host.

## Media Framework

- Demuxers and decoders inherit thread lifecycle from `RunThreadBase`.
- Native packet/frame ownership is explicit. Do not bypass `PacketQueue` and frame disposal paths.
- Rendering has D3D/Flyleaf paths, device-lost handling, swapchain present, frame cache, and screen clear behavior.
- Any render loop or device change requires real Windows/WPF/DirectX smoke testing.

## Subtitles

- `SubtitlesManager.Subs` must remain sorted by `StartTime`; current/previous/next lookup depends on binary search.
- Text and bitmap subtitles are both first-class.
- Bitmap subtitle data has explicit lifetime/disposal and positioning.
- The portable host draws bitmap subtitles from the engine `BgraBitmap`; `SubsBitmapPosition` remains the owner of position/scale state and rules, and the host mirrors those rules in video coordinates without mutating engine state for rendering. No engine change is implied by host-side drawing.
- `SubtitlesSelectedHelper` is static/global and is not multi-player safe. Do not redesign it incidentally.
- `SubtitleData` carries optional per-cue metadata. `Language` (the ASR source language, T-10) and `SpeakerId` (a speaker-label string reserved for future diarization, F-03 — nothing populates it yet) are inert by themselves: they do not change rendering, export, or translation, and a default `null` is byte-identical. (`Language` is additionally surfaced read-only as a sidebar language badge when `Subtitles.ASRPerSegmentLanguage` is on — since 0.3.38, display-only and gated on that toggle precisely because ASR stamps the pinned language on every cue even with the toggle off; the property notifies like `AssignedVoiceId` so the badge stays live if a future writer mutates it post-Add.) `AssignedVoiceId` (the per-line dub-voice override, F-16 phase 2a) is likewise inert for rendering, export, and translation but IS consumed by the AI dub renderer (`DubbingRenderer` synthesizes that line with the override instead of `DubbingConfig.DefaultVoiceId`); a default `null` falls back to the default voice, so a track with no assignments is byte-identical. The batch dubbing path can also apply a current-session `DubbingVoiceAssignmentMap` snapshot when the open local media path and cue millisecond timings match the batch job, including the existing `.ru.srt` render path; `SubtitleData` itself is still not serialized (the opt-in `Subtitles.PersistPerLineVoices` toggle instead mirrors the override to a `video.ru.voices.json` companion file and restores it onto the loaded/ASR cues at load time — fill-empty, byte-identical when off; see dubbing-contract.md). All three fields are copied by `SubtitleData.Clone()` and carried onto the split cues when a cue is re-segmented, so per-cue metadata is not lost.

## Avalonia Sidebar Cue Commands — 2026-11-01

The owner-approved additive shared Windows/Linux sidebar chooses either slot without replacing its engine source.
`CueInfo.Start`/`End` remain raw authored times. A trailing optional opaque `SubtitleCueIdentity` binds live row commands
to adapter owner, media epoch/native generation, selected item/video source, slot, native subtitle source/manager and
cue reference/index/raw timing. Equal indices/timestamps or a fabricated/foreign row do not authorize mutation. Both
slots publish current-index changes through the existing dispatcher/coalesced cue notification paths.

Preserving external-video switching can retain native cue objects/text/caches while rebinding their command source.
The existing external-video completion callback now queues coalesced cue notifications for both slots after success
or settled rollback, including direct preserving native callers. Consumers re-read the current source rather than
carrying old row/token payloads, and retain existing disposal checks. This refresh does not depend on translation
being enabled or an incidental text update, and adds no engine mutation, queue, provider reset or collection lock.

`SeekToCue` validates currentness then uses `SeekAccurate(rawStart, slot)`, retaining the matching delay and fractional
rounding. `SynchronizeSubtitleToCurrentTimeAsync` takes an optional caller cancellation token and enters the existing
recognition mutation queue without resetting/suspending either translator. Retired selection/source/filter rows cancel
their token; the adapter checks it at worker entry, in the live-source predicate and immediately before mutation. Valid
sync computes `checked(player.CurTime - rawStart.Ticks)` at execution and sets only the chosen runtime `SubConfig.Delay`.
No seek/play/pause/speed/other-delay write is added. Cached translations, display choices and provider ownership remain
intact for valid and rejected work; only an admitted selected text overlay is refreshed after resync.

The Delay setter's external-subtitle resync reads legacy global `SubtitlesSelectedHelper.CurIndex`. The adapter scopes
it to the validated slot around that setter and restores its previous value in `finally`, preserving the opposite
bitmap rather than clearing it through the other slot's context. No helper redesign, bitmap renderer/lifetime change
or new selected-bitmap restoration is introduced. The selected bitmap retains existing WPF-compatible clear/resync
semantics; native paused/playing text and bitmap timing acceptance remains **NOT_RUN**.

Collection snapshots release their gate before Delay enters `SetCurrentTime`/`ReSync`; never hold that gate across
decoder resync. Existing Player action gating protects media reset/stop/dispose and app-owned mutations use the queue.
Arbitrary native/custom `Open(StreamBase)` callers do not universally hold that gate, so this bounded change does not
claim atomicity with every simultaneous custom native open. Existing supported app routes and deterministic queued
replacement are reviewed/tested; native concurrency and physical playback acceptance remain distinct.

App source checkpoint: `c04dd101551ea4ed75cbba94184d7020a5dd38f5`. Final local Windows verification/package/plugin
checks passed on controlled snapshots, with exact 16-file raw-product proof v2 passed separately.
[Current evidence](f13-status-2026-10-18.md#primarysecondary-sidebar-and-cue-sync--owner-approved-2026-11-01)
keeps dirty docs validation, remote delivery and native timing acceptance separate.

## ASR/OCR/Translation

- ASR uses independent audio demux/decode, 16 kHz WAV chunks, Whisper.net or faster-whisper process, cancellation, and shared dual-ASR behavior. Interactive ASR additionally supports pause/resume (F-04) via a cooperative async gate (`PauseTokenSource`/`PauseToken`): the consumer awaits the gate at a chunk boundary, so pausing keeps already-produced subtitles and back-pressures the bounded producer channel (no thread is blocked), while cancellation still clears subtitles. The gate is reset at the start of, and in the `finally` of, every `Execute` so a run never starts paused. Batch ASR passes a default (never-paused) token and is unaffected. The producer may optionally cut a chunk at a detected silent frame (RMS below `ASRSilenceRmsThreshold`, only after `ASRSilenceSoftFraction` of the size/elapsed budget) with the existing size/elapsed caps kept as a hard ceiling (T-09, `ASRSplitOnSilence`, default on) — this changes only *where* a chunk is cut, never the 16 kHz WAV format or the chunk `Start`/`End` contract, and applies to interactive and batch ASR. Interactive ASR may also fold back to transcribe the audio skipped before a mid-video start (T-08, `ASRFoldBack`, default off): the earlier span `[0..curTime)` is transcribed *first*, so cues are still emitted in increasing-time order and the append-only sorted-subtitle invariant is preserved; it only affects the interactive `curTime > 30s` seek path (batch passes `curTime = 0`, so fold-back never triggers). The producer may also optionally denoise the resampled audio before chunking (F-02, `ASRDenoise`, default off): a managed high-pass plus an FFmpeg `afftdn` stationary-noise filter is applied to the 16 kHz mono S16 PCM before it is written to the WAV chunk, so both ASR engines and batch see the cleaned audio. This changes only the audio *content* fed to Whisper, never the 16 kHz/mono/S16 WAV format, the chunk `Start`/`End` contract (timestamps stay frame-pts-derived), or the sorted-emission invariant; an end-of-pass flush drains the filter's lookahead tail so no audio is lost. The `afftdn` graph is a separate isolated avfilter graph built inside `AudioReader` (it does NOT touch the playback `AudioConfig.Filters` graph); if `afftdn` is unavailable in the shipped FFmpeg it degrades to the managed high-pass alone, and with denoise off the path is byte-identical. It targets stationary noise (hiss/hum/rumble), not speech/music separation. By default ASR pins the first detected language onto the whole transcript (F-17 anti-drift); the opt-in `Subtitles.ASRPerSegmentLanguage` (T-10, default off → byte-identical) instead lets each whisper.cpp segment / faster-whisper chunk auto-detect its own language (the pin and its capture/per-chunk reset are gated through the pure `AsrLanguagePolicy`), and the per-cue language is recorded on `SubtitleData.Language` (inert metadata; it does not change rendering by itself). It only affects the auto-detect path (a model/user-fixed language is untouched) and applies to interactive and batch ASR; with it off the path is byte-identical. Interactive faster-whisper ASR may additionally request per-word timestamps (F-19, `Subtitles.WordTimestamps`, default on): the engine command gets `--word_timestamps` and a side JSON output (`<basename>.json`) that is read AFTER the chunk process exits, and re-segmentation places cue boundaries on real word end-times instead of character proportion. This does not change the 16 kHz WAV chunk format or the chunk `Start`/`End` contract; the stdout SRT cue parser (`SubShortReg`/`SubLongReg`) is left untouched and stays the source of cue text/time, with words attached by segment order only when the JSON lines up 1:1 (otherwise timing falls back to proportional — fail-soft, byte-identical). When on, a chunk's cues are buffered and emitted at chunk end (the JSON exists only then), invisible during playback because ASR runs ahead of the play position; whisper.cpp surfaces no words and the batch consumer ignores them (output-identical). Interactive ASR (both engines) may additionally snap re-segmentation cue boundaries onto speech pauses via a CPU VAD (F-19 slice 2, `Subtitles.VadCueSnapping`, default on): per audio chunk a bundled Silero VAD ONNX model (run through `Microsoft.ML.OnnxRuntime`) reads the SAME 16 kHz mono S16 PCM the engine transcribes — a non-destructive `MemoryStream.GetBuffer` read, so the chunk handed to Whisper is byte-unchanged — and produces speech segments; re-segmentation then nudges each internal cue boundary onto the nearest speech↔silence transition within `SnapToSpeechToleranceSec` (0.5 s), layered on top of the word/character timing. It is fail-soft — a missing model or any ONNX Runtime load/inference failure skips snapping (timing unchanged, byte-identical) — and is enabled only on the interactive path (via an explicit `AudioReader` flag); batch and loaded subtitles run no VAD. It does not change the 16 kHz WAV chunk format, the chunk `Start`/`End` contract, or the append-only sorted-subtitle invariant (a snapped boundary stays strictly inside the cue's original span).
- **Linux ASR engine slice (F-13, engine-stage evidence recorded 2026-10-02):** the portable `net10.0` target uses the CPU-only Whisper.net 1.9.0 runtime; the Avalonia package explicitly carries only `runtimes/linux-x64/libwhisper.so`, `libggml-whisper.so`, `libggml-base-whisper.so`, and `libggml-cpu-whisper.so`. GPU, Metal, other architectures, and the NuGet build-copy targets are excluded. The cached native build requires glibc 2.34+, `GLIBCXX_3.4.29`, `libgomp.so.1`, and a CPU with AVX, AVX2, FMA, and F16C. Portable Whisper factory options disable GPU use. The existing Silero VAD model is linked into the Linux package at `Assets/silero_vad.onnx`; ONNX Runtime remains CPU. Linux ASR prerequisite checks skip the Windows VC++ Redistributable probe and report missing selected model/runtime or invalid faster-whisper executable paths before processing. Linux faster-whisper uses `faster-whisper-xxl` as the default executable name, preserves the configured manual path, and treats a nonzero finished process (even with a completion marker) as failure. Windows behavior and executable naming remain as before. At that engine-only checkpoint, Avalonia UI work was still pending. A separate `WhisperCppASRService` CPU inference using full `large-v3` weights on the 11-second JFK sample produced three coherent English cues; the reproducible receipt is `artifacts/f13-asr-2026-10-02/large-v3-cpu-inference.json`. This direct service inference remains distinct from the current headless MainWindow/Player-command smoke recorded below, batch acceptance, faster-whisper executable acceptance, and live desktop/Windows runtime.
- **Avalonia recognition UI (F-13 bounded slice, 2026-10-03):** the app routes recognition actions through `LLPlayer.Core.Services.RecognitionActions` and `PlayerRecognitionBackend`, which use the canonical Flyleaf commands for ASR and OCR. One serialized queue also admits media/track mutations, cancels stale queued work by media generation, and drains on host shutdown. The Recognition menu exposes whisper.cpp CPU on primary/secondary slots, global pause/resume, per-slot cancel/clear, and OCR of the already-selected bitmap track. OCR uses the existing selected-track language metadata; only `Language.Unknown` reaches the configured per-slot fallback. The dialog discovers only existing models in the selected directory and never downloads/installs weights. Recognition settings are detached; Save & Close writes `LLPlayer.Recognition.json` atomically before applying values, while Close discards edits. Corrupt/unreadable input is preserved and reported. Settings save is blocked while recognition or subtitle loading is busy; cancel/clear a still-enabled ASR slot before saving if inference has already ended. Current headless evidence exercises MainWindow commands with a real Player and existing `large-v3` weights, including actual cue output, pause/cancel, and real primary-English/secondary-Russian PGS OCR; it is distinct from live desktop, Windows owner acceptance, and direct service inference. The UI smoke passed 2/2 (14 s), with receipts in `artifacts/f13-recognition-2026-10-03/real-ui.log` and `.exit`. The full Linux gate also passed (FlyleafLib 1554 passed/6 skipped; Avalonia 139 passed/1 skipped), receipt `artifacts/f13-recognition-2026-10-03/linux-full.log`; Core focused tests passed 14/14. Windows-target test-project cross-compilation passed without warnings/errors. Publish/package validation passed; the archive SHA-256 is recorded in `artifacts/f13-recognition-2026-10-03/package/LLPlayer-0.3.62-linux-x64.tar.gz.sha256`, and package-negative checks rejected the settings JSON and both temp variants (`package-negative.exit` 0). Independent final verification review passed with zero Critical/Important findings. Native desktop and Windows runtime/owner acceptance remain pending.
- Batch ASR uses a headless media probe/transcriber path, not `Player.OpenAsync` and not the current interactive `OpenSubtitlesASR` command. It must keep ASR text in the source video language, disable Whisper built-in translate-to-English in its config snapshot, and perform Russian translation as a separate provider step. Because this path is headless (independent demuxers/decoders over a config snapshot, no live `Player`), a batch run is not tied to the main window: while a batch is active, closing the main video window keeps the run alive (the app stays in the tray) and the `Player` is disposed only once shutdown actually proceeds. (A normal no-batch close still disposes the `Player` directly before shutting down.)
- OCR services must preserve `TryInitialize -> Do -> Dispose`.
- Translation service creation is lazy and provider-based through `ITranslateService`, `ITranslateSettings`, `TranslateServiceType`, and `TranslateServiceFactory`.
- **Avalonia word translation (F-13 preferences/lifecycle slice):** the app-scoped `FlyleafWordTranslator` consumes an immutable `TranslationSettings` snapshot and creates the selected engine service lazily through `TranslateServiceFactory` in word mode. Its generation-scoped cache is cleared and pending operations are canceled on settings save, source-language change, popup invalidation, or shutdown. Each operation owns its linked CTS through completion of the engine consumer; canceled/stale generations cannot update the cache or popup. Save writes atomically, then publishes the new settings snapshot and generation under one admission lock before draining/discarding the prior provider; load/save never initializes a provider. `MainWindow` defers closing while host shutdown drains popup/translator consumers and disposes their provider service. At the preferences-slice checkpoint, dictionary configuration defaulted Off and word saving/manager/exports were not yet in the Avalonia UI; those are additive in the subsequent bounded words slice below. Sentence/subtitle auto-translation remains out of scope.
- Each `WordPopup` owns its lazily created translation and definition services, but it borrows the application-owned `PDICSender`. Host teardown invalidates and cancels the active lookup before disposing only those popup-owned services; the local async operation retains and disposes its CTS only after its translation and definition token consumers have settled. Cancellation, provider/language invalidation, and a superseding click must prevent the old operation from writing to caches, popup UI, spinner state, context-menu/Save snapshots, or user notifications. Ordinary Close, Esc, playback dismissal, fullscreen, and tray hide/show do not cross this service-lifetime boundary.
- **Avalonia word library and explicit export (F-13 words slice):** dictionary lookup uses the existing `WordDefinitionService`/selector and defaults to `Off`. Word-click state is captured as a detached `WordClickSnapshot` with term, context, slot, source/target language and overlay `Data.IsTranslated`; sidebar translation selection remains separately represented by `SubtitleData.UseTranslated`. Translation and definition consumers run independently in parallel; each has its own busy/result/fail-soft path and generation-aware cache. Save is unavailable until both settle. A translated overlay click saves its shown target-language term with other lookup fields blank; same-language source definition lookup remains allowed. `WordLibrary` wraps the engine `WordListStore`, retaining the `SavedWord` JSON-array schema and first-wins term deduplication. It writes an atomic candidate before publishing mutations in memory. Read/parse failure preserves the source file and blocks mutations until external repair; there is no import or recovery UI. The singleton Word Manager edits Reading/Translation/Definition/Example immediately, keeps Term read-only, filters and removes words. Its saved `AppPrefs.AnkiDeck` default seeds the next opening; an already-open manager owns its session deck and Settings Save does not overwrite it. TSV and APKG are explicit save-picker exports; AnkiConnect is a separate explicit send action to localhost, with injectable HTTP for tests and cancellation drain on close. These operations do not add automatic sentence/subtitle translation or full parity for other WPF word features.
- **Avalonia waveform overlay (F-13 audio half):** the adapter mirrors the engine's live `Player.WaveformPeaks`/`WaveformActive`/`WaveformEnabled`; peaks are transient engine state rebuilt per opened file and never persisted. The persisted `ShowWaveform` preference (off by default) is applied to `WaveformEnabled` on startup and on each transport toggle, so the OFF path performs no audio decode. The display-only control never hit-tests the seek slider and paints nothing without peaks; geometry mirrors the WPF seek-bar envelope. No engine decode/format change.
- **Avalonia subtitle/sentence translation (F-13 bounded Linux slice):** the UI has independent Word and Sentence provider selections, an explicit selector for editing their shared provider settings, and separate primary/secondary session toggles that start Off and are not persisted. The settings reuse `LLPlayer.Translation.json` and the engine provider/target/chat records; loading/saving performs no health/probe requests, though ordinary translation may resume for a slot explicitly enabled in the current session. Subtitle work uses Flyleaf's existing `SubTranslator` and ahead-translation behavior, admitted through the existing `RecognitionActions` serialization path. The portable lifecycle adds suspension/drain and generation checks around toggles, settings changes, media/track mutation, and host shutdown; stale results are rejected and translated-cue state invalidated when settings change. Overlay/sidebar display provenance remains tied to each surface's selected text, and cue/view notifications are coalesced. Focused tests, Linux `--fast`, package/content validation, independent review, and launcher refresh passed; the receipt is `artifacts/f13-current-build-2026-10-03/installation-receipt-subtitles.json`. This bounded slice does not complete full F-13 parity. See [`2026-10-03-f13-subtitle-translation.md`](plans/2026-10-03-f13-subtitle-translation.md).
- **Avalonia translation chat settings (F-13 bounded Linux slice):** the Translation dialog exposes existing `TranslationSettings.Chat` behavior and defaults, including target-region language formatting, without changing ordinary translation semantics. Context-before/after and KeepContext counts accept whole nonnegative `Int32` values; zero disables that context side or retains no prior subtitle lines, respectively. Context-window indexing handles `Int32.MaxValue` without changing ordinary behavior. Detached draft Save & Close applies persisted edits; Close discards them. Focused tests, Linux `--fast`, package review, and installation readback passed; native desktop/provider acceptance and Windows build/runtime remain untested. See [`2026-10-03-f13-translation-chat.md`](plans/2026-10-03-f13-translation-chat.md).
- LLM-like translation has sequential/context modes; do not parallelize away context retention. Batch subtitle translation must keep `KeepContext` and `ContextWindow` sequential within each file. By default completed-file translation overlaps with ASR for the next file (pipelined throughput); a background-friendly "serialize ASR and translation" mode (default on in the app) instead runs each file fully through ASR → translate → save before the next file's ASR, so a GPU ASR engine and a local-LLM translator never both saturate the GPU at once.
- Batch can stay responsive during a long run via a per-chunk CPU fallback: `BatchAsrTranscriber` / `AudioReader` / `FasterWhisperASRService` accept an optional `Func<bool> preferCpu` (app supplies a Win32 idle check); when it returns true, the NEXT faster-whisper chunk is launched with the device forced to CPU (and GPU-only compute types remapped) instead of the configured GPU device. The chunk in flight always finishes on its chosen device, so nothing already computed is lost; switching only affects subsequent chunks. The interactive ASR path passes no policy (`null`), so its behaviour is unchanged. whisper.cpp (in-process) does not participate. This is additive and must not change ASR/translation results.

  **Additive F-13 Avalonia note (2026-10-26):** the Avalonia Word Manager seeds its deck from the persisted `AppPrefs.AnkiDeck` default (Settings → Tools → Anki); an already-open manager keeps its own session deck, so the WPF statement above remains true for WPF while Avalonia persists only the next-open default.

## Shared Avalonia settings/action lifecycle — 2026-11-01

The [approved settings/hotkeys block](plans/2026-11-01-f13-settings-keys-design.md) adds explicit Translation
Check/Get Models/Hello operations on detached validated provider snapshots. No automatic diagnostic is admitted
by open/load/save. Single-flight ownership blocks racing Save; editor/provider changes invalidate/cancel results,
and close/shutdown cancels and drains the owned Task before teardown. Late replies cannot publish models/status;
custom models survive discovery and statuses exclude secrets/response bodies. Existing ordinary translation
scheduling and its generation/drain boundary remain intact.

Word menus clone the action and retain immutable word/cue/slot/language/button/modifier provenance independently
of the active popup lookup. Right click opens a menu without starting a lookup or selection side effect. Optional
selection copying/last search retains the popup path; direct copy modes use one copy path. Auto-copy observes
actual nonempty changed subtitle text, uses current displayed-source values and Primary/Secondary/All selection,
and does not copy on construction, visibility-only updates or Settings Save. Masked/control/drag ownership stays
intact; HTTP(S) browser and clipboard effects use host seams.

Accepted parent close enters terminal Word admission before any diagnostic/browser drain await. Its cancellable
UI browser wait can retire independently of an uncooperative OS backend; late backend success/failure remains
observed without a late toast or ownership of the external browser. A modal child may veto before parent
`OnClosing`; the rejected attempt leaves the still-open parent usable, and a later accepted retry owns shutdown.

Hotkey source retains app-first dispatch and native Flyleaf keyboard handling. Explicit app rows can opt into
release execution; legacy rows keep immediate timing. The dispatcher owns physical presses, snapshots admitted
handlers and retains consume-only release ownership after focus/capture cancellation. Owned-child releases can
finish parent presses without admitting new child-origin playback presses; required engine releases still reach
the engine. Genuine deactivation cancels app execution; subsequent activation starts a new app/input epoch,
retiring canceled press ownership that would otherwise swallow the first fresh down after a missed external up.
Old unpaired app/input ups remain consume-only; genuine engine-held presses still await actual ups. No new TTL,
OS key-state queries, global hooks or synthetic engine releases are introduced. Terminal shutdown cannot reactivate
input. Custom engine binding objects/delegates are preserved. The missed-release guard expires armed release
work rather than suppressing eligible immediate repeats. Scoped runtime/UI reviews and focused regressions are
closed; full-stage gates remain pending in the current F-13 status. No decode/render, native
dependency or WPF input policy change is introduced by these UI additions.

## Plugins

- Plugin discovery is reflection-based.
- Extension points are interfaces in `PluginBase.cs`: `IOpen`, `IOpenSubtitles`, `ISuggest*`, `ISearch*`, `IScrapeItem`, and `IDownloadSubtitles`.
- Prefer new plugin/provider interfaces over adding special cases to `Player`.
- `YoutubeDL` manages an external process, watcher, temp folder, and stream suggestions. It needs process cleanup and race-aware file reads.

### Configured portable online extraction (2026-11-01)

- Preserve the WPF plugin's helper location, options, flags and separate publish flow. The portable plugin only admits explicit online requests; it must not claim ordinary HTTP(S) media or change direct playback when tools are absent or invalid.
- Standalone yt-dlp and Deno are external, explicitly configured absolute executables. Portable invocation uses structured arguments, isolated helper configuration/plugins/cache, an explicit Deno path and normal TLS verification. No arbitrary portable `ExtraArguments`, shell invocation, startup probes, download, update or PATH fallback.
- Each extraction owns immutable request identity, process, redirected streams and temporary state. Cancellation, a newer media request and shutdown interrupt stalled extraction and terminate its owned process tree with bounded waits. Cleanup never waits on callbacks while holding a UI-sensitive lock; late completion cannot publish to another request, including a repeat of the same URL.
- Descendant ownership outlives root exit: Windows starts the helper suspended, assigns a kill-on-close Job Object, then resumes it; Linux explicitly uses system `/usr/bin/setsid` (util-linux, no `--fork`) and terminates the owned process group. Missing containment prerequisites fail explicit extraction clearly, without installation or changing direct media playback.
- Reuse current format selection and metadata mapping, including HTTP headers, chapters and the existing VTT `automatic_captions` subset. Authored captions and broader playlist support are not added. Failures remain actionable and recoverable by opening other media.
- Deterministic/headless/package evidence is distinct from separately authorized native/live video-and-audio acceptance on Windows and Linux.
