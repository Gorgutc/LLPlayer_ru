using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using CliWrap;
using CliWrap.Builders;
using CliWrap.EventStream;
using FlyleafLib.MediaFramework.MediaDecoder;
using FlyleafLib.MediaFramework.MediaDemuxer;
using FlyleafLib.MediaFramework.MediaStream;
using Whisper.net;
using Whisper.net.LibraryLoader;
using Whisper.net.Logger;
using static FlyleafLib.Logger;

namespace FlyleafLib.MediaPlayer;

#nullable enable

// TODO: L: Pause and resume ASR

/// <summary>
/// Running ASR from a media file
/// </summary>
/// <remarks>
/// Read in a separate thread from the video playback.
/// Note that multiple threads cannot seek to multiple locations for a single AVFormatContext,
/// so it is necessary to open it with another avformat_open_input for the same video.
/// </remarks>
public class SubtitlesASR
{
    private readonly SubtitlesManager _subtitlesManager;
    private readonly Config _config;
    private readonly Lock _locker = new();
    private readonly Lock _lockerSubs = new();
    private CancellationTokenSource? _cts = null;
    public HashSet<int> SubIndexSet { get; } = new();

    private readonly LogHandler Log;

    public SubtitlesASR(SubtitlesManager subtitlesManager, Config config)
    {
        _subtitlesManager = subtitlesManager;
        _config = config;

        Log = new LogHandler(("[#1]").PadRight(8, ' ') + " [SubtitlesASR  ] ");
    }

    // F-04: pause gate for the interactive ASR run. The consumer awaits this at a chunk boundary; pausing keeps
    // already-produced subtitles (unlike TryCancel, which clears them) and back-pressures the producer.
    private readonly PauseTokenSource _pauseSource = new();

    /// <summary>True while the interactive ASR run is paused at a chunk boundary (accumulated subtitles kept).</summary>
    public bool IsPaused => _pauseSource.IsPaused;

    /// <summary>
    /// Pause the interactive ASR run at the next chunk boundary, keeping already-produced subtitles. No-op when
    /// ASR is not running. A chunk is transcribed atomically, so the chunk in flight (incl. the external
    /// faster-whisper process) finishes first; the producer then back-pressures on the bounded channel.
    /// </summary>
    public void Pause()
    {
        CancellationTokenSource? cts = _cts;
        if (cts == null || cts.IsCancellationRequested)
            return; // not running

        _pauseSource.Pause();
        _config.Subtitles.player.IsASRPaused = true;
    }

    /// <summary>Resume the interactive ASR run after <see cref="Pause"/>. Idempotent / safe when not paused.</summary>
    public void Resume()
    {
        _pauseSource.Resume();
        _config.Subtitles.player.IsASRPaused = false;
    }

    /// <summary>
    /// Check that ASR is executable
    /// </summary>
    /// <param name="err">error information</param>
    /// <param name="actionHint">recoverable-action hint (see KnownErrorActionKeys); empty when none</param>
    /// <returns></returns>
    public bool CanExecute(out string err, out string actionHint)
    {
        actionHint = "";

        if (_config.Subtitles.ASREngine == SubASREngineType.WhisperCpp)
        {
            // whisper.cpp loads its native runtime in-process; without the VC++ redistributable that load
            // aborts the whole app (README). Preflight the CRT here so the user gets an actionable message
            // instead of a crash. faster-whisper is a self-contained external exe, so it is not checked.
            if (!VcRedistChecker.IsRuntimePresent(out _))
            {
                err = VcRedistChecker.BuildMissingMessage("Speech-to-text (whisper.cpp)");
                actionHint = KnownErrorActionKeys.InstallVcRedist;
                return false;
            }

            if (_config.Subtitles.WhisperCppConfig.Model == null)
            {
                err = "whisper.cpp model is not set. Please download it from the settings.";
                actionHint = KnownErrorActionKeys.DownloadWhisperModel;
                return false;
            }

            if (!File.Exists(_config.Subtitles.WhisperCppConfig.Model.ModelFilePath))
            {
                err = $"whisper.cpp model file '{_config.Subtitles.WhisperCppConfig.Model.ModelFileName}' does not exist in the folder. Please download it from the settings.";
                actionHint = KnownErrorActionKeys.DownloadWhisperModel;
                return false;
            }
        }
        else if (_config.Subtitles.ASREngine == SubASREngineType.FasterWhisper)
        {
            if (_config.Subtitles.FasterWhisperConfig.UseManualEngine)
            {
                if (!File.Exists(_config.Subtitles.FasterWhisperConfig.ManualEnginePath))
                {
                    err = "faster-whisper engine does not exist in the manual path.";
                    return false;
                }
            }
            else
            {
                if (!File.Exists(FasterWhisperConfig.DefaultEnginePath))
                {
                    err = "faster-whisper engine is not downloaded. Please download it from the settings.";
                    return false;
                }
            }

            if (_config.Subtitles.FasterWhisperConfig.UseManualModel)
            {
                if (!Directory.Exists(_config.Subtitles.FasterWhisperConfig.ManualModelDir))
                {
                    err = "faster-whisper manual model directory does not exist.";
                    return false;
                }
            }
        }

        err = "";

        return true;
    }

    /// <summary>
    /// Open media file and read all subtitle data from audio
    /// </summary>
    /// <param name="subIndex">0: Primary, 1: Secondary</param>
    /// <param name="url">media file path</param>
    /// <param name="streamIndex">Audio streamIndex</param>
    /// <param name="type">Demuxer type</param>
    /// <param name="curTime">Current playback timestamp, from which whisper is run</param>
    /// <returns>true: process completed, false: run in progress</returns>
    public bool Execute(int subIndex, string url, int streamIndex, MediaType type, TimeSpan curTime)
    {
        // When Dual ASR: Copy the other ASR result and return early
        if (SubIndexSet.Count > 0 && !SubIndexSet.Contains(subIndex))
        {
            lock (_lockerSubs)
            {
                SubIndexSet.Add(subIndex);
                int otherIndex = (subIndex + 1) % 2;

                if (_subtitlesManager[otherIndex].Subs.Count > 0)
                {
                    bool enableTranslated = _config.Subtitles[subIndex].EnabledTranslated;

                    // Copy other ASR result
                    _subtitlesManager[subIndex]
                        .Load(_subtitlesManager[otherIndex].Subs.Select(s =>
                        {
                            SubtitleData clone = s.Clone();

                            if (!enableTranslated)
                            {
                                clone.TranslatedText = null;
                                clone.EnabledTranslated = true;
                            }

                            return clone;
                        }));

                    if (!_subtitlesManager[otherIndex].IsLoading)
                    {
                        // Copy the language source if one of them is already done.
                        _subtitlesManager[subIndex].LanguageSource = _subtitlesManager[otherIndex].LanguageSource;
                    }
                }
            }

            // return early
            return false;
        }

        // If it has already been executed, cancel it to start over from the current playback position.
        if (SubIndexSet.Contains(subIndex))
        {
            Dictionary<int, List<SubtitleData>> prevSubs = new();
            HashSet<int> prevSubIndexSet = [.. SubIndexSet];
            lock (_lockerSubs)
            {
                // backup current result
                foreach (int i in SubIndexSet)
                {
                    prevSubs[i] = _subtitlesManager[i].Subs.ToList();
                }
            }
            // Cancel preceding execution and wait
            TryCancel(true);

            // restore previous result
            lock (_lockerSubs)
            {
                foreach (int i in prevSubIndexSet)
                {
                    _subtitlesManager[i].Load(prevSubs[i]);
                    // Re-enable spinner
                    _subtitlesManager[i].StartLoading();

                    SubIndexSet.Add(i);
                }
            }
        }

        lock (_locker)
        {
            SubIndexSet.Add(subIndex);

            // UI status flag (e.g. the ASR chip): true only while actively transcribing. Reset in the
            // finally below so it always clears on completion, cancellation, or error.
            _config.Subtitles.player.IsASRRunning = true;
            try
            {
            _cts = new CancellationTokenSource();
            // A fresh run must never start paused (e.g. left paused before a seek-triggered restart).
            _pauseSource.Resume();
            _config.Subtitles.player.IsASRPaused = false;
            using AudioReader reader = new(_config, subIndex);
            reader.Open(url, streamIndex, type, _cts.Token);

            if (_cts.Token.IsCancellationRequested)
            {
                return true;
            }

            reader.ReadAll(curTime, data =>
            {
                if (_cts.Token.IsCancellationRequested)
                {
                    return;
                }

                lock (_lockerSubs)
                {
                    // F-18: normalize an ALL-CAPS ASR artifact (a faster-whisper(-XXL) quirk) to sentence-case
                    // before re-segmentation. Gated; a non-all-caps cue is returned unchanged.
                    string? asrText = data.Text;
                    if (_config.Subtitles.FixAllCaps && !string.IsNullOrEmpty(asrText))
                        asrText = SubtitleCaseFixer.FixAllCaps(asrText);

                    // Re-segment an over-long ASR cue into short, capped-line cues (proportional timings) so
                    // a single subtitle does not fill the frame. Gated by the config toggle; cues that already
                    // fit pass through unchanged.
                    List<(string Text, TimeSpan Start, TimeSpan End)> cues = _config.Subtitles.ResegmentSubtitles
                        ? SubtitleSegmenter.Resegment(asrText, data.StartTime, data.EndTime, _config.Subtitles.SubtitleSegmentOptions)
                        : [(asrText, data.StartTime, data.EndTime)];

                    foreach (int i in SubIndexSet)
                    {
                        bool isInit = false;
                        if (_subtitlesManager[i].LanguageSource == null)
                        {
                            isInit = true;

                            // Delete subtitles after the first subtitle to be added (leave the previous one)
                            _subtitlesManager[i].DeleteAfter(data.StartTime);

                            // Set language
                            // Can currently only be set for the whole, not per subtitle
                            _subtitlesManager[i].LanguageSource = Language.Get(data.Language);
                        }

                        foreach ((string text, TimeSpan startTime, TimeSpan endTime) in cues)
                        {
                            SubtitleData sub = new()
                            {
                                Text = text,
                                StartTime = startTime,
                                EndTime = endTime,
                                // Per-cue source language (T-10). With per-segment detection off this mirrors the
                                // pinned transcript language; on, it is the segment's own auto-detected language.
                                Language = Language.Get(data.Language),
#if DEBUG
                                ChunkNo = data.ChunkNo,
                                StartTimeChunk = data.StartTimeChunk,
                                EndTimeChunk = data.EndTimeChunk,
#endif
                            };

                            _subtitlesManager[i].Add(sub);
                        }

                        if (isInit)
                        {
                            _subtitlesManager[i].SetCurrentTime(new TimeSpan(_config.Subtitles.player.CurTime));
                        }
                    }
                }
            }, _cts.Token, _pauseSource.Token);

            if (!_cts.Token.IsCancellationRequested)
            {
                Utils.PlayCompletionSound();
                // Also surface a non-blocking visual confirmation (the UI subscribes to ASRCompleted).
                _config.Subtitles.player.RaiseASRCompleted();
            }

            foreach (int i in SubIndexSet)
            {
                lock (_lockerSubs)
                {
                    // Stop spinner (required when dual ASR)
                    _subtitlesManager[i].StartLoading().Dispose();
                }
            }
            }
            finally
            {
                _config.Subtitles.player.IsASRRunning = false;
                // Always clear the paused state on completion/cancellation/error so the next run starts clean.
                _pauseSource.Resume();
                _config.Subtitles.player.IsASRPaused = false;
            }
        }

        return true;
    }

