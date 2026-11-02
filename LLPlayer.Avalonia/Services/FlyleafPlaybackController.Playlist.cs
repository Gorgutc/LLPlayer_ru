using System.ComponentModel;
using FlyleafLib.MediaFramework.MediaPlaylist;
using FlyleafLib.MediaPlayer;

namespace LLPlayer.Avalonia.Services;

public sealed partial class FlyleafPlaybackController
{
    sealed record PlaylistChoiceToken(FlyleafPlaybackController Owner, int Epoch, long Generation,
        Playlist Playlist, PlaylistItem Selected, PlaylistItem Target, string? TargetUrl, string? DirectUrl);
    sealed class ActivePlaylistOperation
    {
        public long InterruptRevision;
    }
    IDisposable? playlistItemsWatcher;
    ActivePlaylistOperation? activePlaylistOperation;
    int playlistNotificationPending;

    public event Action? PlaylistChanged;
    public string PlaylistTitle => string.IsNullOrWhiteSpace(player.Playlist.Title) ? "Playlist" : player.Playlist.Title;
    public int PlaylistExpectedCount => player.Playlist.ExpectingItems;
    public string? CurrentMediaPath
    {
        get
        {
            PlaylistItem? item = player.Playlist.Selected;
            string? page = item?.DirectUrl;
            if (FlyleafLib.Utils.IsSafeProcessUrl(page) && Uri.TryCreate(page, UriKind.Absolute, out var uri) && uri.UserInfo.Length == 0)
                return page;
            if (!string.IsNullOrWhiteSpace(item?.Url) && Path.IsPathFullyQualified(item.Url)) return item.Url;
            return FlyleafLib.Plugins.OnlineVideoRequest.TryGetUrl(Url!, out string original) ? original : Url;
        }
    }

    public IReadOnlyList<PlaybackPlaylistItem> GetPlaylistItems()
    {
        if (disposed || player.IsDisposed || player.IsMediaResetting || CanCancelOpen || player.IsOpening
            || player.Playlist.Selected is not { } selected) return [];
        int epoch = Volatile.Read(ref mediaOpenEpoch);
        long generation = player.MediaGeneration;
        Playlist playlist = player.Playlist;
        try
        {
            var result = bridge.Snapshot(playlist.Items).Select((item, index) => new PlaybackPlaylistItem(index,
                string.IsNullOrWhiteSpace(item.Title) ? $"Item {index + 1}" : item.Title,
                ReferenceEquals(item, selected), new PlaylistChoiceToken(this, epoch, generation, playlist,
                    selected, item, item.Url, item.DirectUrl))).ToArray();
            return !player.IsMediaResetting && !CanCancelOpen && !player.IsOpening && generation == player.MediaGeneration
                && epoch == Volatile.Read(ref mediaOpenEpoch) && ReferenceEquals(selected, player.Playlist.Selected)
                ? result : [];
        }
        catch (InvalidOperationException) { return []; }
    }

    bool IsCurrentPlaylistChoice(PlaylistChoiceToken token, int epoch)
        => !disposed && !player.IsDisposed && !player.IsMediaResetting && !player.IsOpening
            && ReferenceEquals(token.Owner, this) && epoch == Volatile.Read(ref mediaOpenEpoch)
            && token.Generation == player.MediaGeneration && ReferenceEquals(token.Playlist, player.Playlist)
            && ReferenceEquals(token.Selected, player.Playlist.Selected) && token.TargetUrl == token.Target.Url
            && token.DirectUrl == token.Target.DirectUrl
            && bridge.Snapshot(player.Playlist.Items).Any(item => ReferenceEquals(item, token.Target));

