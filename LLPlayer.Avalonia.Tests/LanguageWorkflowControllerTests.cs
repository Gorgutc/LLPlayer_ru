using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using System.Reflection;
using FlyleafLib;
using FlyleafLib.MediaFramework.MediaPlaylist;
using FlyleafLib.MediaFramework.MediaStream;
using FlyleafLib.MediaPlayer;
using FlyleafLib.MediaPlayer.Dubbing;
using LLPlayer.Avalonia.Services;

namespace LLPlayer.Avalonia.Tests;

[Collection(FlyleafGlobalsCollection.Name)]
public sealed class LanguageWorkflowControllerTests
{
    [AvaloniaFact]
    public async Task Subtitle_source_replacement_before_debounce_keeps_earlier_voice_when_shutdown_drains()
    {
        using AvStreamTestMedia media = await AvStreamTestMedia.CreateAsync();
        using TempDir state = new();
        await using Session session = new(state);
        await session.OpenAsync(media);
        var player = session.Player;
        var controller = session.Controller;
        controller.SelectSubtitleTrack(1, null);
        await controller.Recognition.MutateAsync(() => { });
        await Until(() => !player.Subtitles[1].Enabled);
        player.Config.Subtitles.PersistPerLineVoices = true;
        FreezeVoiceDebounce(session);
        player.SubtitlesManager[0].Subs[0].EndTime = TimeSpan.FromSeconds(4);
        CueInfo firstSource = Assert.Single(controller.GetCues(0));
        SubtitleData firstTiming = Copy(player.SubtitlesManager[0].Subs[0]);
        await controller.SetCueVoiceAsync(firstSource, 0, "voice-A");

        string replacementPath = Path.Combine(state.Path, "replacement.srt");
        await File.WriteAllTextAsync(replacementPath,
            "1\n00:00:00,000 --> 00:00:04,000\nReplacement unedited line A\n\n2\n00:00:06,000 --> 00:00:09,000\nReplacement line C\n\n");
        ExternalSubtitlesStream replacement = new() { Url = replacementPath, Title = "Replacement", Language = Language.English, Downloaded = true };
        PlaylistItem.AddExternalStream(replacement, player.Playlist.Selected, "LanguageWorkflowTests");
        controller.SelectSubtitleTrack(0, controller.GetSubtitleTracks().Single(track => ReferenceEquals(track.Source, replacement)));
        await controller.Recognition.MutateAsync(() => { });
        await Until(() => !player.SubtitlesManager[0].IsLoading
            && ReferenceEquals(player.decoder.SubtitlesStreams[0]?.ExternalStream, replacement)
            && controller.GetCues(0).Count == 2);
        Assert.False(controller.IsCueCurrent(firstSource, 0));
        CueInfo unedited = Assert.Single(controller.GetCues(0), cue => cue.Text == "Replacement unedited line A");
        Assert.Equal(firstTiming.StartTime, unedited.Start);
        Assert.Equal(firstTiming.EndTime, unedited.End);
        Assert.Null(unedited.AssignedVoiceId);
        await controller.SetCueVoiceAsync(Assert.Single(controller.GetCues(0), cue => cue.Text == "Replacement line C"), 0, "voice-C");
        SubtitleData replacementTiming = Copy(Assert.Single(player.SubtitlesManager[0].Subs, cue => cue.Text == "Replacement line C"));
        Assert.NotEqual(firstTiming.StartTime, replacementTiming.StartTime);
        Assert.False(File.Exists(DubbingVoiceAssignmentStore.BuildVoicesPath(media.Media)));

        var batchBeforeDrain = controller.CaptureBatchVoiceAssignments();
        Assert.NotNull(batchBeforeDrain);
        SubtitleData[] immediate = [Copy(firstTiming), Copy(replacementTiming)];
        batchBeforeDrain.Apply(media.Media, immediate);
        Assert.Equal(new[] { "voice-A", "voice-C" }, immediate.Select(cue => cue.AssignedVoiceId));

        await controller.DrainVoiceAssignmentsAsync();
        SubtitleData[] restored = [firstTiming, replacementTiming];
        DubbingVoiceAssignmentStore.LoadMap(media.Media).Apply(media.Media, restored);
        Assert.Equal(new[] { "voice-A", "voice-C" }, restored.Select(cue => cue.AssignedVoiceId));
        Assert.Equal(immediate.Select(cue => cue.AssignedVoiceId), restored.Select(cue => cue.AssignedVoiceId));
    }