    public void TryCancel(bool isWait)
    {
        var cts = _cts;
        if (cts != null)
        {
            if (!cts.IsCancellationRequested)
            {
                lock (_lockerSubs)
                {
                    foreach (var i in SubIndexSet)
                    {
                        _subtitlesManager[i].Clear();
                    }
                }

                cts.Cancel();
                lock (_lockerSubs)
                {
                    SubIndexSet.Clear();
                }
            }
            else
            {
                Log.Info("Already cancel requested");
            }

            if (!isWait)
            {
                return;
            }

            lock (_locker)
            {
                // dispose after it is no longer used.
                cts.Dispose();
                _cts = null;
            }
        }
    }

    public void Reset(int subIndex)
    {
        if (!SubIndexSet.Contains(subIndex))
            return;

        if (SubIndexSet.Count == 2)
        {
            lock (_lockerSubs)
            {
                // When Dual ASR: only the state is cleared without stopping ASR execution.
                SubIndexSet.Remove(subIndex);
                _subtitlesManager[subIndex].Clear();
            }

            return;
        }

        // cancel asynchronously as it takes time to cancel.
        TryCancel(false);
    }
}

public class AudioReader : IDisposable
{
    private readonly Config _config;
    private readonly int _subIndex;
    private readonly Func<bool>? _preferCpu;

    private Demuxer? _demuxer;
    private AudioDecoder? _decoder;
    private AudioStream? _stream;

    private unsafe AVPacket* _packet = null;
    private unsafe AVFrame* _frame = null;
    private unsafe SwrContext* _swrContext = null;

    private bool _isFile;

    private readonly LogHandler Log;

    /// <param name="preferCpu">Optional per-chunk device policy for the faster-whisper engine (batch only):
    /// when it returns true a chunk is transcribed on CPU instead of the configured GPU device. Null keeps
    /// the configured device (the interactive ASR path passes nothing, so its behaviour is unchanged).</param>
    public AudioReader(Config config, int subIndex, Func<bool>? preferCpu = null)
    {
        _config = config;
        _subIndex = subIndex;
        _preferCpu = preferCpu;
        Log = new LogHandler(("[#1]").PadRight(8, ' ') + " [AudioReader   ] ");
    }

    public void Open(string url, int streamIndex, MediaType type, CancellationToken token)
    {
        _demuxer = new Demuxer(_config.Demuxer, type, _subIndex + 1, false);

        token.Register(() =>
        {
            if (_demuxer != null)
                _demuxer.Interrupter.ForceInterrupt = 1;
        });

        _demuxer.Log.Prefix = _demuxer.Log.Prefix.Replace("Demuxer: ", "DemuxerA:");
        string? error = _demuxer.Open(url);

        if (error != null)
        {
            if (token.IsCancellationRequested)
                return;

            throw new InvalidOperationException($"demuxer open error: {error}");
        }

        _stream = (AudioStream)_demuxer.AVStreamToStream[streamIndex];

        _decoder = new AudioDecoder(_config, _subIndex + 1);
        _decoder.Log.Prefix = _decoder.Log.Prefix.Replace("Decoder: ", "DecoderA:");

        if (!_decoder.Open(_stream))
        {
            if (token.IsCancellationRequested)
                return;

            throw new InvalidOperationException($"decoder open error");
        }

        _isFile = File.Exists(url);
    }

    private record struct AudioChunk(MemoryStream Stream, int ChunkNumber, TimeSpan Start, TimeSpan End);

    /// <summary>
    /// Extract audio files in WAV format and run Whisper
    /// </summary>
    /// <param name="curTime">Current playback timestamp, from which whisper is run</param>
    /// <param name="addSub">Action to process one result</param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    /// <exception cref="OperationCanceledException"></exception>
    public void ReadAll(TimeSpan curTime, Action<SubtitleASRData> addSub, CancellationToken cancellationToken, PauseToken pauseToken = default)
    {
        if (_demuxer == null || _decoder == null || _stream == null)
            throw new InvalidOperationException("Open() is not called");

        // Assume a network stream and parallelize the reading of packets and the execution of whisper.
        // For network video, increase capacity as downloads may take longer.
        // (concern that memory usage will increase by three times the chunk size)
        int capacity = _isFile ? 1 : 2;
        BoundedChannelOptions channelOptions = new(capacity)
        {
            SingleReader = true,
            SingleWriter = true,
        };
        Channel<AudioChunk> channel = Channel.CreateBounded<AudioChunk>(channelOptions);

        // own cancellation for producer/consumer
        // HC-19: this linked CTS registers a callback on the parent token; dispose it in the finally below,
        // AFTER the OnlyOnFaulted continuations that call cts.Cancel() have reached a terminal state, so a
        // Cancel() can never race the Dispose().
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        CancellationToken token = cts.Token;

        ConcurrentStack<MemoryStream> memoryStreamPool = new();

        // Consumer: Run whisper
        Task consumerTask = Task.Run(DoConsumer, token);

        // Producer: Extract WAV and pass to consumer
        Task producerTask = Task.Run(DoProducer, token);

        // complete channel
        producerTask.ContinueWith(t =>
            channel.Writer.Complete(), token);

        // When an exception occurs in both consumer and producer, the other is canceled.
        Task faultCancelConsumer = consumerTask.ContinueWith(t =>
            cts.Cancel(), TaskContinuationOptions.OnlyOnFaulted);
        Task faultCancelProducer = producerTask.ContinueWith(t =>
            cts.Cancel(), TaskContinuationOptions.OnlyOnFaulted);

        try
        {
            Task.WhenAll(consumerTask, producerTask).Wait();
        }
        catch (AggregateException)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                // canceled by caller
                if (CanDebug) Log.Debug("Whisper canceled");
                return;
            }

