using System.Linq;

using FlyleafLib.MediaFramework.MediaDemuxer;
using FlyleafLib.MediaFramework.MediaPlaylist;
using FlyleafLib.MediaFramework.MediaStream;

using static FlyleafLib.MediaFramework.MediaContext.DecoderContext;

namespace FlyleafLib.MediaPlayer;

unsafe partial class Player
{
    bool preservingSubtitleOwners;
    readonly Dictionary<ExternalVideoStream, (string Url, int Index)> selectedVideoSources = new();
    long selectedVideoGeneration = -1;
    PlaylistItem selectedVideoItem;

    // These descriptors retain only managed input metadata and the ORIGINAL input's stream index.
    // No AVStream/AVFormatContext belonging to the replaced video demuxer survives the switch.
    internal sealed class RetainedSubtitlesStream : ExternalSubtitlesStream
    {
        internal int SourceStreamIndex { get; init; }
    }

    sealed class RetainedAudioStream : ExternalAudioStream
    {
        internal int SourceStreamIndex { get; init; }
    }

    sealed class RetainedVideoStream : ExternalVideoStream
    {
        internal int SourceStreamIndex { get; init; }
    }

    static int RetainedStreamIndex(ExternalStream stream, int requestedIndex) => stream switch
    {
        RetainedVideoStream video => video.SourceStreamIndex,
        RetainedAudioStream audio => audio.SourceStreamIndex,
        RetainedSubtitlesStream subtitles => subtitles.SourceStreamIndex,
        _ => requestedIndex
    };

    /// <summary>Runs a media-scoped mutation only while its expected native source is current.</summary>
    public bool TryMutateMedia(long expectedGeneration, PlaylistItem expectedItem, Func<bool> isCurrent,
        Action mutation, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (lockActions)
        {
            bool Current() => !IsDisposed && !IsMediaResetting && expectedItem != null
                && MediaGeneration == expectedGeneration && ReferenceEquals(Playlist.Selected, expectedItem);
            // Initialize/Stop/Dispose use this same gate. Keep validation and native use indivisible,
            // including a second check in case the caller's predicate reenters a media operation.
            if (!Current() || !isCurrent() || !Current())
                return false;
            cancellationToken.ThrowIfCancellationRequested();
            mutation();
            return true;
        }
    }

