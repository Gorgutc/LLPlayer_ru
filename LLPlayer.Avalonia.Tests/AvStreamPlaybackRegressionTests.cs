using System.Runtime.InteropServices;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FlyleafLib;
using FlyleafLib.MediaFramework.MediaFrame;
using FlyleafLib.MediaFramework.MediaPlaylist;
using FlyleafLib.MediaFramework.MediaRenderer;
using FlyleafLib.MediaFramework.MediaStream;
using FlyleafLib.MediaPlayer;
using FlyleafLib.MediaPlayer.Translation;
using FlyleafLib.MediaPlayer.Translation.Services;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.ViewModels;

namespace LLPlayer.Avalonia.Tests;

[Collection(FlyleafGlobalsCollection.Name)]
public sealed class AvStreamPlaybackRegressionTests
{
    [AvaloniaTheory]
    [InlineData(0, false, false)]
    [InlineData(1, false, false)]
    [InlineData(0, true, false)]
    [InlineData(1, true, false)]
    [InlineData(0, true, true)]
    [InlineData(1, true, true)]
    public async Task Preserving_external_video_switch_refreshes_live_sidebar_cue_identity(int slot, bool embeddedSubtitles, bool failedTarget)
    {
        using AvStreamTestMedia media = await AvStreamTestMedia.CreateAsync();
        using TempDir state = new();
        await using NativeSession session = new(state);
        Player player = session.Player; FlyleafPlaybackController controller = session.Controller;
        Assert.Null(player.Open(media.Media).Error);
        await LoadSubtitles(player, controller, media, embeddedSubtitles);
        await SeekAndWait(player, 2500);
        MainWindowViewModel vm = new(controller, new AppPrefs { SidebarShowSecondary = slot == 1 }.Normalize(), _ => { }, new FakeTranslator(), new FakeShell());
        try
        {
            vm.Sidebar.Reload();
            await controller.Recognition.MutateAsync(() => { });
            Dispatcher.UIThread.RunJobs();
            Assert.False(controller.PrimaryTranslationEnabled);
            Assert.False(controller.SecondaryTranslationEnabled);
            CueItemViewModel previous = Assert.Single(vm.Sidebar.Cues);
            Assert.True(vm.Sidebar.IsCurrentRow(previous));
            SubtitleData[] nativeCues = [player.SubtitlesManager[0].Subs[0], player.SubtitlesManager[1].Subs[0]];
            ExternalVideoStream video = RegisterVideo(player, media);
            if (failedTarget) video.Url = Path.Combine(media.DirectoryPath, "missing-sidebar-video.mkv");
            // Drain the initial loading notifications before the source changes. A translation reset cannot
            // incidentally reload this sidebar: translation is off and neither cue has cached translated text.
            Dispatcher.UIThread.RunJobs();

            await controller.SelectVideoTrackAsync(controller.GetVideoTracks().Single(track => track.Label.Contains("480x270", StringComparison.Ordinal)));
            await controller.Recognition.MutateAsync(() => { });
            Dispatcher.UIThread.RunJobs();

            Assert.False(controller.IsCueCurrent(previous.Cue, slot));
            CueItemViewModel current = Assert.Single(vm.Sidebar.Cues);
            Assert.True(vm.Sidebar.IsCurrentRow(current));
            Assert.NotSame(previous, current);
            Assert.Same(nativeCues[0], Assert.Single(player.SubtitlesManager[0].SnapshotSubs()));
            Assert.Same(nativeCues[1], Assert.Single(player.SubtitlesManager[1].SnapshotSubs()));
            long playhead = player.CurTime;
            await vm.Sidebar.SyncCommand.ExecuteAsync(current);
            Assert.Equal(playhead - current.Start.Ticks, player.Config.Subtitles[slot].Delay);
            Assert.Equal(0, player.Config.Subtitles[1 - slot].Delay);
        }
        finally { vm.Dispose(); await vm.WordPopup.DisposeAsync(); }
    }

    [AvaloniaFact]
    public async Task External_video_switch_preserves_both_loaded_subtitle_caches()
    {
        using AvStreamTestMedia media = await AvStreamTestMedia.CreateAsync();
        using TempDir state = new();
        await using NativeSession session = new(state);
        Player player = session.Player;
        Assert.Null(player.Open(media.Media).Error);
        await LoadSubtitles(player, session.Controller, media, embedded: false);
        SubtitleData[] primary = player.SubtitlesManager[0].SnapshotSubs().ToArray();
        SubtitleData[] secondary = player.SubtitlesManager[1].SnapshotSubs().ToArray();
        Assert.Single(primary); Assert.Single(secondary);
        var video = RegisterVideo(player, media);

        // RED evidence used the old Open(video), which cleared both cached tracks through Initialize.
        var result = player.OpenExternalVideoPreservingState(video);

        Assert.NotNull(result); Assert.Null(result.Error);
        Assert.Equal(primary, player.SubtitlesManager[0].SnapshotSubs().ToArray());
        Assert.Equal(secondary, player.SubtitlesManager[1].SnapshotSubs().ToArray());
    }