            // canceled because of exceptions
            throw;
        }
        finally
        {
            // Let the fault-cancel continuations settle (each either ran cts.Cancel() or was Canceled because its
            // task did not fault) before disposing, so no Cancel() runs against a disposed CTS.
            try { Task.WaitAll(faultCancelConsumer, faultCancelProducer); } catch { /* not-run => Canceled; ignore */ }
            cts.Dispose();
        }

        return;

        async Task DoConsumer()
        {
            await using IASRService asrService = _config.Subtitles.ASREngine switch
            {
                SubASREngineType.WhisperCpp => new WhisperCppASRService(_config),
                SubASREngineType.FasterWhisper => new FasterWhisperASRService(_config, _preferCpu),
                _ => throw new InvalidOperationException()
            };

            // Track the previous emitted segment to drop consecutive duplicate segments produced by
            // whisper repetition loops (see the dedup check below).
            string? lastText = null;
            TimeSpan lastEnd = TimeSpan.MinValue;

            while (await channel.Reader.WaitToReadAsync(token))
            {
                // F-04 pause boundary: a chunk is transcribed atomically, so pause takes effect BETWEEN chunks.
                // If paused, suspend here until resumed or cancelled, keeping already-produced subtitles; the
                // producer back-pressures on the bounded channel meanwhile. A default PauseToken (batch) no-ops.
                await pauseToken.WaitWhilePausedAsync(token);

                // Use TryPeek() to reduce the channel capacity by one.
                if (!channel.Reader.TryPeek(out AudioChunk chunk))
                    throw new InvalidOperationException("can not peek AudioChunk from channel");

                try
                {
                    if (CanDebug) Log.Debug(
                            $"Reading chunk from channel (chunkNo: {chunk.ChunkNumber}, start: {chunk.Start}, end: {chunk.End})");

                    //// Output wav file for debugging
                    //await using (FileStream fs = new($"subtitlewhisper-{chunk.ChunkNumber}.wav", FileMode.Create, FileAccess.Write))
                    //{
                    //    chunk.Stream.WriteTo(fs);
                    //    chunk.Stream.Position = 0;
                    //}

                    await foreach (var data in asrService.Do(chunk.Stream, token))
                    {
                        TimeSpan start = chunk.Start.Add(data.start);
                        TimeSpan end = chunk.Start.Add(data.end);

                        // Drop a hallucinated tail whose start is already at/after the chunk boundary
                        // (whisper sometimes emits trailing segments timestamped outside the audio).
                        if (start >= chunk.End)
                        {
                            continue;
                        }

                        if (end > chunk.End)
                        {
                            // Shorten by 20 ms to prevent the next subtitle from being covered
                            end = chunk.End.Subtract(TimeSpan.FromMilliseconds(20));
                        }

                        // Guarantee a positive duration so the subtitle remains searchable/visible
                        // (the clamp above could otherwise push end before start).
                        if (end <= start)
                        {
                            end = start.Add(TimeSpan.FromMilliseconds(1));
                        }

                        // Prevent adjacent segments from the same chunk from overlapping: whisper can emit
                        // a start earlier than the previous emitted segment's end. Clamp start up to the
                        // previous end. This runs before the duplicate check below (so a same-text overlap
                        // is still caught as a duplicate) and never drops a segment.
                        if (lastText != null && start < lastEnd)
                        {
                            start = lastEnd;
                            if (end <= start)
                            {
                                end = start.Add(TimeSpan.FromMilliseconds(1));
                            }
                        }

                        // Drop consecutive duplicate segments that overlap or are immediately adjacent:
                        // these are whisper repetition-loop artifacts, not genuine repeated lines (a
                        // real repeat is separated in time, so it is preserved).
                        if (lastText == data.text && start <= lastEnd.Add(TimeSpan.FromMilliseconds(200)))
                        {
                            if (CanDebug) Log.Debug($"Skipping duplicate ASR segment: {data.text}");
                            continue;
                        }

                        lastText = data.text;
                        lastEnd = end;

                        SubtitleASRData subData = new()
                        {
                            Text = data.text,
                            Language = data.language,
                            StartTime = start,
                            EndTime = end,
#if DEBUG
                            ChunkNo = chunk.ChunkNumber,
                            StartTimeChunk = chunk.Start,
                            EndTimeChunk = chunk.End
#endif
                        };

                        if (CanDebug) Log.Debug(string.Format("{0}->{1} ({2}->{3}): {4}",
                            start, end,
                            chunk.Start, chunk.End,
                            data.text));

                        addSub(subData);
                    }
                }
                finally
                {
                    chunk.Stream.SetLength(0);
                    memoryStreamPool.Push(chunk.Stream);

                    if (!channel.Reader.TryRead(out _))
                        throw new InvalidOperationException("can not discard AudioChunk from channel");
                }
            }
        }

        unsafe void DoProducer()
        {
            _packet = av_packet_alloc();
            _frame = av_frame_alloc();

            // When passing the audio file to Whisper, it must be converted to a 16000 sample rate WAV file.
            // For this purpose, the ffmpeg API is used to perform the conversion.
            // Audio files are divided by a certain size, stored in memory, and passed by memory stream.
            int targetSampleRate = 16000;
            int targetChannel = 1;
            const int waveHeaderSize = 44;

            // Stream processing is performed by dividing the audio by a certain size and passing it to whisper.
            long chunkSize = _config.Subtitles.ASRChunkSize;
            // Also split by elapsed seconds for live
            TimeSpan chunkElapsed = TimeSpan.FromSeconds(_config.Subtitles.ASRChunkSeconds);

            // T-09: prefer to cut a chunk at a silent frame instead of strictly at the cap (read tunables once).
            bool splitOnSilence = _config.Subtitles.ASRSplitOnSilence;
            double silenceSoftFraction = _config.Subtitles.ASRSilenceSoftFraction;
            double silenceRmsThreshold = _config.Subtitles.ASRSilenceRmsThreshold;

            // F-02: opt-in denoise (managed high-pass + optional native afftdn). Read once; off by default → the
            // ResampleTo write stays byte-identical.
            _denoiseEnabled = _config.Subtitles.ASRDenoise;
            _highPass = _denoiseEnabled ? new AsrHighPassFilter(targetSampleRate) : null;

            // Producer state shared across passes (mutated inside RunPass).
            MemoryStream waveStream = new(); // MemoryStream does not need to be disposed for releasing memory
            TimeSpan waveDuration = TimeSpan.Zero; // for logging
            Stopwatch chunkSw = new();
            int chunkCnt = 0;
            TimeSpan? chunkStart = null;
            long framePts = NoTs;
            int demuxErrors = 0;
            int decodeErrors = 0;

            // T-08 fold-back: when ASR starts mid-video, transcribe the skipped earlier span FIRST (so cues are still
            // emitted in increasing-time order, keeping the append-only subtitle store sorted), then the forward span.
            // When fold-back is off this is a single forward pass from curTime, byte-identical to before.
            (bool foldBack, TimeSpan floor) = AsrFoldback.Plan(curTime, _config.Subtitles.ASRFoldBack, TimeSpan.FromSeconds(30));

            bool backfilled = false;

            if (curTime > TimeSpan.FromSeconds(30))
            {
                // copy from DecoderContext.CalcSeekTimestamp()
                long startTime = _demuxer.hlsCtx == null ? _demuxer.StartTime : _demuxer.hlsCtx->first_timestamp * 10;

                if (foldBack)
                {
                    // Backfill [floor .. curTime): seek to the stream start, transcribe until we reach curTime.
                    if (_demuxer.Seek(startTime + floor.Ticks, true) >= 0)
                    {
                        RunPass(curTime);
                        backfilled = true;
                    }
                    else if (CanWarn)
                    {
                        Log.Warn("ASR fold-back: seek to start failed, skipping backfill pass");
                    }
                }

                if (!backfilled)
                {
                    // Seek to the current playback position (the original mid-video start behavior).
                    long ticks = curTime.Ticks + startTime;

                    bool forward = false;

                    if (_demuxer.Type == MediaType.Audio) ticks -= _config.Audio.Delay;

                    if (ticks < startTime)
                    {
                        ticks = startTime;
                        forward = true;
                    }
                    else if (ticks > startTime + _demuxer.Duration - (50 * 10000))
                    {
                        ticks = Math.Max(startTime, startTime + _demuxer.Duration - (50 * 10000));
                        forward = false;
                    }

                    _ = _demuxer.Seek(ticks, forward);
                }

                // When the backfill pass ran, the demuxer is already positioned right after curTime, so the forward
                // pass continues CONTIGUOUSLY from there with NO re-seek: it never re-transcribes [keyframe..curTime)
                // (no duplicate cues at the seam) and every forward cue starts after the last backfill cue, so the
                // append-only subtitle store stays sorted by construction rather than relying on the consumer clamp.
            }

            // Forward pass: from curTime to EOF, or — after a backfill pass — contiguously from where it stopped.
            RunPass(null);

            return;

            // Transcribe from the demuxer's current position until EOF, or — for a backfill pass — until the frame
            // time reaches stopAt. Resets chunk state at entry so it is safe to invoke twice over the one channel.
            unsafe void RunPass(TimeSpan? stopAt)
            {
                // Start each pass with a fresh, header-only WAV buffer (never the one just handed to the consumer).
                if (memoryStreamPool.TryPop(out var pooled))
                    waveStream = pooled;
                else
                    waveStream = new MemoryStream();
                waveStream.SetLength(0);
                WriteWavHeader(waveStream, targetSampleRate, targetChannel);

                // F-02: reset the denoise filter state so one pass cannot bleed into the next (T-08 runs two passes).
                _highPass?.Reset();
                DenoiseResetForPass();

                waveDuration = TimeSpan.Zero;
                chunkStart = null;
                framePts = NoTs;
                demuxErrors = 0;
                decodeErrors = 0;
                chunkSw.Restart();

                bool stop = false;

                while (!stop && !token.IsCancellationRequested)
                {
                    _demuxer.Interrupter.ReadRequest();
                    int ret = av_read_frame(_demuxer.fmtCtx, _packet);

                    if (ret != 0)
                    {
                        av_packet_unref(_packet);

                        if (_demuxer.Interrupter.Timedout)
                        {
                            if (token.IsCancellationRequested)
                                break;

                            ret.ThrowExceptionIfError("av_read_frame (timed out)");
                        }

                        if (ret == AVERROR_EOF || token.IsCancellationRequested)
                        {
                            break;
                        }

                        // demux error
                        if (CanWarn) Log.Warn($"av_read_frame: {FFmpegEngine.ErrorCodeToMsg(ret)} ({ret})");

                        if (++demuxErrors == _config.Demuxer.MaxErrors)
                        {
                            ret.ThrowExceptionIfError("av_read_frame");
                        }
                        continue;
                    }

                    // Discard all but the selected audio stream.
                    if (_packet->stream_index != _stream.StreamIndex)
                    {
                        av_packet_unref(_packet);
                        continue;
                    }

                    ret = avcodec_send_packet(_decoder.CodecCtx, _packet);
                    av_packet_unref(_packet);

                    if (ret != 0)
                    {
                        if (ret == AVERROR(EAGAIN))
                        {
                            // Receive_frame and send_packet both returned EAGAIN, which is an API violation.
                            ret.ThrowExceptionIfError("avcodec_send_packet (EAGAIN)");
                        }

                        // decoder error
                        if (CanWarn) Log.Warn($"avcodec_send_packet: {FFmpegEngine.ErrorCodeToMsg(ret)} ({ret})");

                        if (++decodeErrors == _config.Decoder.MaxErrors)
                        {
                            ret.ThrowExceptionIfError("avcodec_send_packet");
                        }

                        continue;
                    }

                    while (ret >= 0)
                    {
                        ret = avcodec_receive_frame(_decoder.CodecCtx, _frame);
                        if (ret == AVERROR(EAGAIN) || ret == AVERROR_EOF)
                        {
                            break;
                        }
                        ret.ThrowExceptionIfError("avcodec_receive_frame");

                        if (_frame->best_effort_timestamp != NoTs)
                        {
                            framePts = _frame->best_effort_timestamp;
                        }
                        else if (_frame->pts != NoTs)
                        {
                            framePts = _frame->pts;
                        }
                        else
                        {
                            // Certain encoders sometimes cannot get pts (APE, Musepack)
                            framePts += _frame->duration;
                        }

                        waveDuration = waveDuration.Add(new TimeSpan((long)(_frame->duration * _stream.Timebase)));

                        if (chunkStart == null)
                        {
                            chunkStart = new TimeSpan((long)(framePts * _stream.Timebase) - _demuxer.StartTime);
                            if (chunkStart.Value.Ticks < 0)
                            {
                                // Correct to 0 if negative
                                chunkStart = new TimeSpan(0);
                            }
                        }

                        int resampledDataSize = ResampleTo(waveStream, _frame, targetSampleRate, targetChannel);

                        // Cut a chunk when: (a) a hard size/time cap is hit (original behavior, always the ceiling);
                        // (b) T-09 — past the soft fraction of the budget AND this frame is silent (cleaner phrase
                        // boundary); or (c) T-08 — a backfill pass reached its stop time. The hard cap guarantees a
                        // cut always fires, so back-pressure / EOF-tail semantics are unchanged.
                        TimeSpan frameTime = new((long)(framePts * _stream.Timebase) - _demuxer.StartTime);
                        bool reachedStop = AsrFoldback.ReachedStop(frameTime, stopAt);
                        bool hardCap = waveStream.Length >= chunkSize || chunkSw.Elapsed >= chunkElapsed;
                        bool softCut = splitOnSilence
                            && resampledDataSize > 0
                            && AsrSilence.IsSoftReady(waveStream.Length, chunkSize, chunkSw.Elapsed, chunkElapsed, silenceSoftFraction)
                            && AsrSilence.IsSilent(_sampledBuf, resampledDataSize, silenceRmsThreshold);

                        if (hardCap || softCut || reachedStop)
                        {
                            // F-02 + T-08: on the pass-ENDING (reachedStop) cut, flush afftdn's buffered lookahead tail
                            // INTO this chunk before it is emitted. Otherwise framePts is reset below and the end-of-pass
                            // flush's bytes get discarded by the tail-chunk guard (framePts != NoTs), dropping audio at
                            // the backfill->forward seam. Mid-pass cuts (hardCap/softCut) must NOT flush (that would reset
                            // afftdn state and break continuity at every chunk boundary).
                            if (reachedStop)
                                DenoiseFlush(waveStream);

                            TimeSpan chunkEnd = new TimeSpan((long)(framePts * _stream.Timebase) - _demuxer.StartTime);
                            chunkCnt++;

                            if (CanInfo) Log.Info(
                                $"Process chunk (chunkNo: {chunkCnt}, sizeMB: {waveStream.Length / 1024 / 1024}, duration: {waveDuration}, elapsed: {chunkSw.Elapsed})");

                            UpdateWavHeader(waveStream);

                            AudioChunk chunk = new(waveStream, chunkCnt, chunkStart.Value, chunkEnd);

                            if (CanDebug) Log.Debug($"Writing chunk to channel ({chunkCnt})");
                            // if channel capacity reached, it will be waited
                            channel.Writer.WriteAsync(chunk, token).AsTask().Wait(token);
                            if (CanDebug) Log.Debug($"Done writing chunk to channel ({chunkCnt})");

                            if (memoryStreamPool.TryPop(out var stream))
                                waveStream = stream;
                            else
                                waveStream = new MemoryStream();

                            WriteWavHeader(waveStream, targetSampleRate, targetChannel);
                            waveDuration = TimeSpan.Zero;

                            chunkStart = null;
                            chunkSw.Restart();
                            framePts = NoTs;

                            // T-08: a backfill pass has covered up to its stop point — end this pass.
                            if (reachedStop)
                            {
                                stop = true;
                                break;
                            }
                        }
                    }
                }

                token.ThrowIfCancellationRequested();

                // F-02: flush the afftdn lookahead tail into the current chunk so the pass loses no audio.
                DenoiseFlush(waveStream);

                // Process remaining (this pass's tail chunk).
                if (waveStream.Length > waveHeaderSize && framePts != NoTs)
                {
                    TimeSpan chunkEnd = new TimeSpan((long)(framePts * _stream.Timebase) - _demuxer.StartTime);

                    chunkCnt++;

                    if (CanInfo) Log.Info(
                        $"Process last chunk (chunkNo: {chunkCnt}, sizeMB: {waveStream.Length / 1024 / 1024}, duration: {waveDuration}, elapsed: {chunkSw.Elapsed})");

                    UpdateWavHeader(waveStream);

                    AudioChunk chunk = new(waveStream, chunkCnt, chunkStart!.Value, chunkEnd);

                    if (CanDebug) Log.Debug($"Writing last chunk to channel ({chunkCnt})");
                    channel.Writer.WriteAsync(chunk, token).AsTask().Wait(token);
                    if (CanDebug) Log.Debug($"Done writing last chunk to channel ({chunkCnt})");
                }
            }
        }
    }

    private static void WriteWavHeader(Stream stream, int sampleRate, int channels)
    {
        using BinaryWriter writer = new(stream, Encoding.UTF8, true);
        writer.Write(['R', 'I', 'F', 'F']);
        writer.Write(0); // placeholder for file size
        writer.Write(['W', 'A', 'V', 'E']);
        writer.Write(['f', 'm', 't', ' ']);
        writer.Write(16); // PCM header size
        writer.Write((short)1); // PCM format
        writer.Write((short)channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels * 2); // Byte rate
        writer.Write((short)(channels * 2)); // Block align
        writer.Write((short)16); // Bits per sample
        writer.Write(['d', 'a', 't', 'a']);
        writer.Write(0); // placeholder for data size
    }

    private static void UpdateWavHeader(Stream stream)
    {
        long fileSize = stream.Length;
        stream.Seek(4, SeekOrigin.Begin);
        stream.Write(BitConverter.GetBytes((int)(fileSize - 8)), 0, 4);
        stream.Seek(40, SeekOrigin.Begin);
        stream.Write(BitConverter.GetBytes((int)(fileSize - 44)), 0, 4);
        stream.Position = 0;
    }

    private byte[] _sampledBuf = [];
    private int _sampledBufSize;

    // F-02 opt-in ASR denoise: managed high-pass over the resampled S16 mono 16k PCM (the testable core), applied in
    // ResampleTo before the WAV write. _denoiseEnabled mirrors Config.Subtitles.ASRDenoise (read once). The optional
    // native afftdn stage lives in the _denoiseGraph* fields below.
    private bool _denoiseEnabled;
    private AsrHighPassFilter? _highPass;

    // F-02 optional native FFmpeg afftdn stage (built lazily per ASR pass, drained/freed at pass end). Fixed format
    // s16/mono/16k in and out, so no codec-change reinit is needed. Degrades to managed-high-pass-only on failure.
    private unsafe AVFilterGraph*   _denoiseGraph   = null;
    private unsafe AVFilterContext* _denoiseSrcCtx  = null;
    private unsafe AVFilterContext* _denoiseSinkCtx = null;
    private unsafe AVFrame*         _denoiseInFrame = null;
    private unsafe AVFrame*         _denoiseOutFrame= null;
    private bool                    _denoiseAfftdnFailed;

    // for codec change detection
    private int _lastFormat;
    private int _lastSampleRate;
    private ulong _lastChannelLayout;

    /// <summary>Resamples one decoded frame to S16 mono <paramref name="targetSampleRate"/> Hz, appends it to
    /// <paramref name="toStream"/>, and returns the number of PCM bytes written (also left in <see cref="_sampledBuf"/>
    /// for silence detection, T-09).</summary>
    private unsafe int ResampleTo(Stream toStream, AVFrame* frame, int targetSampleRate, int targetChannel)
    {
        bool codecChanged = false;

        if (_lastFormat != frame->format)
        {
            _lastFormat = frame->format;
            codecChanged = true;
        }
        if (_lastSampleRate != frame->sample_rate)
        {
            _lastSampleRate = frame->sample_rate;
            codecChanged = true;
        }
        if (_lastChannelLayout != frame->ch_layout.u.mask)
        {
            _lastChannelLayout = frame->ch_layout.u.mask;
            codecChanged = true;
        }

        // Reinitialize SwrContext because codec changed
        // Note that native error will occur if not reinitialized.
        // Reference: AudioDecoder::RunInternal
        if (_swrContext != null && codecChanged)
        {
            fixed (SwrContext** ptr = &_swrContext)
            {
                swr_free(ptr);
            }
            _swrContext = null;
        }

        if (_swrContext == null)
        {
            AVChannelLayout outLayout;
            av_channel_layout_default(&outLayout, targetChannel);

            // NOTE: important to reuse this context
            fixed (SwrContext** ptr = &_swrContext)
            {
                swr_alloc_set_opts2(
                    ptr,
                    &outLayout,
                    AVSampleFormat.S16,
                    targetSampleRate,
                    &frame->ch_layout,
                    (AVSampleFormat)frame->format,
                    frame->sample_rate,
                    0, null)
                    .ThrowExceptionIfError("swr_alloc_set_opts2");

                swr_init(_swrContext)
                    .ThrowExceptionIfError("swr_init");
            }
        }

        // ffmpeg ref: https://github.com/FFmpeg/FFmpeg/blob/504df09c34607967e4109b7b114ee084cf15a3ae/libavfilter/af_aresample.c#L171-L227
        double ratio = targetSampleRate * 1.0 / frame->sample_rate; // 16000:44100=0.36281179138321995
        int nOut = (int)(frame->nb_samples * ratio) + 32;

        long delay = swr_get_delay(_swrContext, targetSampleRate);
        if (delay > 0)
        {
            nOut += (int)Math.Min(delay, Math.Max(4096, nOut));
        }
        int needed = nOut * targetChannel * sizeof(ushort);

        if (_sampledBufSize < needed)
        {
            _sampledBuf = new byte[needed];
            _sampledBufSize = needed;
        }

        int samplesPerChannel;

        fixed (byte* dst = _sampledBuf)
        {
            samplesPerChannel = swr_convert(
                 _swrContext,
                 &dst,
                 nOut,
                 frame->extended_data,
                 frame->nb_samples);
        }
        samplesPerChannel.ThrowExceptionIfError("swr_convert");

        int resampledDataSize = samplesPerChannel * targetChannel * sizeof(ushort);

        // F-02: managed high-pass in place over the resampled S16 mono PCM (size-preserving, so the T-09 silence
        // contract and resampledDataSize are unchanged). T-09 then reads the high-passed _sampledBuf.
        if (_denoiseEnabled && resampledDataSize > 0)
            _highPass?.ProcessInPlace(_sampledBuf, resampledDataSize);

        // The optional native afftdn stage (when available) writes its own output to toStream and returns; otherwise
        // (off, or afftdn unavailable) the high-passed/raw PCM is written here.
        if (DenoiseAfftdnWrite(toStream, resampledDataSize))
            return resampledDataSize;

        toStream.Write(_sampledBuf, 0, resampledDataSize);

        return resampledDataSize;
    }

    // --- F-02 optional native afftdn denoise stage (mirrors AudioDecoder.Filters.cs avfilter pattern) ---

    /// <summary>Pushes the high-passed S16/mono/16k PCM through the afftdn graph and writes its output to
    /// <paramref name="toStream"/>. Returns true when the afftdn stage handled the write; false when the caller should
    /// write the high-passed/raw bytes itself (denoise off, afftdn unavailable, or a failure → managed-only fallback).</summary>
    private unsafe bool DenoiseAfftdnWrite(Stream toStream, int resampledDataSize)
    {
        if (!_denoiseEnabled || _denoiseAfftdnFailed)
            return false;

        if (resampledDataSize <= 0)
            return true; // nothing to push (empty resample / afftdn warm-up): write nothing, same as before

        if (_denoiseGraph == null)
        {
            SetupDenoiseGraph();
            if (_denoiseAfftdnFailed)
                return false; // afftdn not available → fall back to managed high-pass only
        }

        try
        {
            av_frame_unref(_denoiseInFrame);
            _denoiseInFrame->format      = (int)AVSampleFormat.S16;
            _denoiseInFrame->sample_rate = 16000;
            av_channel_layout_default(&_denoiseInFrame->ch_layout, 1);
            _denoiseInFrame->nb_samples  = resampledDataSize / 2;
            av_frame_get_buffer(_denoiseInFrame, 0).ThrowExceptionIfError("denoise frame buffer");

            _sampledBuf.AsSpan(0, resampledDataSize).CopyTo(new Span<byte>((void*)_denoiseInFrame->data[0], resampledDataSize));

            av_buffersrc_add_frame_flags(_denoiseSrcCtx, _denoiseInFrame, AVBuffersrcFlag.KeepRef)
                .ThrowExceptionIfError("denoise buffersrc");

            DrainDenoise(toStream);
            return true;
        }
        catch (Exception e)
        {
            if (CanWarn) Log.Warn($"ASR denoise (afftdn) failed mid-stream, falling back to high-pass only: {e.Message}");
            _denoiseAfftdnFailed = true;
            DisposeDenoise();
            return false;
        }
    }

    private unsafe void DrainDenoise(Stream toStream)
    {
        while (av_buffersink_get_frame_flags(_denoiseSinkCtx, _denoiseOutFrame, 0) >= 0)
        {
            int outBytes = _denoiseOutFrame->nb_samples * 2; // s16 mono
            if (outBytes > 0)
                toStream.Write(new ReadOnlySpan<byte>((void*)_denoiseOutFrame->data[0], outBytes));
            av_frame_unref(_denoiseOutFrame);
        }
    }

    /// <summary>Frees any graph from a previous pass so each ASR pass starts clean (T-08 runs two passes over one
    /// channel); the graph is rebuilt lazily on the next denoised frame.</summary>
    private unsafe void DenoiseResetForPass() => DisposeDenoise();

    /// <summary>End-of-pass flush: drains afftdn's buffered lookahead tail into the current chunk so no audio is lost,
    /// then frees the graph (it EOFs after a flush; the next pass rebuilds).</summary>
    private unsafe void DenoiseFlush(Stream toStream)
    {
        if (_denoiseGraph == null || _denoiseSrcCtx == null)
            return;

        try
        {
            av_buffersrc_add_frame(_denoiseSrcCtx, null); // signal EOF
            DrainDenoise(toStream);
        }
        catch (Exception e)
        {
            if (CanWarn) Log.Warn($"ASR denoise flush failed: {e.Message}");
        }
        finally
        {
            DisposeDenoise();
        }
    }

    private unsafe void SetupDenoiseGraph()
    {
        try
        {
            AVFilter* abuffer     = avfilter_get_by_name("abuffer");
            AVFilter* afftdn      = avfilter_get_by_name("afftdn");
            AVFilter* abuffersink = avfilter_get_by_name("abuffersink");
            if (abuffer == null || afftdn == null || abuffersink == null)
                throw new Exception("required FFmpeg filter (abuffer/afftdn/abuffersink) not available");

            _denoiseGraph = avfilter_graph_alloc();
            if (_denoiseGraph == null)
                throw new Exception("avfilter_graph_alloc failed");

            AVFilterContext* srcCtx;
            avfilter_graph_create_filter(&srcCtx, abuffer, "in",
                "channel_layout=mono:sample_fmt=s16:sample_rate=16000:time_base=1/16000", null, _denoiseGraph)
                .ThrowExceptionIfError("abuffer");
            _denoiseSrcCtx = srcCtx;

            AVFilterContext* afftdnCtx;
            avfilter_graph_create_filter(&afftdnCtx, afftdn, "afftdn", AsrDenoise.BuildAfftdnArgs(), null, _denoiseGraph)
                .ThrowExceptionIfError("afftdn");
            avfilter_link(srcCtx, 0, afftdnCtx, 0).ThrowExceptionIfError("link src->afftdn");

            AVFilterContext* sinkCtx;
            if (Engine.FFmpeg.Ver8OrGreater)
            {
                sinkCtx = avfilter_graph_alloc_filter(_denoiseGraph, abuffersink, "out");
                if (sinkCtx == null)
                    throw new Exception("abuffersink alloc failed");
                SetDenoiseSinkOpt(sinkCtx, "sample_formats",  [AVSampleFormat.S16],        AVOptionType.SampleFmt);
                SetDenoiseSinkOpt(sinkCtx, "samplerates",     [16000],                     AVOptionType.Int);
                SetDenoiseSinkOpt(sinkCtx, "channel_layouts", [AV_CHANNEL_LAYOUT_MONO],    AVOptionType.Chlayout);
                avfilter_init_dict(sinkCtx, null).ThrowExceptionIfError("abuffersink init");
            }
            else
            {
                avfilter_graph_create_filter(&sinkCtx, abuffersink, "out", null, null, _denoiseGraph)
                    .ThrowExceptionIfError("abuffersink");
                int sr = 16000;
                AVSampleFormat fmt = AVSampleFormat.S16;
                av_opt_set_bin(sinkCtx, "sample_fmts",  (byte*)&fmt, sizeof(AVSampleFormat), OptSearchFlags.Children);
                av_opt_set_bin(sinkCtx, "sample_rates", (byte*)&sr,  sizeof(int),             OptSearchFlags.Children);
                av_opt_set_int(sinkCtx, "all_channel_counts", 0,                              OptSearchFlags.Children);
                av_opt_set(sinkCtx,     "ch_layouts", "mono",                                 OptSearchFlags.Children);
            }
            _denoiseSinkCtx = sinkCtx;

            avfilter_link(afftdnCtx, 0, sinkCtx, 0).ThrowExceptionIfError("link afftdn->sink");
            avfilter_graph_config(_denoiseGraph, null).ThrowExceptionIfError("graph config");

            _denoiseInFrame  = av_frame_alloc();
            _denoiseOutFrame = av_frame_alloc();
            if (_denoiseInFrame == null || _denoiseOutFrame == null)
                throw new Exception("av_frame_alloc failed");

            if (CanInfo) Log.Info("ASR denoise: afftdn graph ready");
        }
        catch (Exception e)
        {
            if (CanWarn) Log.Warn($"ASR denoise: afftdn unavailable, using high-pass only ({e.Message})");
            _denoiseAfftdnFailed = true;
            DisposeDenoise();
        }
    }

    private unsafe int SetDenoiseSinkOpt<T>(AVFilterContext* ctx, string name, T[] value, AVOptionType type) where T : unmanaged
    {
        fixed (T* ptr = value)
            return av_opt_set_array(ctx, name, OptSearchFlags.Children, 0, (uint)value.Length, type, ptr);
    }

    private unsafe void DisposeDenoise()
    {
        if (_denoiseGraph != null)
        {
            fixed (AVFilterGraph** ptr = &_denoiseGraph)
                avfilter_graph_free(ptr);
            _denoiseGraph = null;
        }

        // src/sink contexts are owned by the graph and freed with it.
        _denoiseSrcCtx  = null;
        _denoiseSinkCtx = null;

        if (_denoiseInFrame != null)
        {
            fixed (AVFrame** ptr = &_denoiseInFrame)
                av_frame_free(ptr);
            _denoiseInFrame = null;
        }

        if (_denoiseOutFrame != null)
        {
            fixed (AVFrame** ptr = &_denoiseOutFrame)
                av_frame_free(ptr);
            _denoiseOutFrame = null;
        }
    }

    private bool _isDisposed;

    public unsafe void Dispose()
    {
        if (_isDisposed)
            return;

        // av_frame_alloc
        if (_frame != null)
        {
            fixed (AVFrame** ptr = &_frame)
            {
                av_frame_free(ptr);
            }
        }

        // av_packet_alloc
        if (_packet != null)
        {
            fixed (AVPacket** ptr = &_packet)
            {
                av_packet_free(ptr);
            }
        }

        // swr_init
        if (_swrContext != null)
        {
            fixed (SwrContext** ptr = &_swrContext)
            {
                swr_free(ptr);
            }
        }

        DisposeDenoise();

        _decoder?.Dispose();
        if (_demuxer != null)
        {
            _demuxer.Interrupter.ForceInterrupt = 0;
            _demuxer.Dispose();
        }

        _isDisposed = true;
    }
}