    /// <summary>
    /// Switches this item's external video variant without resetting either subtitle owner.
    /// Call synchronously from the host's serialized media-mutation worker.
    /// </summary>
    public ExternalStreamOpenedArgs OpenExternalVideoPreservingState(ExternalVideoStream stream, CancellationToken cancellationToken = default)
    {
        OpenExternalVideoStreamCompletedArgs result = new(stream, Playlist.Selected?.ExternalVideoStream);
        try
        {
            lock (lockActions)
            {
                ThrowIfVideoSwitchCanceled(cancellationToken);
                var item = Playlist.Selected;
                if (IsDisposed || IsMediaResetting || item == null || !CanPlay || stream == null)
                {
                    result.Error = "Video switching requires an opened media item";
                    return result;
                }
                if (!item.ExternalVideoStreams.Contains(stream))
                {
                    result.Error = "The video stream no longer belongs to the opened media item";
                    return result;
                }
                DemuxerInput source = item.ExternalVideoStream ?? (DemuxerInput)item;
                if (stream.IOStream != null || source.IOStream != null)
                {
                    result.Error = "Preserving a video switch from an IOStream input is not supported";
                    return result;
                }
                if (string.IsNullOrWhiteSpace(stream.Url))
                {
                    result.Error = "The video stream has no reopenable URL";
                    return result;
                }
                if (ReferenceEquals(stream, item.ExternalVideoStream) && !VideoDecoder.Disposed)
                    return result;

                long generation = MediaGeneration;
                if (selectedVideoGeneration != generation || !ReferenceEquals(selectedVideoItem, item))
                {
                    selectedVideoSources.Clear();
                    selectedVideoGeneration = generation;
                    selectedVideoItem = item;
                }
                ExternalVideoStream previousVideo = item.ExternalVideoStream;
                int previousVideoIndex = VideoDecoder.VideoStream?.StreamIndex ?? -1;
                if (previousVideoIndex >= 0)
                {
                    if (previousVideo != null)
                        selectedVideoSources[previousVideo] = (VideoDemuxer.Url, previousVideoIndex);
                    else
                    {
                        var original = VideoDecoder.VideoStream;
                        previousVideo = CopyRetainedInput(new RetainedVideoStream
                        {
                            SourceStreamIndex = previousVideoIndex,
                            Width = (int)original.Width,
                            Height = (int)original.Height,
                            FPS = original.FPS,
                            Codec = original.Codec,
                            BitRate = original.BitRate,
                            Protocol = File.Exists(VideoDemuxer.Url) ? "file" : "input",
                            HasAudio = Audio.IsOpened
                        }, source);
                    }
                }

                // Snapshot everything that depends on the old native input before replacing it.
                // Dedicated external sources already have independent native owners and are kept intact.
                RetainedAudioStream audio = null;
                if (AudioDecoder.OnVideoDemuxer && AudioDecoder.AudioStream is { } audioStream)
                {
                    audio = CopyRetainedInput(new RetainedAudioStream
                    {
                        SourceStreamIndex = audioStream.StreamIndex,
                        Language = audioStream.Language,
                        Codec = audioStream.Codec,
                        BitRate = audioStream.BitRate,
                        SampleRate = audioStream.SampleRate,
                        ChannelLayout = audioStream.ChannelLayoutStr
                    }, source);
                    lock (item.lockExternalStreams)
                        audio = item.ExternalAudioStreams.OfType<RetainedAudioStream>().FirstOrDefault(existing =>
                            existing.SourceStreamIndex == audio.SourceStreamIndex && SameRetainedInput(existing, audio)) ?? audio;
                }
                RetainedSubtitlesStream[] subtitles = new RetainedSubtitlesStream[subNum];
                for (int i = 0; i < subNum; i++)
                {
                    if (SubtitlesManager[i].IsLoading)
                    {
                        result.Error = "Wait for subtitle loading to finish before switching video";
                        return result;
                    }
                    if (SubtitlesDecoders[i].OnVideoDemuxer && SubtitlesDecoders[i].SubtitlesStream is { } subtitleStream)
                    {
                        subtitles[i] = CopyRetainedInput(new RetainedSubtitlesStream
                        {
                            SourceStreamIndex = subtitleStream.StreamIndex,
                            Language = subtitleStream.Language,
                            Title = subtitleStream.Title ?? $"Track {subtitleStream.StreamIndex}",
                            Codec = subtitleStream.Codec,
                            IsBitmap = subtitleStream.IsBitmap,
                            Downloaded = true
                        }, source);
                        var captured = subtitles[i];
                        lock (item.lockExternalStreams)
                            subtitles[i] = item.ExternalSubtitlesStreamsAll.OfType<RetainedSubtitlesStream>().FirstOrDefault(existing =>
                                existing.SourceStreamIndex == captured.SourceStreamIndex && SameRetainedInput(existing, captured)) ?? captured;
                    }
                }
                int dataIndex = DataDecoder.OnVideoDemuxer ? DataDecoder.Stream?.StreamIndex ?? -1 : -1;
                DemuxerInput dataSource = dataIndex < 0 ? null : CopyRetainedInput(new DemuxerInput(), source);
                if ((audio != null || subtitles.Any(subtitle => subtitle != null) || dataSource != null) && string.IsNullOrEmpty(VideoDemuxer.Url))
                {
                    result.Error = "The original embedded streams have no reopenable URL";
                    return result;
                }

                bool shouldPlay = IsPlaying;
                int selectedSlot = SubtitlesSelectedHelper.CurIndex;
                long audioDelay = Config.Audio.Delay;
                long position = curTime;
                bool videoReplaced = false;
                try
                {
                    Pause();
                    seeks.Clear();
                    while (taskPlayRuns || taskSeekRuns)
                        Thread.Sleep(5);
                    decoder.Pause();
                    position = curTime;
                    preservingSubtitleOwners = true;
                    requiresBuffering = true;

                    // Move attached streams while their original AVStream pointers are still valid.
                    // DecoderBase then disables the old embedded stream before its demuxer is destroyed.
                    if (audio != null)
                    {
                        var opened = decoder.Open(audio, false, audio.SourceStreamIndex);
                        // The ordinary audio-open notification resets delay. Restore it before any seek,
                        // frame priming or PCM decode can observe the newly retained audio source.
                        Config.Audio.SetDelay(audioDelay);
                        ThrowIfVideoSwitchCanceled(cancellationToken);
                        if (!opened.Success)
                            throw new InvalidOperationException($"Cannot retain original audio: {opened.Error}");
                        if (!item.ExternalAudioStreams.Contains(audio))
                            PlaylistItem.AddExternalStream(audio, item, source is ExternalStream ext ? ext.PluginName : null);
                    }
                    for (int i = 0; i < subNum; i++)
                    {
                        if (subtitles[i] == null)
                            continue;
                        SubtitlesSelectedHelper.CurIndex = i;
                        var opened = decoder.Open(subtitles[i], false, subtitles[i].SourceStreamIndex);
                        ThrowIfVideoSwitchCanceled(cancellationToken);
                        if (!opened.Success)
                            throw new InvalidOperationException($"Cannot retain subtitle track {i + 1}: {opened.Error}");
                        SubtitlesDemuxers[i].SelectRetainedSubtitleQueue(i);
                        if (!item.ExternalSubtitlesStreamsAll.Contains(subtitles[i]))
                            PlaylistItem.AddExternalStream(subtitles[i], item, source is ExternalStream ext ? ext.PluginName : null);
                    }
                    if (dataSource != null)
                    {
                        string error = decoder.OpenDemuxerInput(DataDemuxer, dataSource);
                        if (error != null)
                            throw new InvalidOperationException($"Cannot retain original data: {error}");
                        if (!DataDemuxer.AVStreamToStream.TryGetValue(dataIndex, out var dataStream) || dataStream is not DataStream || !DataDecoder.Open(dataStream))
                            throw new InvalidOperationException("Cannot retain the original data stream");
                    }

                    ThrowIfVideoSwitchCanceled(cancellationToken);
                    if (previousVideo is RetainedVideoStream && !item.ExternalVideoStreams.Contains(previousVideo))
                        PlaylistItem.AddExternalStream(previousVideo, item, source is ExternalStream videoSource ? videoSource.PluginName : null);

                    // All other selected native owners are detached before this input can be replaced.
                    videoReplaced = true;
                    VideoDecoder.Dispose(true);
                    vFrame = null;
                    aFrame = null;
                    dFrame = null;
                    int targetIndex = RetainedStreamIndex(stream, -1);
                    if (targetIndex == -1 && selectedVideoSources.TryGetValue(stream, out var selected)
                        && (selected.Url == stream.Url || selected.Url == stream.UrlFallback))
                        targetIndex = selected.Index;
                    result = (OpenExternalVideoStreamCompletedArgs)decoder.Open(stream, false, targetIndex);
                    ThrowIfVideoSwitchCanceled(cancellationToken);
                    if (result.Success)
                        result.Error = RestoreVideoSwitchPosition(position, cancellationToken);
                }
                catch (Exception error)
                {
                    result.Error = error.Message;
                }
                finally
                {
                    bool ownsMedia = !IsDisposed && generation == MediaGeneration && ReferenceEquals(item, Playlist.Selected);
                    bool canceled = cancellationToken.IsCancellationRequested || decoder.Interrupt || !ownsMedia;
                    if (result.Error != null && !canceled)
                    {
                        try
                        {
                            // Retention can fail before every decoder has detached. Reopen the old input
                            // only after the replacement phase began; earlier failures keep it alive.
                            if (videoReplaced && previousVideo != null && previousVideoIndex >= 0)
                            {
                                VideoDecoder.Dispose(true);
                                var restored = decoder.Open(previousVideo, false, previousVideoIndex);
                                ThrowIfVideoSwitchCanceled(cancellationToken);
                                if (!restored.Success)
                                    result.Error += $"\nRestoring the previous video failed: {restored.Error}";
                            }
                            Config.Audio.SetDelay(audioDelay);
                            string recoveryError = RestoreVideoSwitchPosition(position, cancellationToken);
                            if (recoveryError != null)
                                result.Error += $"\n{recoveryError}";
                        }
                        catch (Exception recoveryError)
                        {
                            result.Error += $"\nRestoring playback failed: {recoveryError.Message}";
                        }
                    }
                    preservingSubtitleOwners = false;
                    SubtitlesSelectedHelper.CurIndex = selectedSlot;
                    Config.Audio.SetDelay(audioDelay);
                    NormalizeRetainedSourceSelection(item);
                    Video.Refresh();
                    Audio.Refresh();
                    Data.Refresh();
                    for (int i = 0; i < subNum; i++)
                        Subtitles[i].RefreshRetainedStream();
                    canPlay = Video.IsOpened || Audio.IsOpened;
                    UpdateMainDemuxer();
                    isLive = MainDemuxer.IsLive;
                    duration = MainDemuxer.Duration;
                    status = canPlay ? Status.Paused : Status.Failed;
                    SubtitlesManager.SetCurrentTime(new TimeSpan(curTime));
                    canceled = cancellationToken.IsCancellationRequested || decoder.Interrupt || !ownsMedia;
                    if (!canceled)
                    {
                        VideoDemuxer.Start();
                        AudioDemuxer.Start();
                        DataDemuxer.Start();
                        decoder.PauseOnQueueFull();
                    }
                    UIAdd(() =>
                    {
                        CanPlay = canPlay;
                        IsLive = isLive;
                        Duration = duration;
                        Status = status;
                        SetCurTime();
                    });
                    UIAll();
                    if (shouldPlay && canPlay && !canceled)
                        Play();
                }
            }
        }
        catch (Exception error)
        {
            result.Error = error.Message;
        }
        finally
        {
            OnOpenExternalVideoStreamCompleted(result);
        }
        return result;
    }

