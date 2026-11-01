using System.Collections.ObjectModel;
using System.ComponentModel;
using FlyleafLib.MediaFramework.MediaDemuxer;
using FlyleafLib.MediaFramework.MediaPlaylist;
using FlyleafLib.MediaFramework.MediaStream;

namespace LLPlayer.Avalonia.Services;

public sealed partial class FlyleafPlaybackController
{
    enum PlaybackChoiceKind { Audio, Video, Chapter }

    sealed record PlaybackChoiceMedia(int Epoch, long Generation, PlaylistItem Item, string? VideoSource);

    // Only this adapter can construct these handles. Native objects never leave the private token boundary.
    sealed record PlaybackChoiceToken(
        FlyleafPlaybackController Owner, PlaybackChoiceMedia Media, PlaybackChoiceKind Kind,
        object Source, object Collection, Demuxer? Demuxer, string? SourceUrl, Stream? SourceIO);

    sealed record ActivePlaybackChoice(PlaybackChoiceToken Token, long Revision)
    {
        public long InterruptRevision { get; set; }
    }

    sealed record PlaybackChoiceFailureState(AudioStream? Audio, VideoStream? Video,
        ExternalAudioStream? AudioSource, ExternalVideoStream? VideoSource);

    sealed record PlaybackChoicesNotification(int Epoch, long Generation, PlaylistItem? Item, string? VideoSource);

    readonly List<IDisposable> playbackChoiceWatchers = [];
    readonly object playbackChoiceWatchersGate = new();
    PlaybackChoicesNotification? pendingPlaybackChoices;
    int playbackChoicesNotificationPending;
    long playbackChoiceOperationRevision;
    ActivePlaybackChoice? interruptiblePlaybackChoice;

    public IReadOnlyList<PlaybackStreamTrack> GetAudioTracks() => SnapshotPlaybackChoices(media =>
    {
        List<PlaybackStreamTrack> tracks = [];
        var external = bridge.Snapshot(media.Item.ExternalAudioStreams);
        if (player.Audio.Streams is { } embedded)
            foreach (AudioStream stream in bridge.Snapshot(embedded))
                tracks.Add(new(AudioLabel(stream), ReferenceEquals(player.decoder.AudioStream, stream) && !external.Any(candidate => ReferenceEquals(candidate, stream.ExternalStream)),
                    NativeToken(media, PlaybackChoiceKind.Audio, stream, embedded)));
        foreach (ExternalAudioStream stream in external)
            tracks.Add(new($"External · {LanguageName(stream.Language)} {stream.Codec} [BitRate: {stream.BitRate}] [{stream.Protocol}]",
                player.Audio.IsOpened && ReferenceEquals(player.decoder.AudioStream?.ExternalStream, stream),
                ExternalToken(media, PlaybackChoiceKind.Audio, stream, media.Item.ExternalAudioStreams)));
        return tracks;
    });

    public IReadOnlyList<PlaybackStreamTrack> GetVideoTracks() => SnapshotPlaybackChoices(media =>
    {
        List<PlaybackStreamTrack> tracks = [];
        var external = bridge.Snapshot(media.Item.ExternalVideoStreams);
        if (player.Video.Streams is { } embedded)
            foreach (VideoStream stream in bridge.Snapshot(embedded))
                tracks.Add(new($"#{stream.StreamIndex}{TrackTitle(stream.Title)} {stream.Width}x{stream.Height} @{stream.FPS:0.##} ({stream.Codec}/{stream.PixelFormatStr})",
                    ReferenceEquals(player.decoder.VideoStream, stream) && !external.Any(candidate => ReferenceEquals(candidate, stream.ExternalStream)),
                    NativeToken(media, PlaybackChoiceKind.Video, stream, embedded)));
        foreach (ExternalVideoStream stream in external)
            tracks.Add(new($"External · {stream.DisplayMember}",
                player.Video.IsOpened && ReferenceEquals(player.decoder.VideoStream?.ExternalStream, stream),
                ExternalToken(media, PlaybackChoiceKind.Video, stream, media.Item.ExternalVideoStreams)));
        return tracks;
    });