public interface IASRService : IAsyncDisposable
{
    public IAsyncEnumerable<(string text, TimeSpan start, TimeSpan end, string language)> Do(MemoryStream waveStream, CancellationToken token);
}

// https://github.com/sandrohanea/whisper.net
// https://github.com/ggerganov/whisper.cpp
public class WhisperCppASRService : IASRService
{
    public static readonly Lock RuntimeSelectionLock = new();

    private readonly Config _config;

    private readonly LogHandler Log;
    private readonly IDisposable _logger;
    private readonly WhisperFactory _factory;
    private readonly WhisperProcessor _processor;

    private readonly bool _isLanguageDetect;
    private string? _detectedLanguage;

    public WhisperCppASRService(Config config)
    {
        _config = config;
        Log = new LogHandler(("[#1]").PadRight(8, ' ') + " [WhisperCpp    ] ");

        lock (RuntimeSelectionLock)
        {
            if (_config.Subtitles.WhisperCppConfig.RuntimeLibraries.Count >= 1)
            {
                RuntimeOptions.RuntimeLibraryOrder = [.. _config.Subtitles.WhisperCppConfig.RuntimeLibraries];
            }
            else
            {
                RuntimeOptions.RuntimeLibraryOrder = [RuntimeLibrary.Cpu, RuntimeLibrary.CpuNoAvx]; // fallback to default
            }

            _logger = CanDebug
                ? LogProvider.AddLogger((level, s) => Log.Debug($"[Whisper.net] [{level.ToString()}] {s}"))
                : Disposable.Empty;

            if (CanDebug) Log.Debug($"Selecting whisper runtime libraries from ({string.Join(",", RuntimeOptions.RuntimeLibraryOrder)})");

            _factory = WhisperFactory.FromPath(_config.Subtitles.WhisperCppConfig.Model!.ModelFilePath, _config.Subtitles.WhisperCppConfig.GetFactoryOptions());

            if (CanDebug) Log.Debug($"Selected whisper runtime library '{RuntimeOptions.LoadedLibrary}'");

            WhisperProcessorBuilder whisperBuilder = _factory.CreateBuilder();
            _processor = _config.Subtitles.WhisperCppConfig.ConfigureBuilder(_config.Subtitles.WhisperConfig, whisperBuilder).Build();
        }

        if (_config.Subtitles.WhisperCppConfig.IsEnglishModel)
        {
            _isLanguageDetect = false;
            _detectedLanguage = "en";
        }
        else
        {
            _isLanguageDetect = _config.Subtitles.WhisperConfig.LanguageDetection;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _processor.DisposeAsync();
        _factory.Dispose();
        _logger.Dispose();
    }

    public async IAsyncEnumerable<(string text, TimeSpan start, TimeSpan end, string language)> Do(MemoryStream waveStream, [EnumeratorCancellation] CancellationToken token)
    {
        // If language detection is on, pin the already-detected language onto this chunk (F-17 anti-drift), so a
        // later uncertain segment cannot drift to a foreign language. With per-segment detection on (T-10), the
        // language is NOT pinned: each segment auto-detects its own, transcribing mixed-language audio correctly.
        if (AsrLanguagePolicy.ShouldPinLanguage(_config.Subtitles.ASRPerSegmentLanguage, _isLanguageDetect, _detectedLanguage))
        {
            _processor.ChangeLanguage(_detectedLanguage);
        }

        await foreach (var result in _processor.ProcessAsync(waveStream, token).ConfigureAwait(false))
        {
            token.ThrowIfCancellationRequested();

            string text = result.Text.Trim(); // remove leading whitespace

            // Skip empty/blank segments (silence or hallucinated blanks) so they are not emitted as
            // empty subtitles.
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            // Remember the detected language only from a segment that actually produced text, so a silent
            // or music-only first chunk cannot lock the whole file to a wrongly-detected language (F-17).
            // Per-segment detection (T-10) never pins, so this capture is skipped when that toggle is on.
            if (!_config.Subtitles.ASRPerSegmentLanguage && _detectedLanguage is null && !string.IsNullOrEmpty(result.Language))
            {
                _detectedLanguage = result.Language;
            }

            yield return (text, result.Start, result.End, result.Language);
        }
    }
}

// https://github.com/Purfview/whisper-standalone-win
// Purfview's Stand-alone Faster-Whisper-XXL & Faster-Whisper
// Do not support official OpenAI Whisper version
public partial class FasterWhisperASRService : IASRService
{
    private readonly Config _config;