    void ThrowIfVideoSwitchCanceled(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (decoder.Interrupt)
            throw new OperationCanceledException("Video switching was interrupted", cancellationToken);
    }

    string RestoreVideoSwitchPosition(long position, CancellationToken cancellationToken)
    {
        ThrowIfVideoSwitchCanceled(cancellationToken);
        int milliseconds = (int)Math.Clamp(position / TimeSpan.TicksPerMillisecond, 0, int.MaxValue);
        if (!VideoDecoder.Disposed && decoder.VideoStream != null)
        {
            if (decoder.Seek(milliseconds, false, false) < 0)
                return "The video opened, but restoring its playback position failed";
            decoder.GetVideoFrame(milliseconds * TimeSpan.TicksPerMillisecond);
            ThrowIfVideoSwitchCanceled(cancellationToken);
            ResetFrameStats();
            ShowOneFrame();
        }
        else if (!AudioDecoder.Disposed)
        {
            if (decoder.SeekAudio(milliseconds) < 0)
                return "Restoring the retained audio position failed";
            _CurTime = curTime = position;
        }
        return null;
    }

    void NormalizeRetainedSourceSelection(PlaylistItem item)
    {
        ExternalVideoStream video = VideoDecoder.Disposed ? null : VideoDecoder.VideoStream?.ExternalStream as ExternalVideoStream;
        if (!ReferenceEquals(item.ExternalVideoStream, video))
        {
            if (item.ExternalVideoStream != null) item.ExternalVideoStream.Enabled = false;
            item.ExternalVideoStream = video;
        }
        ExternalAudioStream audio = AudioDecoder.Disposed ? null : AudioDecoder.AudioStream?.ExternalStream as ExternalAudioStream;
        if (!ReferenceEquals(item.ExternalAudioStream, audio))
        {
            if (item.ExternalAudioStream != null) item.ExternalAudioStream.Enabled = false;
            item.ExternalAudioStream = audio;
        }
        for (int i = 0; i < subNum; i++)
        {
            var selected = SubtitlesDecoders[i].Disposed ? null : SubtitlesDecoders[i].SubtitlesStream?.ExternalStream as ExternalSubtitlesStream;
            var previous = item.ExternalSubtitlesStreams[i];
            item.ExternalSubtitlesStreams[i] = selected;
            if (previous != null && !item.ExternalSubtitlesStreams.Contains(previous))
                previous.Enabled = false;
        }
        foreach (var selected in item.ExternalSubtitlesStreams)
            if (selected != null) selected.Enabled = true;
    }