    public IReadOnlyList<PlaybackChapter> GetChapters() => SnapshotPlaybackChoices(media =>
    {
        List<PlaybackChapter> chapters = [];
        if (player.Chapters is { } source)
            foreach (Demuxer.Chapter chapter in bridge.Snapshot(source))
                chapters.Add(new(string.IsNullOrWhiteSpace(chapter.Title) ? $"Chapter {chapters.Count + 1}" : chapter.Title,
                    TimeSpan.FromTicks(chapter.StartTime),
                    new PlaybackChoiceToken(this, media, PlaybackChoiceKind.Chapter, chapter, source,
                        player.VideoDemuxer, player.VideoDemuxer.Url, null)));
        return chapters;
    });

    static string TrackTitle(string? title) => string.IsNullOrWhiteSpace(title) ? "" : $" · {title}";
    static string AudioLabel(AudioStream stream)
        => $"#{stream.StreamIndex} {LanguageName(stream.Language)}{TrackTitle(stream.Title)} {stream.Codec} {stream.SampleFormatStr} @{stream.SampleRate}Hz [CH: {stream.Channels}] [Bits: {stream.Bits}]";

    PlaybackChoiceToken NativeToken(PlaybackChoiceMedia media, PlaybackChoiceKind kind, StreamBase source, object collection)
        => new(this, media, kind, source, collection, source.Demuxer, source.Demuxer.Url, null);

    PlaybackChoiceToken ExternalToken(PlaybackChoiceMedia media, PlaybackChoiceKind kind, ExternalStream source, object collection)
        => new(this, media, kind, source, collection, null, source.Url, source.IOStream);

    IReadOnlyList<T> SnapshotPlaybackChoices<T>(Func<PlaybackChoiceMedia, List<T>> snapshot)
    {
        PlaybackChoiceMedia? media = CapturePlaybackChoiceMedia();
        if (media == null) return [];
        try
        {
            List<T> choices = snapshot(media);
            return IsCurrentPlaybackMedia(media) ? choices : [];
        }
        // An uncooperative engine collection writer can still invalidate all bridge retries during replacement.
        catch (InvalidOperationException) { return []; }
    }

    PlaybackChoiceMedia? CapturePlaybackChoiceMedia()
    {
        int epoch = Volatile.Read(ref mediaOpenEpoch);
        long generation = player.MediaGeneration;
        if (disposed || player.IsDisposed || player.IsMediaResetting || Volatile.Read(ref cancelableAppEpoch) != 0 || !player.CanPlay || player.Status == FlyleafLib.MediaPlayer.Status.Opening || player.Playlist?.Selected is not { } item)
            return null;
        PlaybackChoiceMedia media = new(epoch, generation, item, player.VideoDemuxer.Url);
        return IsCurrentPlaybackMedia(media) ? media : null;
    }

    bool IsCurrentPlaybackMedia(PlaybackChoiceMedia media)
        => !disposed && !player.IsDisposed && !player.IsMediaResetting && Volatile.Read(ref cancelableAppEpoch) == 0 && player.CanPlay && player.Status != FlyleafLib.MediaPlayer.Status.Opening
            && media.Epoch == Volatile.Read(ref mediaOpenEpoch) && media.Generation == player.MediaGeneration
            && ReferenceEquals(media.Item, player.Playlist?.Selected) && media.VideoSource == player.VideoDemuxer.Url;