    public FasterWhisperASRService(Config config, Func<bool>? preferCpu = null)
    {
        _config = config;
        _preferCpu = preferCpu;

        _cmdBase = BuildCommand(_config.Subtitles.FasterWhisperConfig, _config.Subtitles.WhisperConfig);
        // CPU variant for the per-chunk background fallback (batch only). Built once; selected per chunk in Do.
        _cmdBaseCpu = preferCpu == null
            ? _cmdBase
            : BuildCommand(_config.Subtitles.FasterWhisperConfig, _config.Subtitles.WhisperConfig, forceCpu: true);

        if (_config.Subtitles.FasterWhisperConfig.IsEnglishModel)
        {
            // force English and disable auto-detection
            _isLanguageDetect = false;
            _manualLanguage = "en";
        }
        else
        {
            _isLanguageDetect = _config.Subtitles.WhisperConfig.LanguageDetection;
            _manualLanguage = _config.Subtitles.WhisperConfig.Language;
        }

        if (!_config.Subtitles.FasterWhisperConfig.UseManualModel)
        {
            WhisperConfig.EnsureModelsDirectory();
        }
    }

    private readonly Command _cmdBase;
    private readonly Command _cmdBaseCpu;
    private readonly Func<bool>? _preferCpu;
    private readonly bool _isLanguageDetect;
    private readonly string _manualLanguage;
    private string? _detectedLanguage;

