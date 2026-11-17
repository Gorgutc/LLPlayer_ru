using System.Text.Json;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using LLPlayer.Avalonia.ViewModels;
using LLPlayer.Avalonia.Views;
using LLPlayer.ControlApi;

namespace LLPlayer.Avalonia.Services.Automation;

internal sealed partial class ControlGuiExecutor(FlyleafPlaybackController player, MainWindowViewModel model, MainWindow window,
    Func<ApplicationLaunchKind, Guid, int>? launchApplication = null)
{
    readonly ControlCueProjection cues = new(player);
    readonly ControlChoices<PlaybackStreamTrack> audio = new(track => track.Token);
    readonly ControlChoices<PlaybackStreamTrack> video = new(track => track.Token);
    readonly ControlChoices<SubtitleTrack> subtitles = new(track => track.Source);
    readonly ControlChoices<PlaybackChapter> chapters = new(chapter => chapter.Token);
    readonly ControlChoices<PlaybackPlaylistItem> playlist = new(item => item.Token);
    long mediaRevision = long.MinValue;
    Guid mediaId;

    public async Task<ControlResult> ExecuteAsync(ControlRequest request, CancellationToken token)
    {
        Dispatcher.UIThread.VerifyAccess();
        token.ThrowIfCancellationRequested();
        EnsureMediaIdentity();
        if (request.ExpectedRevision.HasValue && request.ExpectedRevision != player.ControlMediaRevision)
            return ControlResult.Fail("stale_revision", "Read the current media state and use its revision.");

        T Arguments<T>() where T : class => request.Arguments.Deserialize<T>(ControlProtocol.Json)
            ?? throw new JsonException("The command arguments must be an object.");
        ControlGuiAction Applied() => new(true);
        try
        {
            object result;
            switch (request.OperationId)
            {
                case "media.open":
                    var media = Arguments<ControlMediaOpen>();
                    await player.OpenControlMediaAsync(ValidateMediaPath(media.Path), media.OnlineVideo, token);
                    result = Playback();
                    break;
                case "media.cancel_open": result = new ControlGuiAction(player.CancelOpen()); break;
                case "playback.get": result = Playback(); break;
                case "playback.play": RequireMedia(); player.Play(); result = Playback(); break;
                case "playback.pause": RequireMedia(); player.Pause(); result = Playback(); break;
                case "playback.seek":
                    var position = Arguments<ControlPosition>();
                    RequireMedia();
                    if (position.PositionTicks < 0 || (player.Duration > 0 && position.PositionTicks > player.Duration))
                        throw Invalid("The position is outside the current media.");
                    await player.SeekControlPlaybackAsync(TimeSpan.FromTicks(position.PositionTicks), token);
                    result = Playback();
                    break;
                case "playback.set_speed":
                    double speed = Arguments<ControlSpeed>().Speed;
                    if (!double.IsFinite(speed) || speed is < 0.25 or > 4) throw Invalid("Use a speed between 0.25 and 4.");
                    player.Speed = speed;
                    result = Playback();
                    break;
                case "playback.set_volume":
                    int volume = Arguments<ControlVolume>().Volume;
                    if (volume < 0 || volume > player.VolumeMax) throw Invalid("Use a volume inside the configured range.");
                    player.Volume = volume;
                    result = Playback();
                    break;
                case "playback.set_mute": player.Mute = Arguments<ControlMute>().Mute; result = Playback(); break;
                case "streams.list": result = Streams(Arguments<ControlStreamList>().Kind); break;
                case "streams.select": await SelectStreamAsync(Arguments<ControlStreamSelection>(), token); result = Applied(); break;
                case "chapters.list": result = Chapters(); break;
                case "chapters.seek":
                    Guid chapterId = Arguments<ControlChoice>().Id;
                    _ = Chapters();
                    await player.SeekControlPlaybackAsync(chapters.Get(chapterId).Start, token);
                    result = Applied();
                    break;
                case "playlist.list": result = Playlist(); break;
                case "playlist.select":
                    Guid itemId = Arguments<ControlChoice>().Id;
                    _ = Playlist();
                    await player.SelectControlPlaylistItemAsync(playlist.Get(itemId), token);
                    result = Applied();
                    break;
                case "subtitles.load":
                    var load = Arguments<ControlSubtitleLoad>();
                    await player.OpenControlSubtitleAsync(ValidateLocalPath(load.Path), load.Slot, token);
                    result = Applied();
                    break;
                case "subtitles.list": result = new[] { cues.Describe(0), cues.Describe(1) }; break;
                case "subtitles.read": result = cues.Read(Arguments<ControlSubtitlePageRequest>()); break;
                case "subtitles.set_visibility":
                    player.SubtitlesVisible = Arguments<ControlEnabled>().Enabled;
                    result = Applied();
                    break;
                case "subtitles.seek_to_cue":
                    var seek = Arguments<ControlSubtitleCueRequest>();
                    var seekCue = cues.Resolve(seek);
                    await player.SeekControlCueAsync(seekCue.Cue, seek.Slot, seekCue.Delay, token);
                    result = Applied();
                    break;
                case "subtitles.synchronize":
                    var sync = Arguments<ControlSubtitleCueRequest>();
                    var syncCue = cues.Resolve(sync);
                    await player.SynchronizeControlCueAsync(syncCue.Cue, sync.Slot, syncCue.Delay, token);
                    result = Applied();
                    break;
                case "subtitles.set_delay":
                    var delay = Arguments<ControlSubtitleDelay>();
                    await player.SetControlSubtitleDelayAsync(delay.Slot, delay.DelayTicks, token);
                    result = Applied();
                    break;
                case "waveform.get": result = Waveform(); break;
                case "waveform.set_enabled":
                    player.WaveformEnabled = Arguments<ControlEnabled>().Enabled;
                    result = Waveform();
                    break;
                case "ab_loop.get": result = new ControlAbLoopSnapshot(player.HasABLoopA ? player.ABLoopA : null,
                    player.HasABLoopB ? player.ABLoopB : null, player.ABLoopEnabled); break;
                case "ab_loop.clear": player.ClearABLoop(); result = Applied(); break;
                case "translation.slot.set_enabled":
                    var translate = Arguments<ControlSubtitleTranslation>();
                    await player.SetControlTranslationEnabledAsync(translate.Slot, translate.Enabled, token);
                    result = Applied();
                    break;
                case "commands.list":
                    result = model.BuildExecutableCommands().Select(command => new ControlCommandDescription(command.Id,
                        command.ActionName, command.Description, command.Group, command.Keys, command.CanExecute,
                        IsObservedCommand(command.Id))).ToArray();
                    break;
                case "devices.audio.list": result = AudioDevices(); break;
                case "devices.audio.refresh":
                    await player.RefreshAudioDevicesAsync();
                    result = AudioDevices();
                    break;
                case "devices.audio.select":
                    await player.SelectControlAudioDeviceAsync(Arguments<ControlDeviceSelection>().DeviceId, token);
                    result = AudioDevices();
                    break;
                case "backends.get": result = VideoBackends(); break;
                case "capture.devices": result = await CaptureDevicesAsync(Arguments<ControlCaptureDevices>(), token); break;
                case "capture.formats": result = await CaptureFormatsAsync(Arguments<ControlCaptureFormats>(), token); break;
                case "capture.open":
                    await OpenCaptureAsync(Arguments<ControlCaptureOpen>(), token);
                    result = Playback();
                    break;
                case "recording.status": result = player.ControlRecording; break;
                case "recording.start":
                    var recording = Arguments<ControlRecordingRequest>();
                    result = await player.StartControlRecordingAsync(ValidateLocalPath(recording.Path), recording.Overwrite, token);
                    break;
                case "recording.stop": result = await player.StopControlRecordingAsync(token); break;
                case "snapshot.take":
                    var snapshot = Arguments<ControlSnapshotRequest>();
                    result = await player.TakeControlSnapshotAsync(ValidateLocalPath(snapshot.Path), snapshot.Overwrite,
                        snapshot.Width, snapshot.Height, token);
                    break;
                case "app.about":
                    AboutInformation about = AboutInformation.FromAssembly(typeof(App).Assembly);
                    result = new ControlGuiAbout("LLPlayer (Avalonia)", about.Version, about.Runtime);
                    break;
                case "app.diagnostics":
                    result = new ControlGuiDiagnostic("Avalonia", Environment.Version.ToString(), Playback());
                    break;
                case "ui.get": result = WindowState(); break;
                case "ui.set":
                    var setting = Arguments<ControlGuiUiSet>();
                    if (setting.Fullscreen is { } fullscreen) window.IsFullScreen = fullscreen;
                    if (setting.Sidebar is { } visible && model.SidebarVisible != visible) model.ToggleSidebar();
                    result = WindowState();
                    break;
                case "ui.clipboard.set":
                    var clipboard = window.Clipboard ?? throw Unavailable("The desktop clipboard is unavailable.");
                    await clipboard.SetTextAsync(Arguments<ControlClipboardText>().Text).WaitAsync(token);
                    result = Applied();
                    break;
                case "ui.browser.open":
                    string address = Arguments<ControlOpenUri>().Uri;
                    if (!Uri.TryCreate(address, UriKind.Absolute, out Uri? uri) || !WordActions.IsHttpUri(uri)
                        || !string.IsNullOrEmpty(uri.UserInfo))
                        throw Invalid("Use an HTTP or HTTPS URL without embedded credentials.");
                    if (!await window.OpenBrowserAsync(uri).WaitAsync(token)) throw Unavailable("The desktop could not open the URL.");
                    result = Applied();
                    break;
                case "ui.path.reveal":
                    if (!await window.RevealMediaAsync(ValidateLocalPath(Arguments<ControlRevealPath>().Path)).WaitAsync(token))
                        throw Unavailable("The desktop could not reveal the file or directory.");
                    result = Applied();
                    break;
                default:
                    if ((ExecuteLifecycle(request, token) ?? await ExecuteExtendedAsync(request, token) ?? await ExecuteDubbingOperationAsync(request, token)
                        ?? await ExecuteWordOperationAsync(request, token)) is not { } specialized)
                        return ControlResult.Fail("unknown_operation", "This operation has no GUI handler.");
                    result = specialized;
                    break;
            }
            return new(Revision: player.ControlMediaRevision, Value: ControlProtocol.Encode(result));
        }
        catch (ControlException error) { return new(Error: error.Error); }
        catch (JsonException) { return ControlResult.Fail("invalid_arguments", "The command arguments do not match its schema."); }
        catch (ArgumentException error) { return ControlResult.Fail("invalid_arguments", error.Message); }
        catch (OperationCanceledException) { return ControlResult.Fail("cancelled", "The GUI command was cancelled or superseded."); }
        catch (InvalidOperationException error)
        {
            // Native error strings may include signed URLs or provider-specific diagnostic details.
            return ControlResult.Fail("gui_operation_failed", PublicError(error));
        }
        catch (IOException error) { return ControlResult.Fail("file_io_failed", PublicError(error)); }
    }

    internal static string PublicError(Exception error) =>
        error.Message.Contains("://", StringComparison.Ordinal) ? "The player reported an error for the requested source." : error.Message;

    void EnsureMediaIdentity()
    {
        long revision = player.ControlMediaRevision;
        if (mediaRevision == revision) return;
        mediaRevision = revision;
        mediaId = Guid.NewGuid();
        audio.Clear();
        video.Clear();
        subtitles.Clear();
        chapters.Clear();
        playlist.Clear();
    }

    ControlPlaybackSnapshot Playback()
    {
        EnsureMediaIdentity();
        string? title = player.Title;
        if (title != null && Uri.TryCreate(player.Url, UriKind.Absolute, out Uri? uri) && WordActions.IsHttpUri(uri)
            && title == Path.GetFileName(player.Url))
            title = Path.GetFileName(uri.AbsolutePath);
        return new(player.IsOpened ? mediaId : Guid.Empty, mediaRevision, title, player.IsOpened, player.IsPlaying,
            player.IsOpening, player.CurTime, player.Duration, player.Speed, player.Volume, player.VolumeMax, player.Mute);
    }

    ControlStreamSnapshot[] Streams(ControlStreamKind kind)
        => kind switch
        {
            ControlStreamKind.Audio => audio.Snapshot(player.GetAudioTracks()).Select(pair => new ControlStreamSnapshot(pair.Key, pair.Value.Label, pair.Value.IsSelected)).ToArray(),
            ControlStreamKind.Video => video.Snapshot(player.GetVideoTracks()).Select(pair => new ControlStreamSnapshot(pair.Key, pair.Value.Label, pair.Value.IsSelected)).ToArray(),
            ControlStreamKind.Subtitle => subtitles.Snapshot(player.GetSubtitleTracks()).Select(pair => new ControlStreamSnapshot(pair.Key, pair.Value.Label,
                pair.Value.IsPrimary || pair.Value.IsSecondary, pair.Value.IsPrimary, pair.Value.IsSecondary)).ToArray(),
            _ => throw Invalid("The stream kind is unknown.")
        };

    async Task SelectStreamAsync(ControlStreamSelection selection, CancellationToken token)
    {
        _ = Streams(selection.Kind);
        switch (selection.Kind)
        {
            case ControlStreamKind.Audio:
                await player.SelectControlAudioTrackAsync(audio.Get(selection.Id), token);
                break;
            case ControlStreamKind.Video:
                await player.SelectControlVideoTrackAsync(video.Get(selection.Id), token);
                break;
            case ControlStreamKind.Subtitle:
                if (!selection.Slot.HasValue) throw Invalid("Select a subtitle slot.");
                await player.SelectControlSubtitleAsync(subtitles.Get(selection.Id), selection.Slot.Value, token);
                break;
        }
    }

    ControlChapterSnapshot[] Chapters()
        => chapters.Snapshot(player.GetChapters()).Select(pair => new ControlChapterSnapshot(pair.Key, pair.Value.Title, pair.Value.Start.Ticks)).ToArray();

    ControlPlaylistSnapshot[] Playlist()
        => playlist.Snapshot(player.GetPlaylistItems()).Select(pair => new ControlPlaylistSnapshot(pair.Key, pair.Value.Index, pair.Value.Title, pair.Value.IsCurrent)).ToArray();

    ControlWaveformSnapshot Waveform() => new(player.WaveformEnabled, player.WaveformActive, player.WaveformPeaks);
    ControlGuiUiSnapshot WindowState() => new(window.IsFullScreen, model.SidebarVisible, model.AlwaysOnTop);
    void RequireMedia() { if (!player.IsOpened) throw Unavailable("Open media in this window first."); }

    static string ValidateMediaPath(string path)
    {
        if (Uri.TryCreate(path, UriKind.Absolute, out Uri? uri) && WordActions.IsHttpUri(uri)
            && string.IsNullOrEmpty(uri.UserInfo))
            return path;
        return ValidateLocalPath(path);
    }

    static string ValidateLocalPath(string path)
        => !Path.IsPathFullyQualified(path) || path.IndexOf('\0') >= 0
            ? throw Invalid("Use an absolute local path.")
            : Path.GetFullPath(path);

    static ControlException Invalid(string message) => new(new("invalid_arguments", message));
    static ControlException Unavailable(string message) => new(new("unavailable", message));

    sealed class ControlChoices<T>(Func<T, object> key)
    {
        Dictionary<Guid, T> items = [];

        public void Clear() => items.Clear();

        public IReadOnlyDictionary<Guid, T> Snapshot(IEnumerable<T> current)
        {
            Dictionary<object, Guid> ids = [];
            foreach ((Guid id, T item) in items) ids.TryAdd(key(item), id);
            Dictionary<Guid, T> snapshot = [];
            foreach (T item in current)
            {
                Guid id = ids.TryGetValue(key(item), out Guid previous) && !snapshot.ContainsKey(previous)
                    ? previous : Guid.NewGuid();
                snapshot.Add(id, item);
            }
            return items = snapshot;
        }

        public T Get(Guid id) => items.TryGetValue(id, out T? item) ? item
            : throw new ControlException(new("stale_revision", "Read the current choices before selecting one."));
    }
}