    bool UsesRetainedNativeSubtitles(int index) =>
        SubtitlesDecoders[index].SubtitlesStream?.ExternalStream is RetainedSubtitlesStream { IsBitmap: true }
        && !Config.Subtitles.EnabledCached
        && SubtitlesSelectedHelper.GetMethod(index) != SelectSubMethod.OCR;

    // Only uncached bitmap tracks need their retained native source. Text, OCR and cached bitmap
    // tracks keep using their existing manager and never reload or restart recognition/translation.
    internal void StartRetainedSubtitleDecoders()
    {
        if (!Config.Subtitles.Enabled)
            return;
        for (int i = 0; i < subNum; i++)
        {
            if (!UsesRetainedNativeSubtitles(i))
                continue;
            SubtitlesDemuxers[i].SelectRetainedSubtitleQueue(i);
            SubtitlesDemuxers[i].Start();
            SubtitlesDecoders[i].Start();
        }
    }

    internal int SeekRetainedSubtitleSources(long milliseconds)
    {
        int result = 0;
        for (int i = 0; i < subNum; i++)
        {
            if (!UsesRetainedNativeSubtitles(i))
                continue;
            var nativeDecoder = SubtitlesDecoders[i];
            var nativeSource = SubtitlesDemuxers[i];
            nativeDecoder.Pause();
            nativeSource.Pause();
            lock (nativeDecoder.lockActions)
            lock (nativeDecoder.lockCodecCtx)
            {
                // Start before the active cue, including long display intervals whose bitmap is not cached.
                SubtitlesManager[i].SetCurrentTime(TimeSpan.FromMilliseconds(milliseconds));
                long cueStart = SubtitlesManager[i].GetCurrent()?.StartTime.Ticks
                    ?? milliseconds * TimeSpan.TicksPerMillisecond - Config.Subtitles[i].Delay;
                long timestamp = Math.Max(nativeSource.StartTime,
                    nativeSource.StartTime + cueStart - 2 * TimeSpan.TicksPerSecond);
                int seekResult = nativeSource.Seek(timestamp, false);
                if (seekResult < 0)
                    result = seekResult;
                nativeDecoder.Flush();
                sFrames[i] = null;
            }
        }
        return result;
    }

    static bool SameRetainedInput(DemuxerInput left, DemuxerInput right)
    {
        if (left.Url != right.Url || left.UrlFallback != right.UrlFallback
            || !ReferenceEquals(left.IOStream, right.IOStream)
            || left.UserAgent != right.UserAgent || left.Referrer != right.Referrer)
            return false;
        if (left.HTTPHeaders == null || right.HTTPHeaders == null)
            return left.HTTPHeaders == null && right.HTTPHeaders == null;
        return left.HTTPHeaders.Count == right.HTTPHeaders.Count
            && left.HTTPHeaders.All(header => right.HTTPHeaders.TryGetValue(header.Key, out string value) && header.Value == value);
    }

    T CopyRetainedInput<T>(T target, DemuxerInput source) where T : DemuxerInput
    {
        // Url is the input that actually opened, including a successful fallback URL.
        target.Url = VideoDemuxer.Url;
        target.UserAgent = source.UserAgent;
        target.Referrer = source.Referrer;
        target.HTTPHeaders = source.HTTPHeaders == null ? null : new Dictionary<string, string>(source.HTTPHeaders);
        return target;
    }
}