    [GeneratedRegex("^Detected language '(.+)' with probability")]
    private static partial Regex LanguageReg { get; }

    [GeneratedRegex(@"^\[\d{2}:\d{2}\.\d{3} --> \d{2}:\d{2}\.\d{3}\] ")]
    private static partial Regex SubShortReg { get; } // [08:15.050 --> 08:16.450] Text

    [GeneratedRegex(@"^\[\d{2}:\d{2}:\d{2}\.\d{3} --> \d{2}:\d{2}:\d{2}\.\d{3}\] ")]
    private static partial Regex SubLongReg { get; } // [02:08:15.050 --> 02:08:16.450] Text

    [GeneratedRegex("^Operation finished in:")]
    private static partial Regex EndReg { get; }


    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }

    public static Command BuildCommand(FasterWhisperConfig config, WhisperConfig commonConfig, bool forceCpu = false)
    {
        string tempFolder = Path.GetTempPath();
        string enginePath = config.UseManualEngine ? config.ManualEnginePath! : FasterWhisperConfig.DefaultEnginePath;

        ArgumentsBuilder args = new();
        args.Add("--output_dir").Add(tempFolder);
        args.Add("--output_format").Add("srt");
        args.Add("--verbose").Add("True");
        args.Add("--beep_off");
        args.Add("--model").Add(config.Model);
        args.Add("--model_dir")
            .Add(config.UseManualModel ? config.ManualModelDir! : WhisperConfig.ModelsDirectory);

        if (config.IsEnglishModel)
        {
            args.Add("--language").Add("en");
        }
        else
        {
            if (commonConfig.Translate)
                args.Add("--task").Add("translate");

            if (!commonConfig.LanguageDetection)
                args.Add("--language").Add(commonConfig.Language);
        }

        // F-17/F-18: pass the user's initial prompt (--initial_prompt) to bias the language/script and casing at
        // the source. De-duplicated against ExtraArguments so an explicit --initial_prompt there wins; the
        // ArgumentsBuilder quotes the value, so a prompt with spaces is passed as one argument.
        if (!string.IsNullOrWhiteSpace(config.Prompt) &&
            !ContainsFlag(config.ExtraArguments ?? string.Empty, "--initial_prompt"))
        {
            args.Add("--initial_prompt").Add(config.Prompt);
        }

        string arguments = args.Build();

        // Append anti-hallucination decoding defaults (condition_on_previous_text off + more permissive
        // speech/VAD thresholds) BEFORE ExtraArguments so an explicit user value still wins, and de-duplicated
        // so no flag is ever passed twice (a duplicate/unknown flag would fail the whole faster-whisper run).
        if (config.AntiHallucination)
        {
            string anti = AntiHallucinationArgsFor(config.ExtraArguments);
            if (!string.IsNullOrWhiteSpace(anti))
            {
                arguments += $" {anti}";
            }
        }

        if (!string.IsNullOrWhiteSpace(config.ExtraArguments))
        {
            arguments += $" {config.ExtraArguments}";
        }

        if (forceCpu)
        {
            arguments = ForceCpuDevice(arguments);
        }

        Command cmd = Cli.Wrap(enginePath)
            .WithArguments(arguments)
            .WithValidation(CommandResultValidation.None);

        if (config.ProcessPriority != ProcessPriorityClass.Normal)
        {
            cmd = cmd.WithResourcePolicy(builder =>
                builder.SetPriority(config.ProcessPriority));
        }

        return cmd;
    }