    public async Task SelectPlaylistItemAsync(PlaybackPlaylistItem item)
    {
        if (item.Token is not PlaylistChoiceToken token || !ReferenceEquals(token.Owner, this)) return;
        int epoch;
        lock (mediaOpenInterruption)
        {
            if (cancelableAppEpoch != 0 || !IsCurrentPlaylistChoice(token, token.Epoch)) return;
            cancelableAppEpoch = epoch = Interlocked.Increment(ref mediaOpenEpoch);
        }
        Raise(nameof(CanCancelOpen));
        QueuePlaylistChanged();
        QueuePlaybackChoices();
        Post(() => { if (!disposed && epoch == Volatile.Read(ref mediaOpenEpoch)) ExternalMediaOpening?.Invoke(); });
        try
        {
            await MutateWithTranslationAsync(cancellation =>
            {
                string? error = null;
                bool changed = false;
                CancellationTokenRegistration interruption = default;
                try
                {
                    changed = player.TryMutateMedia(token.Generation, token.Selected,
                        () => IsCurrentPlaylistChoice(token, epoch), () =>
                        {
                            ActivePlaylistOperation active = new();
                            lock (mediaOpenInterruption) activePlaylistOperation = active;
                            try
                            {
                                interruption = cancellation.Register(() =>
                                {
                                    lock (mediaOpenInterruption)
                                        if (ReferenceEquals(activePlaylistOperation, active) && !player.IsDisposed)
                                            active.InterruptRevision = player.decoder.RequestOwnedInterrupt();
                                });
                                cancellation.ThrowIfCancellationRequested();
                                using var scope = FlyleafLib.Plugins.OnlineVideoRequest.ObserveCancellation(cancellation);
                                error = player.OpenPlaylistItemFromStart(token.Target);
                                cancellation.ThrowIfCancellationRequested();
                            }
                            finally
                            {
                                lock (mediaOpenInterruption)
                                    if (ReferenceEquals(activePlaylistOperation, active))
                                    {
                                        player.decoder.ReleaseOwnedInterrupt(active.InterruptRevision);
                                        activePlaylistOperation = null;
                                    }
                            }
                        }, cancellation);
                }
                finally { interruption.Dispose(); }
                if (!changed) return;
                Post(() =>
                {
                    if (disposed || epoch != Volatile.Read(ref mediaOpenEpoch)) return;
                    Raise(nameof(Title)); Raise(nameof(Url)); Raise(nameof(IsOpened));
                    Raise(nameof(CurrentMediaPath));
                    OpenCompleted?.Invoke(player.Playlist.Url, false, error);
                    if (error != null) ErrorOccurred?.Invoke(error);
                });
            }, mediaChange: true).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            Post(() => { if (!disposed && epoch == Volatile.Read(ref mediaOpenEpoch)) ErrorOccurred?.Invoke(error.Message); });
        }
        finally
        {
            lock (mediaOpenInterruption)
                if (cancelableAppEpoch == epoch) cancelableAppEpoch = 0;
            Raise(nameof(CanCancelOpen));
            QueuePlaylistChanged(); QueuePlaybackChoices(); QueueCueNotification(0); QueueCueNotification(1);
        }
    }

    void InitializePlaylistChoices()
    {
        player.Playlist.PropertyChanged += OnPlaylistPropertyChanged;
        player.PropertyChanged += OnPlaylistPlayerPropertyChanged;
        player.OpenPlaylistItemCompleted += OnPlaylistItemCompleted;
        player.OpenSessionCompleted += OnPlaylistItemCompleted;
        playlistItemsWatcher = bridge.Watch(player.Playlist.Items, QueuePlaylistChanged);
    }
    void DisposePlaylistChoices()
    {
        playlistItemsWatcher?.Dispose();
        player.Playlist.PropertyChanged -= OnPlaylistPropertyChanged;
        player.PropertyChanged -= OnPlaylistPlayerPropertyChanged;
        player.OpenPlaylistItemCompleted -= OnPlaylistItemCompleted;
        player.OpenSessionCompleted -= OnPlaylistItemCompleted;
    }
    void OnPlaylistPropertyChanged(object? sender, PropertyChangedEventArgs e) => QueuePlaylistChanged();
    void OnPlaylistPlayerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Player.Status) or nameof(Player.CanPlay)) QueuePlaylistChanged();
    }
    void OnPlaylistItemCompleted(object? sender, object? args) => QueuePlaylistChanged();
    void QueuePlaylistChanged()
    {
        if (disposed || player.IsDisposed || Interlocked.Exchange(ref playlistNotificationPending, 1) != 0) return;
        Post(() =>
        {
            Interlocked.Exchange(ref playlistNotificationPending, 0);
            if (disposed || player.IsDisposed) return;
            Raise(nameof(PlaylistTitle)); Raise(nameof(PlaylistExpectedCount));
            PlaylistChanged?.Invoke();
        });
    }
}