    bool IsCurrentPlaybackChoice(PlaybackChoiceToken token)
    {
        if (!ReferenceEquals(token.Owner, this) || !IsCurrentPlaybackMedia(token.Media)) return false;
        bool member = token.Source switch
        {
            AudioStream stream when token.Kind == PlaybackChoiceKind.Audio =>
                ReferenceEquals(token.Demuxer, stream.Demuxer) && token.SourceUrl == stream.Demuxer.Url
                && ReferenceEquals(token.Collection, player.Audio.Streams)
                && bridge.Snapshot(player.Audio.Streams).Any(candidate => ReferenceEquals(candidate, stream)),
            VideoStream stream when token.Kind == PlaybackChoiceKind.Video =>
                ReferenceEquals(token.Demuxer, stream.Demuxer) && token.SourceUrl == stream.Demuxer.Url
                && ReferenceEquals(token.Collection, player.Video.Streams)
                && bridge.Snapshot(player.Video.Streams).Any(candidate => ReferenceEquals(candidate, stream)),
            ExternalAudioStream stream when token.Kind == PlaybackChoiceKind.Audio =>
                ReferenceEquals(token.Collection, token.Media.Item.ExternalAudioStreams)
                && ExternalSourceMatches(token, stream)
                && bridge.Snapshot(token.Media.Item.ExternalAudioStreams).Any(candidate => ReferenceEquals(candidate, stream)),
            ExternalVideoStream stream when token.Kind == PlaybackChoiceKind.Video =>
                ReferenceEquals(token.Collection, token.Media.Item.ExternalVideoStreams)
                && ExternalSourceMatches(token, stream)
                && bridge.Snapshot(token.Media.Item.ExternalVideoStreams).Any(candidate => ReferenceEquals(candidate, stream)),
            Demuxer.Chapter chapter when token.Kind == PlaybackChoiceKind.Chapter =>
                ReferenceEquals(token.Demuxer, player.VideoDemuxer) && token.SourceUrl == player.VideoDemuxer.Url
                && ReferenceEquals(token.Collection, player.Chapters)
                && bridge.Snapshot(player.Chapters).Any(candidate => ReferenceEquals(candidate, chapter)),
            _ => false
        };
        return member && IsCurrentPlaybackMedia(token.Media);
    }

    static bool ExternalSourceMatches(PlaybackChoiceToken token, ExternalStream source)
        => ReferenceEquals(source.PlaylistItem, token.Media.Item) && token.SourceUrl == source.Url
            && ReferenceEquals(token.SourceIO, source.IOStream);

    public Task SelectAudioTrackAsync(PlaybackStreamTrack track)
        => MutatePlaybackChoiceAsync(track.Token, PlaybackChoiceKind.Audio, (source, _) => source switch
        {
            AudioStream stream => player.Open(stream, defaultAudio: false)?.Error ?? (ReferenceEquals(player.decoder.AudioStream, stream) ? null : "Audio track could not be opened."),
            ExternalAudioStream stream => player.Open(stream)?.Error ?? (ReferenceEquals(player.decoder.AudioStream?.ExternalStream, stream) ? null : "Audio track could not be opened."),
            _ => null
        });

    public Task SelectVideoTrackAsync(PlaybackStreamTrack track)
        => MutatePlaybackChoiceAsync(track.Token, PlaybackChoiceKind.Video, (source, cancellation) => source switch
        {
            VideoStream stream => player.Open(stream, defaultAudio: false)?.Error ?? (ReferenceEquals(player.decoder.VideoStream, stream) ? null : "Video track could not be opened."),
            ExternalVideoStream stream => player.OpenExternalVideoPreservingState(stream, cancellation)?.Error ?? (ReferenceEquals(player.decoder.VideoStream?.ExternalStream, stream) ? null : "Video track could not be opened."),
            _ => null
        });

    public Task SeekToChapterAsync(PlaybackChapter chapter)
        => MutatePlaybackChoiceAsync(chapter.Token, PlaybackChoiceKind.Chapter, (source, _) =>
        {
            player.SeekToChapter((Demuxer.Chapter)source);
            return null;
        });