    [AvaloniaTheory]
    [InlineData("embedded-audio", false, false)]
    [InlineData("embedded-audio", true, false)]
    [InlineData("embedded-video", false, false)]
    [InlineData("embedded-video", true, false)]
    [InlineData("external-audio", false, false)]
    [InlineData("external-audio", true, false)]
    [InlineData("external-video", false, false)]
    [InlineData("external-video", true, false)]
    [InlineData("external-video", false, true)]
    [InlineData("external-video", true, true)]
    public async Task Real_stream_choices_preserve_position_play_state_and_both_translated_tracks(string kind, bool playing, bool embeddedSubtitles)
    {
        using AvStreamTestMedia media = await AvStreamTestMedia.CreateAsync();
        using TempDir state = new();
        await using NativeSession session = new(state);
        Player player = session.Player; FlyleafPlaybackController controller = session.Controller;
        FrameProbe surface = new(); player.Renderer.Surface = surface; player.Renderer.SetControlSize(640, 360);
        Assert.Null(player.Open(media.Media).Error);
        Assert.Equal(2, player.VideoDemuxer.VideoStreams.Count);
        Assert.Equal(2, player.VideoDemuxer.AudioStreams.Count);
        Assert.Equal(2, player.VideoDemuxer.SubtitlesStreamsAll.Count);
        Assert.Equal(3, player.Chapters.Count);
        // Establish an explicit original audio owner before switching to a video-only alternative.
        await controller.SelectAudioTrackAsync(controller.GetAudioTracks().Single(track => track.Label.Contains("Mono 48000", StringComparison.Ordinal)));
        Assert.Equal(48000, player.decoder.AudioStream.SampleRate);
        Assert.Null(player.Open(player.VideoDemuxer.VideoStreams.Single(stream => stream.Width == 320), defaultAudio: false).Error);
        await LoadSubtitles(player, controller, media, embeddedSubtitles);
        var subtitles = await TranslateBoth(player, controller);
        await SeekAndWait(player, 2500);
        await WaitUntil(() => surface.Is("red"));
        RegisterVideo(player, media); RegisterAudio(player, media);
        if (playing) { player.Play(); await WaitUntil(() => player.IsPlaying); }
        long position = player.CurTime, generation = player.MediaGeneration, frames = surface.Count;
        using PcmProbe pcm = new(player);
        PlaybackStreamTrack choice = kind switch
        {
            "embedded-audio" => controller.GetAudioTracks().Single(track => track.Label.Contains("Stereo 44100", StringComparison.Ordinal)),
            "embedded-video" => controller.GetVideoTracks().Single(track => track.Label.Contains("Blue 640", StringComparison.Ordinal)),
            "external-audio" => controller.GetAudioTracks().Single(track => track.Label.Contains("external-audio-regression", StringComparison.Ordinal)),
            _ => controller.GetVideoTracks().Single(track => track.Label.Contains("480x270", StringComparison.Ordinal))
        };

        if (kind.EndsWith("audio", StringComparison.Ordinal)) await controller.SelectAudioTrackAsync(choice);
        else await controller.SelectVideoTrackAsync(choice);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(playing, player.IsPlaying);
        Assert.Equal(generation, player.MediaGeneration);
        Assert.InRange(player.CurTime, position - TimeSpan.FromMilliseconds(250).Ticks, position + TimeSpan.FromSeconds(2).Ticks);
        await subtitles.WaitForTranslation(controller);
        subtitles.AssertUnchanged(player, controller);
        var selected = kind.EndsWith("audio", StringComparison.Ordinal) ? controller.GetAudioTracks() : controller.GetVideoTracks();
        Assert.Equal(choice.Label, Assert.Single(selected, track => track.IsSelected).Label);
        int rate = kind switch { "embedded-audio" => 44100, "external-audio" => 32000, _ => 48000 };
        Assert.Equal(rate, player.decoder.AudioStream.SampleRate);
        Assert.Equal(kind == "embedded-audio" ? 2 : 1, player.decoder.AudioStream.Channels);
        if (kind.EndsWith("video", StringComparison.Ordinal))
        {
            int width = kind == "embedded-video" ? 640 : 480;
            Assert.Equal((uint)width, player.decoder.VideoStream.Width);
            string color = kind == "embedded-video" ? "blue" : "green";
            await WaitUntil(() => surface.Count > frames && surface.Is(color));
        }
        // A paused switch must stay paused; briefly playing afterward proves the selected decoder submits real PCM.
        if (!playing) player.Play();
        await WaitUntil(() => pcm.HasNonzeroSamplesAt(rate));
        player.Pause();
        await subtitles.WaitForTranslation(controller);
        subtitles.AssertUnchanged(player, controller);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Chapter_commands_seek_real_demuxer_without_applying_subtitle_delay(bool playing)
    {
        using AvStreamTestMedia media = await AvStreamTestMedia.CreateAsync();
        using TempDir state = new();
        await using NativeSession session = new(state);
        Player player = session.Player; FlyleafPlaybackController controller = session.Controller;
        FrameProbe surface = new(); player.Renderer.Surface = surface; player.Renderer.SetControlSize(640, 360);
        Assert.Null(player.Open(media.Media).Error);
        await LoadSubtitles(player, controller, media, embedded: true);
        var subtitles = await TranslateBoth(player, controller);
        await SeekAndWait(player, 1500);
        player.Config.Subtitles[0].Delay = TimeSpan.FromSeconds(1).Ticks;
        player.Config.Subtitles[1].Delay = -TimeSpan.FromMilliseconds(250).Ticks;
        var chapters = controller.GetChapters();
        Assert.Equal(new[] { "Opening", "Middle", "Ending" }, chapters.Select(chapter => chapter.Title));
        // Matroska starts before zero for AAC priming; Demuxer.GetChapters subtracts that native origin.
        long normalizedOrigin = -player.VideoDemuxer.StartTime;
        Assert.InRange(normalizedOrigin, 0, TimeSpan.FromMilliseconds(50).Ticks);
        Assert.Equal(new[] { 0L, TimeSpan.FromSeconds(4).Ticks, TimeSpan.FromSeconds(8).Ticks }.Select(ticks => TimeSpan.FromTicks(ticks + normalizedOrigin)), chapters.Select(chapter => chapter.Start));
        int targetMilliseconds = (int)chapters[1].Start.TotalMilliseconds;
        if (playing) { player.Play(); await WaitUntil(() => player.IsPlaying); }
        long framesBefore = surface.Count;
        System.Collections.Concurrent.ConcurrentQueue<long> requestedTimes = new();
        using PcmProbe pcm = new(player);
        TaskCompletionSource<int> seekCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        void Completed(object? sender, int requestedMs) => seekCompleted.TrySetResult(requestedMs);
        player.SeekCompleted += Completed;
        void Capture(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(Player.CurTime)) requestedTimes.Enqueue(player.CurTime);
        }
        player.PropertyChanged += Capture;
        try { await controller.SeekToChapterAsync(chapters[1]); }
        finally { player.PropertyChanged -= Capture; }
        try
        {
            if (!playing) Assert.Equal(targetMilliseconds, await seekCompleted.Task.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken));
            else await WaitUntil(() => pcm.HasTimestampInRange(TimeSpan.FromSeconds(4).Ticks, TimeSpan.FromSeconds(6).Ticks));
            await WaitUntil(() => surface.Count > framesBefore);
        }
        finally { player.SeekCompleted -= Completed; }
        Assert.Contains(TimeSpan.FromMilliseconds(targetMilliseconds).Ticks, requestedTimes);
        await WaitUntil(() => player.CurTime >= TimeSpan.FromMilliseconds(targetMilliseconds).Ticks && player.CurTime < chapters[1].Start.Ticks + TimeSpan.FromSeconds(2).Ticks);
        Assert.Equal(playing, player.IsPlaying);
        await subtitles.WaitForTranslation(controller);
        subtitles.AssertUnchanged(player, controller, checkDelays: false);
        Assert.Equal(TimeSpan.FromSeconds(1).Ticks, player.Config.Subtitles[0].Delay);
        Assert.Equal(-TimeSpan.FromMilliseconds(250).Ticks, player.Config.Subtitles[1].Delay);
    }

    [AvaloniaTheory]
    [InlineData("audio")]
    [InlineData("video")]
    [InlineData("chapter")]
    public async Task Queued_choices_cannot_mutate_a_native_same_url_replacement(string kind)
    {
        using AvStreamTestMedia media = await AvStreamTestMedia.CreateAsync();
        using TempDir state = new();
        await using NativeSession session = new(state);
        Player player = session.Player; FlyleafPlaybackController controller = session.Controller;
        Assert.Null(player.Open(media.Media).Error);
        PlaybackStreamTrack audio = controller.GetAudioTracks().Single(track => track.Label.Contains("Stereo 44100", StringComparison.Ordinal));
        PlaybackStreamTrack video = controller.GetVideoTracks().Single(track => track.Label.Contains("Blue 640", StringComparison.Ordinal));
        PlaybackChapter chapter = controller.GetChapters()[2];
        using ManualResetEventSlim release = new();
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task barrier = controller.Recognition.MutateAsync(() => { entered.TrySetResult(); if (!release.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException("Choice barrier not released."); });
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Task queued = kind switch { "audio" => controller.SelectAudioTrackAsync(audio), "video" => controller.SelectVideoTrackAsync(video), _ => controller.SeekToChapterAsync(chapter) };
            long oldGeneration = player.MediaGeneration;
            Assert.Null(player.Open(media.Media).Error);
            Assert.True(player.MediaGeneration > oldGeneration);
            var replacementAudio = player.decoder.AudioStream; var replacementVideo = player.decoder.VideoStream;
            long position = player.CurTime;
            release.Set();
            await barrier.WaitAsync(TimeSpan.FromSeconds(15));
            await queued.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Same(replacementAudio, player.decoder.AudioStream);
            Assert.Same(replacementVideo, player.decoder.VideoStream);
            Assert.Equal(position, player.CurTime);
            Assert.False(player.IsPlaying);
        }
        finally { release.Set(); await barrier.WaitAsync(TimeSpan.FromSeconds(15)); }
    }

    [AvaloniaFact]
    public async Task Failed_external_video_reports_error_retains_caches_and_replacement_drops_stale_choices()
    {
        using AvStreamTestMedia media = await AvStreamTestMedia.CreateAsync();
        using TempDir state = new();
        await using NativeSession session = new(state);
        Player player = session.Player; FlyleafPlaybackController controller = session.Controller;
        Assert.Null(player.Open(media.Media).Error);
        await LoadSubtitles(player, controller, media, embedded: true);
        var subtitles = await TranslateBoth(player, controller);
        ExternalVideoStream missing = new() { Url = Path.Combine(media.DirectoryPath, "missing.mkv"), Width = 999, Height = 999, Codec = "h264", Protocol = "file" };
        PlaylistItem.AddExternalStream(missing, player.Playlist.Selected, "AvStreamRegression");
        PlaybackStreamTrack stale = controller.GetVideoTracks().Single(track => track.Label.Contains("999x999", StringComparison.Ordinal));
        string? error = null; int errorCount = 0; controller.ErrorOccurred += message => { error = message; errorCount++; };
        await controller.SelectVideoTrackAsync(stale);
        await WaitUntil(() => error != null);
        Assert.False(string.IsNullOrWhiteSpace(error));
        await subtitles.WaitForTranslation(controller);
        subtitles.AssertUnchanged(player, controller);
        Assert.Null(player.Open(media.Media).Error);
        var replacement = player.decoder.VideoStream;
        await controller.SelectVideoTrackAsync(stale);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, errorCount);
        Assert.Same(replacement, player.decoder.VideoStream);
        Assert.DoesNotContain(player.SubtitlesManager[0].SnapshotSubs(), cue => ReferenceEquals(cue, subtitles.Primary));
        Assert.DoesNotContain(player.SubtitlesManager[1].SnapshotSubs(), cue => ReferenceEquals(cue, subtitles.Secondary));
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task External_video_retains_native_bitmap_cache_owners_until_media_replacement(bool invalidTarget)
    {
        using AvStreamTestMedia media = await AvStreamTestMedia.CreateAsync();
        using TempDir state = new();
        await using NativeSession session = new(state);
        Player player = session.Player; FlyleafPlaybackController controller = session.Controller;
        Assert.Null(player.Open(media.Media).Error);
        await LoadSubtitles(player, controller, media, embedded: true);
        SubtitleData[] cues = [Assert.Single(player.SubtitlesManager[0].SnapshotSubs()), Assert.Single(player.SubtitlesManager[1].SnapshotSubs())];
        SubtitleBitmapData[] owners = [AvStreamTestMedia.CreateNativeBitmap(), AvStreamTestMedia.CreateNativeBitmap()];
        for (int slot = 0; slot < 2; slot++) { cues[slot].Bitmap = owners[slot]; cues[slot].IsBitmap = true; }
        byte[][] pixels = owners.Select(owner => owner.SubToBitmap(false).data).ToArray();
        Assert.All(pixels, data => Assert.Contains(data, value => value != 0));
        ExternalVideoStream video = RegisterVideo(player, media);
        if (invalidTarget) video.Url = Path.Combine(media.DirectoryPath, "missing-bitmap-target.mkv");

        var result = player.OpenExternalVideoPreservingState(video);

        Assert.NotNull(result);
        if (invalidTarget) Assert.False(string.IsNullOrWhiteSpace(result.Error)); else Assert.Null(result.Error);
        for (int slot = 0; slot < 2; slot++)
        {
            Assert.Same(cues[slot], Assert.Single(player.SubtitlesManager[slot].SnapshotSubs()));
            Assert.Same(owners[slot], cues[slot].Bitmap);
            Assert.Equal(pixels[slot], owners[slot].SubToBitmap(false).data);
        }
        Assert.Null(player.Open(media.Media).Error);
        foreach (SubtitleBitmapData owner in owners) Assert.Throws<InvalidOperationException>(() => owner.SubToBitmap(false));
    }
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task External_video_stream_input_preflight_rejects_before_changing_native_or_subtitle_owners(bool playing)
    {
        using AvStreamTestMedia media = await AvStreamTestMedia.CreateAsync();
        using TempDir state = new();
        await using NativeSession session = new(state);
        Player player = session.Player; FlyleafPlaybackController controller = session.Controller;
        Assert.Null(player.Open(media.Media).Error);
        await LoadSubtitles(player, controller, media, embedded: false);
        var subtitles = await TranslateBoth(player, controller);
        await SeekAndWait(player, 2500);
        ExternalVideoStream video = RegisterVideo(player, media);
        using MemoryStream input = new(await File.ReadAllBytesAsync(media.ExternalVideo));
        video.IOStream = input;
        if (playing) { player.Play(); await WaitUntil(() => player.IsPlaying); }
        var originalVideo = player.decoder.VideoStream; var originalAudio = player.decoder.AudioStream;
        var originalExternal = player.Playlist.Selected.ExternalVideoStream;
        long position = player.CurTime, generation = player.MediaGeneration;

        var result = player.OpenExternalVideoPreservingState(video);

        Assert.NotNull(result); Assert.False(string.IsNullOrWhiteSpace(result.Error));
        Assert.Same(originalVideo, player.decoder.VideoStream); Assert.Same(originalAudio, player.decoder.AudioStream);
        Assert.Same(originalExternal, player.Playlist.Selected.ExternalVideoStream);
        Assert.False(video.Enabled); Assert.Equal(playing, player.IsPlaying);
        Assert.Equal(generation, player.MediaGeneration);
        Assert.InRange(player.CurTime, position, position + TimeSpan.FromMilliseconds(500).Ticks);
        await subtitles.WaitForTranslation(controller);
        subtitles.AssertUnchanged(player, controller);
    }
    [AvaloniaTheory]
    [InlineData(1000)]
    [InlineData(-1000)]
    public async Task External_video_primes_actual_retained_pcm_with_the_existing_audio_delay(int delayMilliseconds)
    {
        using AvStreamTestMedia media = await AvStreamTestMedia.CreateAsync();
        using TempDir state = new();
        await using NativeSession session = new(state);
        Player player = session.Player; FlyleafPlaybackController controller = session.Controller;
        Assert.Null(player.Open(media.Media).Error);
        await controller.SelectAudioTrackAsync(controller.GetAudioTracks().Single(track => track.Label.Contains("Mono 48000", StringComparison.Ordinal)));
        await LoadSubtitles(player, controller, media, embedded: true);
        var subtitles = await TranslateBoth(player, controller);
        player.Config.Audio.Delay = TimeSpan.FromMilliseconds(delayMilliseconds).Ticks;
        await SeekAndWait(player, 4000);
        long position = player.CurTime;
        ExternalVideoStream target = RegisterVideo(player, media);

        await controller.SelectVideoTrackAsync(controller.GetVideoTracks().Single(track => track.Label.Contains("480x270", StringComparison.Ordinal)));

        Assert.False(player.IsPlaying);
        Assert.Equal(TimeSpan.FromMilliseconds(delayMilliseconds).Ticks, player.Config.Audio.Delay);
        Assert.Same(target, player.decoder.VideoStream.ExternalStream);
        // ShowOneFrame primes retained audio while paused. Decode that primed queue directly, before Play's
        // BufferVASD resynchronization can hide a seek performed with a temporarily reset audio delay.
        player.AudioDemuxer.Start(); player.AudioDecoder.Start();
        await WaitUntil(() => !player.AudioDecoder.Frames.IsEmpty);
        player.AudioDecoder.Pause();
        Assert.True(player.AudioDecoder.Frames.TryPeek(out AudioFrame? first));
        Assert.NotNull(first);
        byte[] bytes = new byte[Math.Min(first.dataLen, 4096)]; Marshal.Copy(first.dataPtr, bytes, 0, bytes.Length);
        Assert.Contains(bytes, value => value != 0);
        Assert.InRange(first.Timestamp, position - TimeSpan.FromMilliseconds(150).Ticks, position + TimeSpan.FromMilliseconds(150).Ticks);
        await subtitles.WaitForTranslation(controller);
        subtitles.AssertUnchanged(player, controller);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_external_video_restores_the_previous_source_position_and_real_audio(bool playing)
    {
        using AvStreamTestMedia media = await AvStreamTestMedia.CreateAsync();
        using TempDir state = new();
        await using NativeSession session = new(state);
        Player player = session.Player; FlyleafPlaybackController controller = session.Controller;
        FrameProbe surface = new(); player.Renderer.Surface = surface; player.Renderer.SetControlSize(640, 360);
        Assert.Null(player.Open(media.Media).Error);
        Assert.Null(player.Open(player.VideoDemuxer.VideoStreams.Single(stream => stream.Width == 320), defaultAudio: false).Error);
        await LoadSubtitles(player, controller, media, embedded: true);
        var subtitles = await TranslateBoth(player, controller);
        await SeekAndWait(player, 4000);
        await WaitUntil(() => surface.Is("red"));
        ExternalVideoStream target = RegisterVideo(player, media);
        target.Url = Path.Combine(media.DirectoryPath, "missing-at-nonzero-position.mkv");
        string? error = null; controller.ErrorOccurred += message => error = message;
        if (playing) { player.Play(); await WaitUntil(() => player.IsPlaying); }
        long position = player.CurTime, generation = player.MediaGeneration;
        string source = player.VideoDemuxer.Url;
        int streamIndex = player.decoder.VideoStream.StreamIndex;

        await controller.SelectVideoTrackAsync(controller.GetVideoTracks().Single(track => track.Label.Contains("480x270", StringComparison.Ordinal)));

        await WaitUntil(() => error != null);
        Assert.False(string.IsNullOrWhiteSpace(error));
        Assert.Equal(playing, player.IsPlaying);
        Assert.Equal(source, player.VideoDemuxer.Url);
        Assert.NotNull(player.decoder.VideoStream);
        Assert.Equal(streamIndex, player.decoder.VideoStream.StreamIndex);
        Assert.Equal(320u, player.decoder.VideoStream.Width);
        Assert.Equal(generation, player.MediaGeneration);
        Assert.InRange(player.CurTime, position - TimeSpan.FromMilliseconds(250).Ticks, position + TimeSpan.FromSeconds(2).Ticks);
        Assert.Contains("320x180", Assert.Single(controller.GetVideoTracks(), track => track.IsSelected).Label);
        await WaitUntil(() => surface.Is("red"));
        using PcmProbe pcm = new(player);
        if (!playing) player.Play();
        await WaitUntil(() => pcm.HasNonzeroSamplesAt(48000));
        Assert.InRange(pcm.FirstTimestamp, position - TimeSpan.FromMilliseconds(250).Ticks, position + TimeSpan.FromSeconds(2).Ticks);
        player.Pause();
        await subtitles.WaitForTranslation(controller);
        subtitles.AssertUnchanged(player, controller);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task External_video_can_return_to_the_previously_selected_original_source(bool playing)
    {
        using AvStreamTestMedia media = await AvStreamTestMedia.CreateAsync();
        using TempDir state = new();
        await using NativeSession session = new(state);
        Player player = session.Player; FlyleafPlaybackController controller = session.Controller;
        FrameProbe surface = new(); player.Renderer.Surface = surface; player.Renderer.SetControlSize(640, 360);
        Assert.Null(player.Open(media.Media).Error);
        Assert.Null(player.Open(player.VideoDemuxer.VideoStreams.Single(stream => stream.Width == 320), defaultAudio: false).Error);
        await LoadSubtitles(player, controller, media, embedded: true);
        var subtitles = await TranslateBoth(player, controller);
        await SeekAndWait(player, 4000);
        string originalSource = player.VideoDemuxer.Url;
        int originalIndex = player.decoder.VideoStream.StreamIndex;
        RegisterVideo(player, media);
        if (playing) { player.Play(); await WaitUntil(() => player.IsPlaying); }
        long generation = player.MediaGeneration, position = player.CurTime;
        await controller.SelectVideoTrackAsync(controller.GetVideoTracks().Single(track => track.Label.Contains("480x270", StringComparison.Ordinal)));
        await WaitUntil(() => surface.Is("green"));
        Assert.Contains("480x270", Assert.Single(controller.GetVideoTracks(), track => track.IsSelected).Label);
        PlaybackStreamTrack previous = Assert.Single(controller.GetVideoTracks(), track => !track.IsSelected && track.Label.Contains("320x180", StringComparison.Ordinal));

        await controller.SelectVideoTrackAsync(previous);

        Assert.Equal(playing, player.IsPlaying);
        Assert.Equal(generation, player.MediaGeneration);
        Assert.Equal(originalSource, player.VideoDemuxer.Url);
        Assert.Equal(originalIndex, player.decoder.VideoStream.StreamIndex);
        Assert.Equal(320u, player.decoder.VideoStream.Width);
        Assert.Contains("320x180", Assert.Single(controller.GetVideoTracks(), track => track.IsSelected).Label);
        Assert.False(Assert.Single(controller.GetVideoTracks(), track => track.Label.Contains("480x270", StringComparison.Ordinal)).IsSelected);
        Assert.InRange(player.CurTime, position - TimeSpan.FromMilliseconds(250).Ticks, position + TimeSpan.FromSeconds(2).Ticks);
        await WaitUntil(() => surface.Is("red"));
        using PcmProbe pcm = new(player);
        if (!playing) player.Play();
        await WaitUntil(() => pcm.HasNonzeroSamplesAt(48000));
        Assert.InRange(pcm.FirstTimestamp, position - TimeSpan.FromMilliseconds(250).Ticks, position + TimeSpan.FromSeconds(2).Ticks);
        player.Pause();
        await subtitles.WaitForTranslation(controller);
        subtitles.AssertUnchanged(player, controller);
    }
    [AvaloniaTheory]
    [InlineData("replace")]
    [InlineData("dispose")]
    public async Task Native_media_mutation_keeps_validation_and_action_atomic_against_reset_or_disposal(string operation)
    {
        using AvStreamTestMedia media = await AvStreamTestMedia.CreateAsync();
        using TempDir state = new();
        await using NativeSession session = new(state);
        Player player = session.Player;
        Assert.Null(player.Open(media.Media).Error);
        // This test exercises the public native primitive without adapter queues or unsafe stale AVStream use.
        await session.Controller.DisposeAsync();
        long expectedGeneration = player.MediaGeneration, observedGeneration = -1;
        PlaylistItem expectedItem = player.Playlist.Selected;
        bool observedDisposed = true;
        using ManualResetEventSlim releasePredicate = new(), releaseMutation = new();
        TaskCompletionSource predicateEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource mutationEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource competingStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        string? competingError = null;
        void Opening(object? sender, OpeningArgs args) { if (!args.IsSubtitles) competingStarted.TrySetResult(); }
        player.Opening += Opening;
        Task<bool> mutation = Task.Run(() => player.TryMutateMedia(expectedGeneration, expectedItem, () =>
        {
            predicateEntered.TrySetResult();
            if (!releasePredicate.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException("Atomic predicate barrier not released.");
            return true;
        }, () =>
        {
            observedGeneration = player.MediaGeneration; observedDisposed = player.IsDisposed;
            mutationEntered.TrySetResult();
            if (!releaseMutation.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException("Atomic mutation barrier not released.");
        }));
        Task? competing = null;
        bool completedBeforePredicateReleased = false, completedDuringMutation = false;
        try
        {
            await predicateEntered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            competing = Task.Run(() =>
            {
                if (operation == "dispose") { competingStarted.TrySetResult(); player.Dispose(); }
                else competingError = player.Open(media.Media).Error;
            });
            await competingStarted.Task.WaitAsync(TimeSpan.FromSeconds(15));
            // The public Opening/start handshake proves the competing worker was admitted. Give the
            // unlocked RED implementation a bounded opportunity to finish; locking must keep it pending.
            try { await competing.WaitAsync(TimeSpan.FromMilliseconds(750)); }
            catch (TimeoutException) { }
            completedBeforePredicateReleased = competing.IsCompleted;
            releasePredicate.Set();
            await mutationEntered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            completedDuringMutation = competing.IsCompleted;
            releaseMutation.Set();
            Assert.True(await mutation.WaitAsync(TimeSpan.FromSeconds(15)));
            await competing.WaitAsync(TimeSpan.FromSeconds(15));
        }
        finally
        {
            releasePredicate.Set(); releaseMutation.Set(); player.Opening -= Opening;
            await mutation.WaitAsync(TimeSpan.FromSeconds(15));
            if (competing != null) await competing.WaitAsync(TimeSpan.FromSeconds(15));
        }
        Assert.Equal(expectedGeneration, observedGeneration);
        Assert.False(observedDisposed);
        Assert.False(completedBeforePredicateReleased, "Native replacement/disposal completed while the current-source predicate was held.");
        Assert.False(completedDuringMutation, "Native replacement/disposal completed before the admitted mutation returned.");
        Assert.Null(competingError);
        if (operation == "dispose") Assert.True(player.IsDisposed);
        else Assert.True(player.MediaGeneration > expectedGeneration);
    }
    [AvaloniaFact]
    public async Task Repeated_video_switch_after_native_track_reselection_reuses_the_registered_retained_owners()
    {
        using AvStreamTestMedia media = await AvStreamTestMedia.CreateAsync();
        using TempDir state = new();
        await using NativeSession session = new(state);
        Player player = session.Player; FlyleafPlaybackController controller = session.Controller;
        FrameProbe surface = new(); player.Renderer.Surface = surface; player.Renderer.SetControlSize(640, 360);
        Assert.Null(player.Open(media.Media).Error);
        Assert.Null(player.Open(player.VideoDemuxer.VideoStreams.Single(stream => stream.Width == 320), defaultAudio: false).Error);
        await controller.SelectAudioTrackAsync(controller.GetAudioTracks().Single(track => track.Label.Contains("Mono 48000", StringComparison.Ordinal)));
        await LoadSubtitles(player, controller, media, embedded: true);
        await SeekAndWait(player, 4000);
        RegisterVideo(player, media);
        long generation = player.MediaGeneration;
        PlaylistItem item = player.Playlist.Selected;
        await controller.SelectVideoTrackAsync(controller.GetVideoTracks().Single(track => track.Label.Contains("480x270", StringComparison.Ordinal)));
        await WaitUntil(() => surface.Is("green"));
        ExternalAudioStream retainedAudio = Assert.IsAssignableFrom<ExternalAudioStream>(player.decoder.AudioStream.ExternalStream);
        ExternalSubtitlesStream[] retainedSubtitles =
        [
            Assert.IsAssignableFrom<ExternalSubtitlesStream>(player.decoder.SubtitlesStreams[0].ExternalStream),
            Assert.IsAssignableFrom<ExternalSubtitlesStream>(player.decoder.SubtitlesStreams[1].ExternalStream)
        ];
        int audioCount = item.ExternalAudioStreams.Count, subtitlesCount = item.ExternalSubtitlesStreamsAll.Count;
        Assert.Contains(retainedAudio, item.ExternalAudioStreams);
        Assert.All(retainedSubtitles, subtitle => Assert.Contains(subtitle, item.ExternalSubtitlesStreamsAll));
        await controller.SelectVideoTrackAsync(Assert.Single(controller.GetVideoTracks(), track => !track.IsSelected && track.Label.Contains("320x180", StringComparison.Ordinal)));
        await WaitUntil(() => surface.Is("red"));

        // Returning to A retains its external owners. Explicit native reselection creates the second
        // migration: without this step a repeated video switch cannot expose duplicate retainers.
        await controller.SelectAudioTrackAsync(controller.GetAudioTracks().Single(track => track.Label.Contains("Mono 48000", StringComparison.Ordinal)));
        await LoadSubtitles(player, controller, media, embedded: true);
        Assert.True(player.AudioDecoder.OnVideoDemuxer);
        Assert.All(player.SubtitlesDecoders, decoder => Assert.True(decoder.OnVideoDemuxer));
        int audioIndex = player.decoder.AudioStream.StreamIndex;
        int[] subtitleIndices = player.decoder.SubtitlesStreams.Select(stream => stream.StreamIndex).ToArray();
        var reselectedCues = await TranslateBoth(player, controller);
        await SeekAndWait(player, 4000);
        long position = player.CurTime;

        await controller.SelectVideoTrackAsync(controller.GetVideoTracks().Single(track => track.Label.Contains("480x270", StringComparison.Ordinal)));

        Assert.Equal(audioCount, item.ExternalAudioStreams.Count);
        Assert.Equal(subtitlesCount, item.ExternalSubtitlesStreamsAll.Count);
        Assert.Same(retainedAudio, player.decoder.AudioStream.ExternalStream);
        for (int slot = 0; slot < 2; slot++)
        {
            Assert.Same(retainedSubtitles[slot], player.decoder.SubtitlesStreams[slot].ExternalStream);
            Assert.Equal(subtitleIndices[slot], player.decoder.SubtitlesStreams[slot].StreamIndex);
        }
        Assert.Equal(audioIndex, player.decoder.AudioStream.StreamIndex);
        Assert.Equal(media.Media, player.decoder.AudioStream.Demuxer.Url);
        Assert.Equal(media.ExternalVideo, player.VideoDemuxer.Url);
        Assert.Equal(480u, player.decoder.VideoStream.Width);
        Assert.Equal(generation, player.MediaGeneration);
        Assert.False(player.IsPlaying);
        Assert.Contains("480x270", Assert.Single(controller.GetVideoTracks(), track => track.IsSelected).Label);
        Assert.InRange(player.CurTime, position - TimeSpan.FromMilliseconds(250).Ticks, position + TimeSpan.FromMilliseconds(250).Ticks);
        await WaitUntil(() => surface.Is("green"));
        using PcmProbe pcm = new(player);
        player.Play();
        await WaitUntil(() => pcm.HasNonzeroSamplesAt(48000));
        Assert.InRange(pcm.FirstTimestamp, position - TimeSpan.FromMilliseconds(250).Ticks, position + TimeSpan.FromSeconds(2).Ticks);
        player.Pause();
        await reselectedCues.WaitForTranslation(controller);
        reselectedCues.AssertUnchanged(player, controller);
    }
    sealed class NativeSession : IAsyncDisposable
    {
        readonly IAudioBackend previousBackend;
        public Player Player { get; }
        public FlyleafPlaybackController Controller { get; }
        public NativeSession(TempDir state)
        {
            AvaloniaUIDispatcher dispatcher = new(); CollectionSyncBridge bridge = new(dispatcher);
            EngineBootstrap.InstallSeams(dispatcher, new AvaloniaHostServices(() => null), bridge, new KeyMapper());
            if (!Engine.IsLoaded)
                EngineBootstrap.Start(EngineBootstrap.CreateEngineConfig(Environment.GetEnvironmentVariable("LLPLAYER_FFMPEG_DIR")!, new AppPaths(Path.Combine(state.Path, "config"), Path.Combine(state.Path, "state"))));
            previousBackend = AudioEngine.Backend; AudioEngine.Backend = NullAudioBackend.Instance;
            Config config = EngineBootstrap.CreatePlayerConfig(new AppPrefs().Normalize());
            config.Player.AutoPlay = false; config.Subtitles.SearchLocal = false; config.Subtitles.LanguageAutoDetect = false;
            Player = new(config); Controller = new(Player, dispatcher, bridge);
        }
        public async ValueTask DisposeAsync()
        {
            try { await Controller.DisposeAsync(); Player.Dispose(); }
            finally { AudioEngine.Backend = previousBackend; }
        }
    }

    static ExternalVideoStream RegisterVideo(Player player, AvStreamTestMedia media)
    {
        ExternalVideoStream video = new() { Url = media.ExternalVideo, Width = 480, Height = 270, FPS = 25, HasAudio = false, Codec = "h264", Protocol = "file" };
        PlaylistItem.AddExternalStream(video, player.Playlist.Selected, "AvStreamRegression"); return video;
    }

    static void RegisterAudio(Player player, AvStreamTestMedia media)
        => PlaylistItem.AddExternalStream(new ExternalAudioStream { Url = media.ExternalAudio, SampleRate = 32000, ChannelLayout = "mono", Language = Language.English, Codec = "external-audio-regression", Protocol = "file" }, player.Playlist.Selected, "AvStreamRegression");

    static async Task LoadSubtitles(Player player, FlyleafPlaybackController controller, AvStreamTestMedia media, bool embedded)
    {
        for (int slot = 0; slot < 2; slot++)
        {
            object source;
            if (embedded) source = player.VideoDemuxer.SubtitlesStreamsAll[slot];
            else
            {
                string path = slot == 0 ? media.Primary : media.Secondary;
                ExternalSubtitlesStream stream = new() { Url = path, Title = Path.GetFileName(path), Language = Language.English, Downloaded = true };
                PlaylistItem.AddExternalStream(stream, player.Playlist.Selected, "AvStreamRegression"); source = stream;
            }
            controller.SelectSubtitleTrack(slot, controller.GetSubtitleTracks().Single(track => ReferenceEquals(track.Source, source)));
            await controller.Recognition.MutateAsync(() => { });
            int currentSlot = slot;
            await WaitUntil(() => !player.SubtitlesManager[currentSlot].IsLoading && player.SubtitlesManager[currentSlot].SnapshotSubs().Count == 1);
        }
    }

    static async Task<SubtitleSnapshot> TranslateBoth(Player player, FlyleafPlaybackController controller)
    {
        controller.ApplyTranslationSettings(new() { SentenceProvider = "GoogleV1", TargetLanguage = TargetLanguage.Russian });
        for (int slot = 0; slot < 2; slot++)
        {
            int currentSlot = slot;
            player.SubtitlesManager[slot].Translator.ServiceFactoryOverride = () => new OfflineProvider(currentSlot == 0 ? "Primary translated" : "Secondary translated");
            if (!(slot == 0 ? controller.PrimaryTranslationEnabled : controller.SecondaryTranslationEnabled)) await controller.ToggleTranslationAsync(slot);
        }
        player.SubtitlesManager.SetCurrentTime(TimeSpan.FromSeconds(2));
        await WaitUntil(() => player.SubtitlesManager[0].SnapshotSubs().Single().IsTranslated && player.SubtitlesManager[1].SnapshotSubs().Single().IsTranslated);
        return new(player, controller);
    }

    sealed class SubtitleSnapshot
    {
        public SubtitleData Primary { get; }
        public SubtitleData Secondary { get; }
        readonly CueInfo[] primaryCues, secondaryCues;
        readonly long primaryDelay, secondaryDelay;
        public SubtitleSnapshot(Player player, FlyleafPlaybackController controller)
        {
            Primary = Assert.Single(player.SubtitlesManager[0].SnapshotSubs()); Secondary = Assert.Single(player.SubtitlesManager[1].SnapshotSubs());
            primaryCues = controller.GetCues(0).ToArray(); secondaryCues = controller.GetCues(1).ToArray();
            primaryDelay = player.Config.Subtitles[0].Delay; secondaryDelay = player.Config.Subtitles[1].Delay;
            Assert.True(Assert.Single(primaryCues).IsTranslated); Assert.True(Assert.Single(secondaryCues).IsTranslated);
        }
        public Task WaitForTranslation(FlyleafPlaybackController controller)
        {
            Assert.True(controller.PrimaryTranslationEnabled); Assert.True(controller.SecondaryTranslationEnabled);
            return WaitUntil(() => controller.GetCues(0).Count == 1 && controller.GetCues(0)[0].IsTranslated && controller.GetCues(1).Count == 1 && controller.GetCues(1)[0].IsTranslated);
        }
        public void AssertUnchanged(Player player, FlyleafPlaybackController controller, bool checkDelays = true)
        {
            Assert.Same(Primary, Assert.Single(player.SubtitlesManager[0].SnapshotSubs())); Assert.Same(Secondary, Assert.Single(player.SubtitlesManager[1].SnapshotSubs()));
            AssertCueMetadataAndIdentity(primaryCues, controller, 0);
            AssertCueMetadataAndIdentity(secondaryCues, controller, 1);
            Assert.True(controller.PrimaryTranslationEnabled); Assert.True(controller.SecondaryTranslationEnabled);
            Assert.Equal(TargetLanguage.Russian, player.Config.Subtitles.TranslateTargetLanguage);
            if (checkDelays) { Assert.Equal(primaryDelay, player.Config.Subtitles[0].Delay); Assert.Equal(secondaryDelay, player.Config.Subtitles[1].Delay); }
        }

        static void AssertCueMetadataAndIdentity(CueInfo[] previous, FlyleafPlaybackController controller, int slot)
        {
            CueInfo[] current = controller.GetCues(slot).ToArray();
            // Cache preservation covers every visible/raw cue field. A native source rebind must instead
            // retire its old command identity, even when it deliberately preserves the same cue objects.
            Assert.Equal(previous.Select(cue => cue with { Identity = null }), current.Select(cue => cue with { Identity = null }));
            for (int i = 0; i < current.Length; i++)
            {
                Assert.NotNull(current[i].Identity);
                Assert.True(controller.IsCueCurrent(current[i], slot));
                Assert.Equal(Equals(previous[i].Identity, current[i].Identity), controller.IsCueCurrent(previous[i], slot));
            }
        }
    }

    sealed class OfflineProvider(string text) : ITranslateService
    {
        public TranslateServiceType ServiceType => TranslateServiceType.GoogleV1;
        public void Initialize(Language source, TargetLanguage target) { }
        public Task<string> TranslateAsync(string source, CancellationToken token) { token.ThrowIfCancellationRequested(); return Task.FromResult(text); }
        public void Dispose() { }
    }

    sealed class FrameProbe : IVideoSurface
    {
        record Pixel(byte Red, byte Green, byte Blue);
        Pixel? pixel;
        long count;
        public long Count => Interlocked.Read(ref count);
        public void PresentFrame(ReadOnlySpan<byte> bgra, int width, int height, int stride)
        {
            int offset = height / 2 * stride + width / 2 * 4;
            Volatile.Write(ref pixel, new Pixel(bgra[offset + 2], bgra[offset + 1], bgra[offset])); Interlocked.Increment(ref count);
        }
        public bool Is(string color)
        {
            Pixel? current = Volatile.Read(ref pixel);
            return current != null && (color switch
            {
                "red" => current.Red > 180 && current.Green < 30 && current.Blue < 30,
                "blue" => current.Blue > 180 && current.Red < 30 && current.Green < 30,
                _ => current.Green > 80 && current.Red < 30 && current.Blue < 30
            });
        }
        public void ClearFrame() => Volatile.Write(ref pixel, null);
    }

    sealed class PcmProbe : IDisposable
    {
        readonly Player player;
        readonly System.Collections.Concurrent.ConcurrentDictionary<int, bool> rates = new();
        long lastTimestamp = -1, firstTimestamp = -1;
        public long FirstTimestamp => Interlocked.Read(ref firstTimestamp);
        public PcmProbe(Player player) { this.player = player; player.Audio.SamplesAdded += Added; }
        void Added(object? sender, AudioFrame frame)
        {
            if (frame.dataLen <= 0) return;
            byte[] bytes = new byte[Math.Min(frame.dataLen, 4096)]; Marshal.Copy(frame.dataPtr, bytes, 0, bytes.Length);
            if (bytes.Any(value => value != 0))
            {
                rates[player.Audio.SampleRate] = true; Interlocked.CompareExchange(ref firstTimestamp, frame.Timestamp, -1); Interlocked.Exchange(ref lastTimestamp, frame.Timestamp);
            }
        }
        public bool HasNonzeroSamplesAt(int rate) => rates.ContainsKey(rate);
        public bool HasTimestampInRange(long first, long last) { long timestamp = Interlocked.Read(ref lastTimestamp); return timestamp >= first && timestamp < last; }
        public void Dispose() => player.Audio.SamplesAdded -= Added;
    }

    static async Task SeekAndWait(Player player, int milliseconds)
    {
        TaskCompletionSource<int> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        void Completed(object? sender, int requested) => completion.TrySetResult(requested);
        player.SeekCompleted += Completed;
        try
        {
            player.SeekAccurate(milliseconds);
            Assert.Equal(milliseconds, await completion.Task.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken));
        }
        finally { player.SeekCompleted -= Completed; }
        Dispatcher.UIThread.RunJobs();
    }

    static async Task WaitUntil(Func<bool> predicate)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(15);
        while (!predicate())
        {
            Dispatcher.UIThread.RunJobs();
            if (DateTime.UtcNow >= deadline) throw new TimeoutException("Native A/V regression condition did not become true.");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
        Dispatcher.UIThread.RunJobs();
    }
}
