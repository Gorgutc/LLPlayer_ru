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

## Media Framework

- Demuxers and decoders inherit thread lifecycle from `RunThreadBase`.
- Native packet/frame ownership is explicit. Do not bypass `PacketQueue` and frame disposal paths.
- Rendering has D3D/Flyleaf paths, device-lost handling, swapchain present, frame cache, and screen clear behavior.
- Any render loop or device change requires real Windows/WPF/DirectX smoke testing.

## Subtitles

- `SubtitlesManager.Subs` must remain sorted by `StartTime`; current/previous/next lookup depends on binary search.
- Text and bitmap subtitles are both first-class.
- Bitmap subtitle data has explicit lifetime/disposal and positioning.
- `SubtitlesSelectedHelper` is static/global and is not multi-player safe. Do not redesign it incidentally.

## ASR/OCR/Translation

- ASR uses independent audio demux/decode, 16 kHz WAV chunks, Whisper.net or faster-whisper process, cancellation, and shared dual-ASR behavior. Interactive ASR additionally supports pause/resume (F-04) via a cooperative async gate (`PauseTokenSource`/`PauseToken`): the consumer awaits the gate at a chunk boundary, so pausing keeps already-produced subtitles and back-pressures the bounded producer channel (no thread is blocked), while cancellation still clears subtitles. The gate is reset at the start of, and in the `finally` of, every `Execute` so a run never starts paused. Batch ASR passes a default (never-paused) token and is unaffected. The producer may optionally cut a chunk at a detected silent frame (RMS below `ASRSilenceRmsThreshold`, only after `ASRSilenceSoftFraction` of the size/elapsed budget) with the existing size/elapsed caps kept as a hard ceiling (T-09, `ASRSplitOnSilence`, default on) — this changes only *where* a chunk is cut, never the 16 kHz WAV format or the chunk `Start`/`End` contract, and applies to interactive and batch ASR. Interactive ASR may also fold back to transcribe the audio skipped before a mid-video start (T-08, `ASRFoldBack`, default off): the earlier span `[0..curTime)` is transcribed *first*, so cues are still emitted in increasing-time order and the append-only sorted-subtitle invariant is preserved; it only affects the interactive `curTime > 30s` seek path (batch passes `curTime = 0`, so fold-back never triggers). The producer may also optionally denoise the resampled audio before chunking (F-02, `ASRDenoise`, default off): a managed high-pass plus an FFmpeg `afftdn` stationary-noise filter is applied to the 16 kHz mono S16 PCM before it is written to the WAV chunk, so both ASR engines and batch see the cleaned audio. This changes only the audio *content* fed to Whisper, never the 16 kHz/mono/S16 WAV format, the chunk `Start`/`End` contract (timestamps stay frame-pts-derived), or the sorted-emission invariant; an end-of-pass flush drains the filter's lookahead tail so no audio is lost. The `afftdn` graph is a separate isolated avfilter graph built inside `AudioReader` (it does NOT touch the playback `AudioConfig.Filters` graph); if `afftdn` is unavailable in the shipped FFmpeg it degrades to the managed high-pass alone, and with denoise off the path is byte-identical. It targets stationary noise (hiss/hum/rumble), not speech/music separation.
- Batch ASR uses a headless media probe/transcriber path, not `Player.OpenAsync` and not the current interactive `OpenSubtitlesASR` command. It must keep ASR text in the source video language, disable Whisper built-in translate-to-English in its config snapshot, and perform Russian translation as a separate provider step. Because this path is headless (independent demuxers/decoders over a config snapshot, no live `Player`), a batch run is not tied to the main window: while a batch is active, closing the main video window keeps the run alive (the app stays in the tray) and the `Player` is disposed only once shutdown actually proceeds. (A normal no-batch close still disposes the `Player` directly before shutting down.)
- OCR services must preserve `TryInitialize -> Do -> Dispose`.
- Translation service creation is lazy and provider-based through `ITranslateService`, `ITranslateSettings`, `TranslateServiceType`, and `TranslateServiceFactory`.
- LLM-like translation has sequential/context modes; do not parallelize away context retention. Batch subtitle translation must keep `KeepContext` and `ContextWindow` sequential within each file. By default completed-file translation overlaps with ASR for the next file (pipelined throughput); a background-friendly "serialize ASR and translation" mode (default on in the app) instead runs each file fully through ASR → translate → save before the next file's ASR, so a GPU ASR engine and a local-LLM translator never both saturate the GPU at once.
- Batch can stay responsive during a long run via a per-chunk CPU fallback: `BatchAsrTranscriber` / `AudioReader` / `FasterWhisperASRService` accept an optional `Func<bool> preferCpu` (app supplies a Win32 idle check); when it returns true, the NEXT faster-whisper chunk is launched with the device forced to CPU (and GPU-only compute types remapped) instead of the configured GPU device. The chunk in flight always finishes on its chosen device, so nothing already computed is lost; switching only affects subsequent chunks. The interactive ASR path passes no policy (`null`), so its behaviour is unchanged. whisper.cpp (in-process) does not participate. This is additive and must not change ASR/translation results.

## Plugins

- Plugin discovery is reflection-based.
- Extension points are interfaces in `PluginBase.cs`: `IOpen`, `IOpenSubtitles`, `ISuggest*`, `ISearch*`, `IScrapeItem`, and `IDownloadSubtitles`.
- Prefer new plugin/provider interfaces over adding special cases to `Player`.
- `YoutubeDL` manages an external process, watcher, temp folder, and stream suggestions. It needs process cleanup and race-aware file reads.