    // Anti-hallucination / don't-drop-speech-under-music decoding defaults for faster-whisper-xxl. Deliberately
    // limited to widely-supported, standard flags so a default-on append cannot break the run on a user's build:
    //  - condition_on_previous_text False: the main repetition-loop driver (mirrors whisper.cpp NoContext, flip 1.5).
    //  - no_speech_threshold 0.4 (down from 0.6): stop discarding audible speech that co-occurs with music as silence.
    //  - vad_threshold 0.35 (down from ~0.45): more permissive VAD so music-masked speech is kept.
    internal static readonly (string Flag, string Value)[] AntiHallucinationFlags =
    [
        ("--condition_on_previous_text", "False"),
        ("--no_speech_threshold", "0.4"),
        ("--vad_threshold", "0.35"),
    ];

    /// <summary>
    /// Returns the anti-hallucination flags NOT already present (by flag name) in <paramref name="extraArguments"/>,
    /// as a single argument string (empty when the user already set them all). Matching is case-insensitive and
    /// handles both "--flag value" and "--flag=value" forms, so a default flag is never duplicated.
    /// </summary>
    public static string AntiHallucinationArgsFor(string? extraArguments)
    {
        string extra = extraArguments ?? string.Empty;
        List<string> parts = new();
        foreach ((string flag, string value) in AntiHallucinationFlags)
        {
            if (ContainsFlag(extra, flag))
            {
                continue;
            }
            parts.Add($"{flag} {value}");
        }
        return string.Join(' ', parts);
    }