    async Task MutatePlaybackChoiceAsync(object handle, PlaybackChoiceKind kind, Func<object, CancellationToken, string?> mutation)
    {
        if (handle is not PlaybackChoiceToken token || token.Kind != kind || !ReferenceEquals(token.Owner, this) || disposed)
            return;
        long operationRevision = 0;
        PlaybackChoiceFailureState? failureState = null;
        try
        {
            await MutateWithTranslationAsync(cancellation =>
            {
                CancellationTokenRegistration interruption = default;
                try
                {
                    player.TryMutateMedia(token.Media.Generation, token.Media.Item,
                        () => IsCurrentPlaybackChoice(token), () =>
                        {
                            ActivePlaybackChoice? active = null;
                            try
                            {
                                if (kind != PlaybackChoiceKind.Chapter)
                                {
                                    operationRevision = Interlocked.Increment(ref playbackChoiceOperationRevision);
                                    active = new(token, operationRevision);
                                    lock (mediaOpenInterruption) interruptiblePlaybackChoice = active;
                                    ActivePlaybackChoice owned = active;
                                    interruption = cancellation.Register(() =>
                                    {
                                        lock (mediaOpenInterruption)
                                        {
                                            // A replacement can advance the app epoch before this old operation
                                            // drains. The unique active lease still owns the native source until
                                            // the engine's atomic mutation gate releases it.
                                            if (ReferenceEquals(interruptiblePlaybackChoice, owned) && !player.IsDisposed
                                                && owned.Token.Media.Generation == player.MediaGeneration
                                                && ReferenceEquals(owned.Token.Media.Item, player.Playlist?.Selected))
                                                owned.InterruptRevision = player.decoder.RequestOwnedInterrupt();
                                        }
                                    });
                                }
                                cancellation.ThrowIfCancellationRequested();
                                string? error = mutation(token.Source, cancellation);
                                cancellation.ThrowIfCancellationRequested();
                                if (error != null) throw new InvalidOperationException(error);
                            }
                            catch
                            {
                                // Native shortcuts can change tracks outside this adapter queue without a
                                // new media generation. Keep the actual failed input/decoder state too.
                                failureState = new(player.decoder.AudioStream, player.decoder.VideoStream,
                                    player.Playlist?.Selected?.ExternalAudioStream, player.Playlist?.Selected?.ExternalVideoStream);
                                throw;
                            }
                            finally
                            {
                                lock (mediaOpenInterruption)
                                {
                                    if (ReferenceEquals(interruptiblePlaybackChoice, active))
                                    {
                                        // Retire only our own write while the native source is still protected.
                                        // A later native true write owns a newer revision and stays set.
                                        if (active != null) player.decoder.ReleaseOwnedInterrupt(active.InterruptRevision);
                                        interruptiblePlaybackChoice = null;
                                    }
                                }
                            }
                        }, cancellation);
                }
                // Drain a racing interrupt callback before the queue admits another operation. Its ownership
                // was cleared inside the engine gate, so it cannot interrupt a native replacement afterward.
                finally { interruption.Dispose(); }
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            Post(() =>
            {
                if (!disposed && !player.IsDisposed && (operationRevision == 0 || operationRevision == Volatile.Read(ref playbackChoiceOperationRevision))
                    && token.Media.Epoch == Volatile.Read(ref mediaOpenEpoch)
                    && token.Media.Generation == player.MediaGeneration && ReferenceEquals(token.Media.Item, player.Playlist?.Selected)
                    && (failureState == null || (ReferenceEquals(failureState.Audio, player.decoder.AudioStream)
                        && ReferenceEquals(failureState.Video, player.decoder.VideoStream)
                        && ReferenceEquals(failureState.AudioSource, player.Playlist?.Selected?.ExternalAudioStream)
                        && ReferenceEquals(failureState.VideoSource, player.Playlist?.Selected?.ExternalVideoStream))))
                    ErrorOccurred?.Invoke(error.Message);
            });
        }
        finally { QueuePlaybackChoices(); }
    }

    void InitializePlaybackChoices()
    {
        player.OpenAudioStreamCompleted += OnPlaybackChoicesCompleted;
        player.OpenVideoStreamCompleted += OnPlaybackChoicesCompleted;
        player.OpenExternalAudioStreamCompleted += OnPlaybackChoicesCompleted;
        player.OpenExternalVideoStreamCompleted += OnPlaybackChoicesCompleted;
        player.OpenPlaylistItemCompleted += OnPlaybackChoicesCompleted;
        player.OpenSessionCompleted += OnPlaybackChoicesCompleted;
        RefreshPlaybackChoiceWatchers();
    }

    void DisposePlaybackChoices()
    {
        player.OpenAudioStreamCompleted -= OnPlaybackChoicesCompleted;
        player.OpenVideoStreamCompleted -= OnPlaybackChoicesCompleted;
        player.OpenExternalAudioStreamCompleted -= OnPlaybackChoicesCompleted;
        player.OpenExternalVideoStreamCompleted -= OnPlaybackChoicesCompleted;
        player.OpenPlaylistItemCompleted -= OnPlaybackChoicesCompleted;
        player.OpenSessionCompleted -= OnPlaybackChoicesCompleted;
        lock (playbackChoiceWatchersGate)
        {
            foreach (IDisposable watcher in playbackChoiceWatchers) watcher.Dispose();
            playbackChoiceWatchers.Clear();
        }
    }

    void OnPlaybackChoicesCompleted(object? sender, object? args)
    {
        QueuePlaybackChoices();
        if (args is FlyleafLib.MediaFramework.MediaContext.DecoderContext.OpenExternalVideoStreamCompletedArgs)
        {
            // A preserving switch (including rollback) can rebind native subtitle sources while keeping
            // the cached cue objects/text. Publish fresh command identities even when translation is off.
            QueueCueNotification(0);
            QueueCueNotification(1);
        }
    }

    void RefreshPlaybackChoiceWatchers()
    {
        lock (playbackChoiceWatchersGate)
        {
            foreach (IDisposable watcher in playbackChoiceWatchers) watcher.Dispose();
            playbackChoiceWatchers.Clear();
            if (disposed || player.IsDisposed) return;
            int epoch = Volatile.Read(ref mediaOpenEpoch);
            long generation = player.MediaGeneration;
            PlaylistItem? item = player.Playlist?.Selected;
            string? videoSource = player.VideoDemuxer.Url;
            void Changed()
            {
                if (!disposed && epoch == Volatile.Read(ref mediaOpenEpoch) && generation == player.MediaGeneration
                    && ReferenceEquals(item, player.Playlist?.Selected) && videoSource == player.VideoDemuxer.Url)
                    QueuePlaybackChoices();
            }
            void Watch<T>(ObservableCollection<T>? collection)
            {
                if (collection != null) playbackChoiceWatchers.Add(bridge.Watch(collection, Changed));
            }
            Watch(player.Audio.Streams);
            Watch(player.Video.Streams);
            Watch(player.Chapters);
            Watch(player.Playlist?.Selected?.ExternalAudioStreams);
            Watch(player.Playlist?.Selected?.ExternalVideoStreams);
        }
    }

    void QueuePlaybackChoices()
    {
        if (disposed || player.IsDisposed) return;
        // Retain the newest lifecycle identity while only one dispatcher callback is pending.
        PlaybackChoicesNotification media = new(Volatile.Read(ref mediaOpenEpoch), player.MediaGeneration, player.Playlist?.Selected, player.VideoDemuxer.Url);
        Interlocked.Exchange(ref pendingPlaybackChoices, media);
        if (Interlocked.Exchange(ref playbackChoicesNotificationPending, 1) != 0) return;
        void Notify()
        {
            Interlocked.Exchange(ref playbackChoicesNotificationPending, 0);
            PlaybackChoicesNotification? latest = Interlocked.Exchange(ref pendingPlaybackChoices, null);
            if (latest == null || disposed || player.IsDisposed || latest.Epoch != Volatile.Read(ref mediaOpenEpoch)
                || latest.Generation != player.MediaGeneration || !ReferenceEquals(latest.Item, player.Playlist?.Selected)
                || latest.VideoSource != player.VideoDemuxer.Url) return;
            RefreshPlaybackChoiceWatchers();
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("PlaybackChoices"));
        }
        if (ui == null) Notify(); else ui.Post(Notify);
    }
}
