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

- ASR uses independent audio demux/decode, 16 kHz WAV chunks, Whisper.net or faster-whisper process, cancellation, and shared dual-ASR behavior.
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