    private static bool ContainsFlag(string arguments, string flag)
    {
        // A flag token is bounded by start/whitespace on the left and whitespace, '=' or end on the right.
        return Regex.IsMatch(arguments, $@"(^|\s){Regex.Escape(flag)}($|[\s=])", RegexOptions.IgnoreCase);
    }

    // Rewrites the faster-whisper args to run on CPU for the background fallback, keeping the rest of the
    // user's flags. Swaps the device to cpu and fixes GPU-only compute types (float16 / int8_float16 are not
    // valid on CPU in CTranslate2; map them to float32 / int8). Handles both the space and '=' arg forms
    // (--device cuda / --device=cuda). Rewrites only the parts OUTSIDE double-quoted segments, so a quoted
    // value such as --initial_prompt "..." is never touched. The two compute_type rules are independent
    // (each is anchored to --compute_type, so the float16 rule cannot match an int8_float16 token).
    private static string ForceCpuDevice(string arguments)
    {
        string[] segments = arguments.Split('"');
        bool anyDevice = false;

        // Even indices are outside double quotes; odd indices are inside a quoted value (leave untouched).
        for (int i = 0; i < segments.Length; i += 2)
        {
            string seg = segments[i];

            if (Regex.IsMatch(seg, @"--device[\s=]+\S+", RegexOptions.IgnoreCase))
            {
                anyDevice = true;
                seg = Regex.Replace(seg, @"--device[\s=]+\S+", "--device cpu", RegexOptions.IgnoreCase);
            }

            seg = Regex.Replace(seg, @"--compute_type[\s=]+int8_float16", "--compute_type int8", RegexOptions.IgnoreCase);
            seg = Regex.Replace(seg, @"--compute_type[\s=]+float16", "--compute_type float32", RegexOptions.IgnoreCase);

            segments[i] = seg;
        }

        string result = string.Join('"', segments);

        if (!anyDevice)
        {
            result = $"{result} --device cpu";
        }

        return result.Trim();
    }

    private static TimeSpan ParseTime(ReadOnlySpan<char> time, bool isLong)
    {
        if (isLong)
        {
            // 01:28:02.130
            // hh:mm:ss.fff
            int hours = int.Parse(time[..2]);
            int minutes = int.Parse(time[3..5]);
            int seconds = int.Parse(time[6..8]);
            int milliseconds = int.Parse(time[9..12]);
            return new TimeSpan(0, hours, minutes, seconds, milliseconds);
        }
        else
        {
            // 28:02.130
            // mm:ss.fff
            int minutes = int.Parse(time[..2]);
            int seconds = int.Parse(time[3..5]);
            int milliseconds = int.Parse(time[6..9]);
            return new TimeSpan(0, 0, minutes, seconds, milliseconds);
        }
    }

    public async IAsyncEnumerable<(string text, TimeSpan start, TimeSpan end, string language)> Do(MemoryStream waveStream, [EnumeratorCancellation] CancellationToken token)
    {
        string tempFilePath = Path.GetTempFileName();
        // because no output option
        string outputFilePath = Path.ChangeExtension(tempFilePath, "srt");

        // write WAV to tmp folder
        await using (FileStream fileStream = new(tempFilePath, FileMode.Create, FileAccess.Write))
        {
            waveStream.WriteTo(fileStream);
        }

        // HC-19: dispose the per-chunk force-kill CTS and its token registration. Do() runs once PER audio chunk,
        // and `token` lives for the whole ASR run, so a leaked registration + CTS per chunk accumulated into
        // hundreds of live objects on a multi-hour file. 'using' declarations dispose both when this async
        // iterator is disposed (i.e. when the consumer's await-foreach over Do() finishes this chunk).
        using CancellationTokenSource forceCts = new();
        using CancellationTokenRegistration forceReg = token.Register(() =>
        {
            // force kill if not exited when sending interrupt
            forceCts.CancelAfter(5000);
        });

        try
        {
            string? lastLine = null;
            StringBuilder output = new(); // for error output
            Lock outputLock = new();
            bool oneSuccess = false;

            // Per-segment detection (T-10): re-detect the language for every chunk instead of pinning the first
            // chunk's. Clearing the remembered language makes the stderr detection below capture this chunk's own
            // language, and the pin below is skipped so faster-whisper auto-detects per chunk.
            if (AsrLanguagePolicy.ShouldResetPerChunk(_config.Subtitles.ASRPerSegmentLanguage, _isLanguageDetect))
            {
                _detectedLanguage = null;
            }

            ArgumentsBuilder args = new();
            // Pin the already-detected language onto this chunk (F-17 anti-drift) unless per-segment detection is on.
            // ShouldPinLanguage is true only when _detectedLanguage is non-empty, so the null-forgiving cast is safe.
            if (AsrLanguagePolicy.ShouldPinLanguage(_config.Subtitles.ASRPerSegmentLanguage, _isLanguageDetect, _detectedLanguage))
            {
                args.Add("--language").Add(_detectedLanguage!);
            }
            args.Add(tempFilePath);
            string addedArgs = args.Build();

            // Per-chunk device selection: when the app reports the user is active, this chunk runs on CPU so
            // the GPU stays free. The chunk in flight always finishes on its chosen device — switching only
            // affects the next chunk, so nothing already computed is lost.
            Command baseCmd = _preferCpu?.Invoke() == true ? _cmdBaseCpu : _cmdBase;
            Command cmd = baseCmd.WithArguments($"{baseCmd.Arguments} {addedArgs}");

            await foreach (var cmdEvent in cmd.ListenAsync(Encoding.Default, Encoding.Default, forceCts.Token, token))
            {
                token.ThrowIfCancellationRequested();

                if (cmdEvent is StandardErrorCommandEvent stdErr)
                {
                    lock (outputLock)
                    {
                        output.AppendLine(stdErr.Text);
                    }

                    continue;
                }

                if (cmdEvent is not StandardOutputCommandEvent stdOut)
                {
                    continue;
                }

                string line = stdOut.Text;

                // process stdout
                if (!oneSuccess)
                {
                    lock (outputLock)
                    {
                        output.AppendLine(line);
                    }

                }
                if (string.IsNullOrEmpty(line))
                {
                    continue;
                }

                lastLine = line;

                if (_isLanguageDetect && _detectedLanguage == null)
                {
                    var match = LanguageReg.Match(line);
                    if (match.Success)
                    {
                        string languageName = match.Groups[1].Value;
                        _detectedLanguage = WhisperLanguage.LanguageToCode[languageName];
                    }

                    continue;
                }

                bool isLong = false;

                Match subtitleMatch = SubShortReg.Match(line);
                if (!subtitleMatch.Success)
                {
                    subtitleMatch = SubLongReg.Match(line);
                    if (!subtitleMatch.Success)
                    {
                        continue;
                    }

                    isLong = true;
                }

                ReadOnlySpan<char> lineSpan = line.AsSpan();

                Range startRange = 1..10;
                Range endRange = 15..24;
                Range textRange = 26..;

                if (isLong)
                {
                    startRange = 1..13;
                    endRange = 18..30;
                    textRange = 32..;
                }

                TimeSpan start = ParseTime(lineSpan[startRange], isLong);
                TimeSpan end = ParseTime(lineSpan[endRange], isLong);
                // because some languages have leading spaces
                string text = lineSpan[textRange].Trim().ToString();
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                yield return (text, start, end, _isLanguageDetect ? _detectedLanguage! : _manualLanguage);

                if (!oneSuccess)
                {
                    oneSuccess = true;
                }
            }

            // validate if success
            if (lastLine == null || !EndReg.Match(lastLine).Success)
            {
                throw new InvalidOperationException("Failed to execute faster-whisper")
                {
                    Data =
                    {
                        ["whisper_command"] = cmd.CommandToText(),
                        ["whisper_output"] = output.ToString()
                    }
                };
            }
        }
        finally
        {
            // delete tmp wave
            if (File.Exists(tempFilePath))
            {
                File.Delete(tempFilePath);
            }
            // delete output srt
            if (File.Exists(outputFilePath))
            {
                File.Delete(outputFilePath);
            }
        }
    }
}

public class SubtitleASRData
{
    public required string Text { get; init; }
    public required TimeSpan StartTime { get; init; }
    public required TimeSpan EndTime { get; init; }

#if DEBUG
    public required int ChunkNo { get; init; }
    public required TimeSpan StartTimeChunk { get; init; }
    public required TimeSpan EndTimeChunk { get; init; }
#endif

    public TimeSpan Duration => EndTime - StartTime;

    // ISO6391
    // ref: https://github.com/openai/whisper/blob/main/whisper/tokenizer.py#L10
    public required string Language { get; init; }
}