    [AvaloniaTheory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    public async Task Equal_timing_slots_use_latest_explicit_edit_for_immediate_batch_and_persisted_voice(int firstSlot, bool clearLatest)
    {
        using AvStreamTestMedia media = await AvStreamTestMedia.CreateAsync();
        using TempDir state = new();
        await using Session session = new(state);
        await session.OpenAsync(media);
        var player = session.Player;
        var controller = session.Controller;
        SubtitleData timing = Copy(player.SubtitlesManager[0].Subs[0]);
        Assert.Equal(timing.StartTime, player.SubtitlesManager[1].Subs[0].StartTime);
        Assert.Equal(timing.EndTime, player.SubtitlesManager[1].Subs[0].EndTime);
        SubtitleData existingDisk = Copy(timing); existingDisk.AssignedVoiceId = "older-disk-voice";
        DubbingVoiceAssignmentStore.SaveAtomic(media.Media, [existingDisk]);
        player.Config.Subtitles.PersistPerLineVoices = true;
        FreezeVoiceDebounce(session);
        await controller.SetCueVoiceAsync(Assert.Single(controller.GetCues(firstSlot)), firstSlot, "earlier-session-voice");
        int latestSlot = 1 - firstSlot;
        string? expected = clearLatest ? null : "latest-session-voice";
        await controller.SetCueVoiceAsync(Assert.Single(controller.GetCues(latestSlot)), latestSlot, expected);

        // Disk is intentionally still old: immediate batch input must honor the edit journal before any flush.
        SubtitleData diskBeforeDrain = Copy(timing);
        DubbingVoiceAssignmentStore.LoadMap(media.Media).Apply(media.Media, [diskBeforeDrain]);
        Assert.Equal("older-disk-voice", diskBeforeDrain.AssignedVoiceId);
        var batch = controller.CaptureBatchVoiceAssignments();
        Assert.NotNull(batch);
        SubtitleData immediate = Copy(timing);
        batch.Apply(media.Media, [immediate]);
        Assert.Equal(expected, immediate.AssignedVoiceId);

        await controller.DrainVoiceAssignmentsAsync();
        SubtitleData persisted = Copy(timing);
        DubbingVoiceAssignmentStore.LoadMap(media.Media).Apply(media.Media, [persisted]);
        Assert.Equal(expected, persisted.AssignedVoiceId);
        Assert.Equal(immediate.AssignedVoiceId, persisted.AssignedVoiceId);
    }

    static void FreezeVoiceDebounce(Session session)
    {
        // Test-only injection keeps the real production queue pending until its real shutdown drain. This
        // removes native-cache duration from the regression: no sleeps or production timing changes.
        FieldInfo? field = typeof(FlyleafPlaybackController).GetField("voiceSaveQueue",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        Assert.Null(field.GetValue(session.Controller));
        field.SetValue(session.Controller, new DubbingVoiceSaveQueue(
            () => session.Player.Config.Subtitles.PersistPerLineVoices, Timeout.InfiniteTimeSpan));
    }

    [AvaloniaFact]
    public async Task Batch_config_capture_preserves_data_plugin_options_and_nested_settings_without_live_aliases()
    {
        using TempDir state = new();
        await using Session session = new(state);
        Config live = session.Player.Config;
        live.Data = new Config.DataConfig { Enabled = true };
        live.Plugins["language-workflow-test"] = new Utils.ObservableDictionary<string, string> { ["option"] = "captured" };
        live.Subtitles.DubbingConfig.CustomVoiceIds = ["captured-voice"];
        live.Audio.Languages = [Language.Get("ja")];
        live.Version = "language-workflow-snapshot-version";
        Config snapshot = session.Controller.CaptureLanguageWorkflowConfig();

        Assert.NotSame(live, snapshot);
        Assert.Equal(live.Version, snapshot.Version);
        Assert.True(snapshot.Data.Enabled);
        Assert.NotSame(live.Data, snapshot.Data);
        Assert.Equal("captured", snapshot.Plugins["language-workflow-test"]["option"]);
        Assert.NotSame(live.Plugins["language-workflow-test"], snapshot.Plugins["language-workflow-test"]);
        live.Plugins["language-workflow-test"]["option"] = "new-live-value";
        live.Subtitles.DubbingConfig.CustomVoiceIds.Add("later-voice");
        live.Audio.Languages.Clear();
        Assert.Equal("captured", snapshot.Plugins["language-workflow-test"]["option"]);
        Assert.Equal(new[] { "captured-voice" }, snapshot.Subtitles.DubbingConfig.CustomVoiceIds);
        Assert.Equal("ja", Assert.Single(snapshot.Audio.Languages).ISO6391);
        snapshot.Data.Enabled = false;
        Assert.True(live.Data.Enabled);
    }

    [AvaloniaFact]
    public async Task Voice_edits_require_current_slot_timing_external_source_tuple_and_media_generation()
    {
        using AvStreamTestMedia media = await AvStreamTestMedia.CreateAsync();
        using TempDir state = new();
        await using Session session = new(state);
        await session.OpenAsync(media);
        var controller = session.Controller;
        CueInfo original = Assert.Single(controller.GetCues(0));
        Assert.NotNull(original.Identity);
        await controller.SetCueVoiceAsync(original, 0, "first");
        Assert.Equal("first", controller.GetCueVoiceId(original, 0));

        await Assert.ThrowsAsync<InvalidOperationException>(() => controller.SetCueVoiceAsync(original, 1, "wrong-slot"));
        Assert.Null(controller.GetCueVoiceId(original, 1));
        Assert.Null(Assert.Single(controller.GetCues(1)).AssignedVoiceId);

        SubtitleData live = session.Player.SubtitlesManager[0].Subs[0];
        live.StartTime += TimeSpan.FromMilliseconds(1);
        Assert.False(controller.IsCueCurrent(original, 0));
        await Assert.ThrowsAsync<InvalidOperationException>(() => controller.SetCueVoiceAsync(original, 0, "stale-timing"));
        Assert.Equal("first", live.AssignedVoiceId);
        CueInfo current = Assert.Single(controller.GetCues(0));
        await controller.SetCueVoiceAsync(current, 0, "current-timing");
        Assert.Equal("current-timing", live.AssignedVoiceId);

        // Mutate just one field of the external source tuple. Index, cue reference, text and timing stay equal.
        session.Primary!.Url = media.Secondary;
        Assert.False(controller.IsCueCurrent(current, 0));
        Assert.Null(controller.GetCueVoiceId(current, 0));
        await Assert.ThrowsAsync<InvalidOperationException>(() => controller.SetCueVoiceAsync(current, 0, "stale-source"));
        Assert.Equal("current-timing", live.AssignedVoiceId);
        CueInfo replacementSource = Assert.Single(controller.GetCues(0));
        Assert.True(controller.IsCueCurrent(replacementSource, 0));
        await controller.SetCueVoiceAsync(replacementSource, 0, "current-source");
        Assert.Equal("current-source", live.AssignedVoiceId);

        Assert.Null(session.Player.Open(media.ExternalVideo).Error);
        Assert.False(controller.IsCueCurrent(replacementSource, 0));
        await Assert.ThrowsAsync<InvalidOperationException>(() => controller.SetCueVoiceAsync(replacementSource, 0, "stale-media"));
        Assert.Null(controller.GetCueVoiceId(replacementSource, 0));
        Assert.Equal(media.ExternalVideo, controller.CurrentMediaPath);
        Assert.All(controller.GetCues(0), cue => Assert.NotEqual("stale-media", cue.AssignedVoiceId));
    }

    [AvaloniaFact]
    public async Task Sequential_edits_persist_both_loaded_slots_and_batch_snapshot_is_detached()
    {
        using AvStreamTestMedia media = await AvStreamTestMedia.CreateAsync();
        using TempDir state = new();
        await using Session session = new(state);
        await session.OpenAsync(media);
        var player = session.Player;
        var controller = session.Controller;
        // The standard fixture gives both languages the same interval. Use separate authored intervals here
        // so this test verifies both slot entries rather than the map's intentional first-timing-wins rule.
        player.SubtitlesManager[1].Subs[0].StartTime = TimeSpan.FromSeconds(6);
        player.SubtitlesManager[1].Subs[0].EndTime = TimeSpan.FromSeconds(10);
        player.Config.Subtitles.PersistPerLineVoices = true;
        await controller.SetCueVoiceAsync(Assert.Single(controller.GetCues(0)), 0, "primary-voice");
        await controller.SetCueVoiceAsync(Assert.Single(controller.GetCues(1)), 1, "secondary-voice");
        await controller.DrainVoiceAssignmentsAsync();

        SubtitleData[] diskTargets = [Copy(player.SubtitlesManager[0].Subs[0]), Copy(player.SubtitlesManager[1].Subs[0])];
        DubbingVoiceAssignmentStore.LoadMap(media.Media).Apply(media.Media, diskTargets);
        Assert.Equal(new[] { "primary-voice", "secondary-voice" }, diskTargets.Select(cue => cue.AssignedVoiceId));

        // Disable disk merging: values below must come from the controller's detached current-session snapshot.
        player.Config.Subtitles.PersistPerLineVoices = false;
        var snapshot = controller.CaptureBatchVoiceAssignments();
        Assert.NotNull(snapshot);
        await controller.SetCueVoiceAsync(Assert.Single(controller.GetCues(0)), 0, "new-primary");
        await controller.SetCueVoiceAsync(Assert.Single(controller.GetCues(1)), 1, null);
        SubtitleData[] rendered = [Copy(diskTargets[0]), Copy(diskTargets[1])];
        snapshot.Apply(media.Media, rendered);
        Assert.Equal(new[] { "primary-voice", "secondary-voice" }, rendered.Select(cue => cue.AssignedVoiceId));
        Assert.Equal("new-primary", Assert.Single(controller.GetCues(0)).AssignedVoiceId);
        Assert.Null(Assert.Single(controller.GetCues(1)).AssignedVoiceId);
        SubtitleData[] unrelated = [Copy(diskTargets[0]), Copy(diskTargets[1])];
        snapshot.Apply(media.ExternalVideo, unrelated);
        Assert.All(unrelated, cue => Assert.Null(cue.AssignedVoiceId));
    }

    [AvaloniaFact]
    public async Task First_explicit_clear_restores_other_saved_cues_and_later_notifications_do_not_resurrect_clear()
    {
        using AvStreamTestMedia media = await AvStreamTestMedia.CreateAsync();
        using TempDir state = new();
        await using Session session = new(state);
        await session.OpenAsync(media);
        var player = session.Player;
        var controller = session.Controller;
        SubtitleData first = player.SubtitlesManager[0].Subs[0];
        SubtitleData second = new() { Text = "second line", StartTime = TimeSpan.FromSeconds(5), EndTime = TimeSpan.FromSeconds(7) };
        first.EndTime = TimeSpan.FromSeconds(4);
        player.SubtitlesManager[0].Add(second);
        SubtitleData savedFirst = Copy(first); savedFirst.AssignedVoiceId = "disk-A";
        SubtitleData savedSecond = Copy(second); savedSecond.AssignedVoiceId = "disk-B";
        DubbingVoiceAssignmentStore.SaveAtomic(media.Media, [savedFirst, savedSecond]);
        player.Config.Subtitles.PersistPerLineVoices = true;
        Assert.Null(first.AssignedVoiceId); Assert.Null(second.AssignedVoiceId);

        await controller.SetCueVoiceAsync(controller.GetCues(0)[0], 0, null);
        await controller.RestoreDubbingVoicesAsync(0);
        await controller.RestoreDubbingVoicesAsync(0);
        Assert.Null(first.AssignedVoiceId);
        Assert.Equal("disk-B", second.AssignedVoiceId);
        await controller.DrainVoiceAssignmentsAsync();
        SubtitleData[] restored = [Copy(first), Copy(second)];
        DubbingVoiceAssignmentStore.LoadMap(media.Media).Apply(media.Media, restored);
        Assert.Null(restored[0].AssignedVoiceId);
        Assert.Equal("disk-B", restored[1].AssignedVoiceId);
    }

    [AvaloniaFact]
    public async Task Transcript_capture_tracks_source_identity_and_uses_requested_slot_and_translation()
    {
        using AvStreamTestMedia media = await AvStreamTestMedia.CreateAsync();
        using TempDir state = new();
        await using Session session = new(state);
        await session.OpenAsync(media);
        var controller = session.Controller;
        var original = controller.CaptureInsightsTranscript(0, false);
        var secondary = controller.CaptureInsightsTranscript(1, false);
        Assert.Equal(new[] { "Primary original" }, original.CueTexts);
        Assert.Equal(new[] { "Secondary original" }, secondary.CueTexts);
        Assert.Equal(0, original.Slot); Assert.Equal(1, secondary.Slot);
        Assert.False(original.UseTranslated);

        // A metadata-only source replacement must invalidate a request even when every transcript string matches.
        session.Primary!.Url = media.Secondary;
        var replaced = controller.CaptureInsightsTranscript(0, false);
        Assert.Equal(original.CueTexts, replaced.CueTexts);
        Assert.NotEqual(original.MediaGeneration, replaced.MediaGeneration);
        session.Player.SubtitlesManager[1].Subs[0].TranslatedText = "translated secondary";
        var translated = controller.CaptureInsightsTranscript(1, true);
        Assert.True(translated.UseTranslated);
        Assert.Equal(new[] { "translated secondary" }, translated.CueTexts);
        Assert.Equal(new[] { "Secondary original" }, secondary.CueTexts);
    }

    static SubtitleData Copy(SubtitleData source) => new()
    { StartTime = source.StartTime, EndTime = source.EndTime, Text = source.Text };

    sealed class Session : IAsyncDisposable
    {
        public Player Player { get; }
        public FlyleafPlaybackController Controller { get; }
        public ExternalSubtitlesStream? Primary { get; private set; }
        public Session(TempDir state)
        {
            string? native = Environment.GetEnvironmentVariable("LLPLAYER_FFMPEG_DIR");
            Assert.SkipWhen(string.IsNullOrWhiteSpace(native), "Offline controller regression requires native FFmpeg fixture configuration.");
            AvaloniaUIDispatcher dispatcher = new();
            CollectionSyncBridge bridge = new(dispatcher);
            EngineBootstrap.InstallSeams(dispatcher, new AvaloniaHostServices(() => null), bridge, new KeyMapper());
            if (!Engine.IsLoaded) EngineBootstrap.Start(EngineBootstrap.CreateEngineConfig(native!, new(state.Path, state.Path)));
            Config config = EngineBootstrap.CreatePlayerConfig(new AppPrefs().Normalize());
            config.Player.AutoPlay = false;
            config.Subtitles.SearchLocal = false;
            config.Subtitles.LanguageAutoDetect = false;
            Player = new(config);
            Controller = new(Player, dispatcher, bridge);
        }
        public async Task OpenAsync(AvStreamTestMedia fixture)
        {
            Assert.Null(Player.Open(fixture.Media).Error);
            await Until(() => !Player.SubtitlesManager[0].IsLoading && Controller.GetCues(0).Count > 0);
            Primary = new() { Url = fixture.Primary, Title = "Primary", Language = Language.English, Downloaded = true };
            ExternalSubtitlesStream secondary = new() { Url = fixture.Secondary, Title = "Secondary", Language = Language.English, Downloaded = true };
            PlaylistItem.AddExternalStream(Primary, Player.Playlist.Selected, "LanguageWorkflowTests");
            PlaylistItem.AddExternalStream(secondary, Player.Playlist.Selected, "LanguageWorkflowTests");
            foreach (var (slot, source) in new[] { (0, Primary), (1, secondary) })
            {
                Controller.SelectSubtitleTrack(slot, Controller.GetSubtitleTracks().Single(track => ReferenceEquals(track.Source, source)));
                await Controller.Recognition.MutateAsync(() => { });
                await Until(() => !Player.SubtitlesManager[slot].IsLoading
                    && ReferenceEquals(Player.decoder.SubtitlesStreams[slot]?.ExternalStream, source)
                    && Controller.GetCues(slot).Count == 1);
            }
            Assert.Equal("Primary original", Assert.Single(Controller.GetCues(0)).Text);
            Assert.Equal("Secondary original", Assert.Single(Controller.GetCues(1)).Text);
            Assert.False(Controller.Recognition.IsBusy);
        }
        public async ValueTask DisposeAsync() { await Controller.DisposeAsync(); Player.Dispose(); }
    }
    static async Task Until(Func<bool> condition)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(15));
        while (!condition()) { Dispatcher.UIThread.RunJobs(); await Task.Delay(10, timeout.Token); }
    }
}
