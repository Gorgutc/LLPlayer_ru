using System.Collections.Concurrent;
using System.ComponentModel;
using Avalonia.Headless.XUnit;
using FlyleafLib;
using FlyleafLib.MediaFramework.MediaFrame;
using FlyleafLib.MediaPlayer;
using LLPlayer.Avalonia.Services;

namespace LLPlayer.Avalonia.Tests;

[Collection(FlyleafGlobalsCollection.Name)]
public sealed class SidebarPlaybackAdapterTests
{
    [AvaloniaTheory]
    [InlineData(0, 1_000)]
    [InlineData(1, -750)]
    public async Task Valid_cue_seek_uses_its_slot_delay_and_rounds_fractional_raw_start_up(int slot, int delayMs)
    {
        using TempDir state = new();
        var (player, controller, ui, media) = Create(state);
        try
        {
            Assert.Null(player.Open(media).Error);
            await LoadBoth(player, controller, ui, state);
            player.SubtitlesManager[slot].Subs[0].StartTime += TimeSpan.FromTicks(5_000);
            player.Config.Subtitles[slot].Delay = TimeSpan.FromMilliseconds(delayMs).Ticks;
            CueInfo cue = Assert.Single(controller.GetCues(slot));
            long requestedTime = -1;
            void Capture(object? sender, PropertyChangedEventArgs e)
            {
                if (e.PropertyName == nameof(Player.CurTime))
                    Interlocked.CompareExchange(ref requestedTime, player.CurTime, -1);
            }
            player.PropertyChanged += Capture;
            try
            {
                controller.SeekToCue(cue, slot);
                long expectedMs = (long)Math.Ceiling(cue.Start.TotalMilliseconds) + delayMs;
                Assert.Equal(TimeSpan.FromMilliseconds(expectedMs).Ticks, requestedTime);
            }
            finally { player.PropertyChanged -= Capture; }
        }
        finally { await controller.DisposeAsync(); player.Dispose(); ui.RunAll(); }
    }

    [AvaloniaTheory]
    [InlineData(0, 1_700)]
    [InlineData(1, -1_200)]
    public async Task Sync_uses_raw_fractional_start_and_changes_only_the_selected_slot(int slot, int existingDelayMs)
    {
        using TempDir state = new();
        var (player, controller, ui, media) = Create(state);
        try
        {
            Assert.Null(player.Open(media).Error);
            await LoadBoth(player, controller, ui, state);
            player.SubtitlesManager[slot].Subs[0].StartTime += TimeSpan.FromTicks(5_000);
            player.Config.Subtitles[slot].Delay = TimeSpan.FromMilliseconds(existingDelayMs).Ticks;
            int other = 1 - slot;
            player.Config.Subtitles[other].Delay = TimeSpan.FromMilliseconds(375).Ticks;
            controller.SeekTo(TimeSpan.FromSeconds(4));
            CueInfo cue = Assert.Single(controller.GetCues(slot));
            long playhead = player.CurTime;
            bool playing = controller.IsPlaying;
            double speed = controller.Speed;
            var source = player.decoder.SubtitlesStreams[slot];

            await controller.SynchronizeSubtitleToCurrentTimeAsync(cue, slot);
            ui.RunAll();

            Assert.Equal(playhead - cue.Start.Ticks, player.Config.Subtitles[slot].Delay);
            Assert.Equal(TimeSpan.FromMilliseconds(375).Ticks, player.Config.Subtitles[other].Delay);
            Assert.Equal(playhead, player.CurTime);
            Assert.Equal(playing, controller.IsPlaying);
            Assert.Equal(speed, controller.Speed);
            Assert.Same(source, player.decoder.SubtitlesStreams[slot]);
            Assert.True(controller.IsCueCurrent(cue, slot));
            Assert.False(controller.IsCueCurrent(cue, other));
            Assert.False(controller.IsCueCurrent(cue, -1));
            Assert.False(controller.IsCueCurrent(cue, 2));
        }
        finally { await controller.DisposeAsync(); player.Dispose(); ui.RunAll(); }
    }

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task Valid_sync_preserves_cached_translation_and_display_choice_in_both_slots(int slot)
    {
        using TempDir state = new();
        var (player, controller, ui, media) = Create(state);
        try
        {
            Assert.Null(player.Open(media).Error);
            await LoadBoth(player, controller, ui, state);
            controller.SeekTo(TimeSpan.FromSeconds(4));
            SubtitleData primary = player.SubtitlesManager[0].Subs[0];
            SubtitleData secondary = player.SubtitlesManager[1].Subs[0];
            SeedTranslation(primary, "Primary translation");
            SeedTranslation(secondary, "Secondary translation");
            CueInfo cue = Assert.Single(controller.GetCues(slot));

            await controller.SynchronizeSubtitleToCurrentTimeAsync(cue, slot);

            AssertTranslation(primary, "Primary translation");
            AssertTranslation(secondary, "Secondary translation");
        }
        finally { await controller.DisposeAsync(); player.Dispose(); ui.RunAll(); }
    }

    [AvaloniaFact]
    public async Task Pending_paused_seek_clears_synthetic_bitmap_without_any_cue_sync()
    {
        using TempDir state = new();
        var (player, controller, ui, media) = Create(state);
        try
        {
            Assert.Null(player.Open(media).Error);
            await LoadBoth(player, controller, ui, state);
            SubtitleData bitmapCue = player.SubtitlesManager[1].Subs[0];
            bitmapCue.Text = null;
            bitmapCue.IsBitmap = true;
            Assert.Null(bitmapCue.Bitmap); // The synthetic displayed pixels have no engine cache owner.
            ui.RunAll();
            player.SubtitleDisplay(new SubtitlesFrameBitmap
            {
                data = [0, 0, 255, 255], width = 1, height = 1, x = 0, y = 0,
            }, 1);
            ui.RunAll();
            Assert.NotNull(controller.SecondaryImage);
            ConcurrentQueue<string> order = new();
            TaskCompletionSource<int> completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
            void SeekCompleted(object? sender, int milliseconds)
            {
                order.Enqueue("native seek completed");
                completed.TrySetResult(milliseconds);
            }
            void OverlayChanged(object? sender, PropertyChangedEventArgs e)
            {
                if (e.PropertyName == "Bitmap" && player.Subtitles[1].Data.Bitmap == null)
                    order.Enqueue("queued bitmap clear applied");
            }
            player.SeekCompleted += SeekCompleted;
            player.Subtitles[1].Data.PropertyChanged += OverlayChanged;
            try
            {
                controller.SeekTo(TimeSpan.FromSeconds(4));
                Assert.Equal(4000, await completed.Task.WaitAsync(TimeSpan.FromSeconds(15)));
                Assert.NotNull(controller.SecondaryImage); // Native completion does not drain posted UI writes.
                ui.RunAll();
                Assert.Null(controller.SecondaryImage);
                Assert.Equal(new[] { "native seek completed", "queued bitmap clear applied" }, order.ToArray());
            }
            finally
            {
                player.SeekCompleted -= SeekCompleted;
                player.Subtitles[1].Data.PropertyChanged -= OverlayChanged;
            }
        }
        finally { await controller.DisposeAsync(); player.Dispose(); ui.RunAll(); }
    }

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task Sync_preserves_opposite_bitmap_when_subtitle_helper_points_at_the_other_slot(int slot)
    {
        using TempDir state = new();
        var (player, controller, ui, media) = Create(state);
        try
        {
            Assert.Null(player.Open(media).Error);
            await LoadBoth(player, controller, ui, state);
            // Seek owns queued overlay writes too. Settle that native operation before planting synthetic
            // pixels; the separate pending-seek test proves its late clear is independent of cue sync.
            TaskCompletionSource<int> completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
            void SeekCompleted(object? sender, int milliseconds) => completed.TrySetResult(milliseconds);
            player.SeekCompleted += SeekCompleted;
            try
            {
                controller.SeekTo(TimeSpan.FromSeconds(4));
                Assert.Equal(4000, await completed.Task.WaitAsync(TimeSpan.FromSeconds(15)));
            }
            finally { player.SeekCompleted -= SeekCompleted; }
            ui.RunAll();
            int other = 1 - slot;
            SubtitleData bitmapCue = player.SubtitlesManager[other].Subs[0];
            Assert.Same(bitmapCue, player.SubtitlesManager[other].GetCurrent());
            bitmapCue.Text = null;
            bitmapCue.IsBitmap = true;
            ui.RunAll();
            player.SubtitleDisplay(new SubtitlesFrameBitmap
            {
                data = [0, 0, 255, 255], width = 1, height = 1, x = 0, y = 0,
            }, other);
            ui.RunAll();
            SubtitleImage image = Assert.IsType<SubtitleImage>(other == 0 ? controller.PrimaryImage : controller.SecondaryImage);
            SubtitlesSelectedHelper.CurIndex = other;
            CueInfo cue = Assert.Single(controller.GetCues(slot));

            await controller.SynchronizeSubtitleToCurrentTimeAsync(cue, slot);
            ui.RunAll();

            Assert.Same(image.Bitmap, Assert.IsType<SubtitleImage>(other == 0 ? controller.PrimaryImage : controller.SecondaryImage).Bitmap);
            Assert.Equal(other, SubtitlesSelectedHelper.CurIndex);
            Assert.Equal(0, player.Config.Subtitles[other].Delay);
        }
        finally { await controller.DisposeAsync(); player.Dispose(); ui.RunAll(); }
    }

    [AvaloniaTheory]
    [InlineData("canceled")]
    [InlineData("replaced-cue")]
    public async Task Rejected_queued_sync_preserves_translations_in_both_live_slots(string rejection)
    {
        using TempDir state = new();
        var (player, controller, ui, media) = Create(state);
        using ManualResetEventSlim release = new();
        using CancellationTokenSource selection = new();
        try
        {
            Assert.Null(player.Open(media).Error);
            await LoadBoth(player, controller, ui, state);
            controller.SeekTo(TimeSpan.FromSeconds(4));
            SubtitleData primary = player.SubtitlesManager[0].Subs[0];
            SubtitleData secondary = player.SubtitlesManager[1].Subs[0];
            SeedTranslation(primary, "Primary translation");
            SeedTranslation(secondary, "Secondary translation");
            CueInfo stale = Assert.Single(controller.GetCues(0));
            TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Task barrier = controller.Recognition.MutateAsync(() =>
            {
                entered.TrySetResult();
                Assert.True(release.Wait(TimeSpan.FromSeconds(15)));
            });
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Task sync = controller.SynchronizeSubtitleToCurrentTimeAsync(stale, 0, selection.Token);
            if (rejection == "canceled") selection.Cancel();
            else
            {
                primary = new() { Text = stale.OriginalText, StartTime = stale.Start, EndTime = stale.End };
                SeedTranslation(primary, "Primary translation");
                player.SubtitlesManager[0].Load([primary]);
            }
            release.Set();
            await barrier;
            await sync;

            AssertTranslation(primary, "Primary translation");
            AssertTranslation(secondary, "Secondary translation");
        }
        finally { release.Set(); await controller.DisposeAsync(); player.Dispose(); ui.RunAll(); }
    }

    static void SeedTranslation(SubtitleData cue, string text)
    {
        cue.EnabledTranslated = true;
        cue.TranslatedText = text;
    }

    static void AssertTranslation(SubtitleData cue, string text)
    {
        Assert.Equal(text, cue.TranslatedText);
        Assert.Equal(text, cue.DisplayText);
        Assert.True(cue.EnabledTranslated);
        Assert.True(cue.UseTranslated);
    }

    [AvaloniaFact]
    public async Task Both_slots_publish_current_indices_and_preserve_source_identity_on_append_and_timing_update()
    {
        using TempDir state = new();
        var (player, controller, ui, media) = Create(state);
        try
        {
            Assert.Null(player.Open(media).Error);
            await LoadBoth(player, controller, ui, state);
            CueInfo first = Assert.Single(controller.GetCues(1));
            Assert.NotNull(first.Identity);
            Assert.Equal(first.Identity, Assert.Single(controller.GetCues(1)).Identity);
            List<string?> changes = [];
            controller.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
            player.SubtitlesManager[0].SetCurrentTime(TimeSpan.FromSeconds(4));
            player.SubtitlesManager[1].SetCurrentTime(TimeSpan.FromSeconds(2));
            ui.RunAll();
            Assert.Equal(0, controller.PrimaryCueIndex);
            Assert.Equal(0, controller.SecondaryCueIndex);
            Assert.Contains(nameof(controller.PrimaryCueIndex), changes);
            Assert.Contains(nameof(controller.SecondaryCueIndex), changes);

            SubtitleData native = player.SubtitlesManager[1].Subs[0];
            native.StartTime += TimeSpan.FromTicks(5_000);
            player.SubtitlesManager[1].Add(new() { Text = "Appended", StartTime = TimeSpan.FromSeconds(7), EndTime = TimeSpan.FromSeconds(9) });
            CueInfo updated = controller.GetCues(1)[0];
            Assert.Equal(first.Identity, updated.Identity);
            Assert.False(controller.IsCueCurrent(first, 1));
            Assert.True(controller.IsCueCurrent(updated, 1));
        }
        finally { await controller.DisposeAsync(); player.Dispose(); ui.RunAll(); }
    }

    [AvaloniaTheory]
    [InlineData("track")]
    [InlineData("cue")]
    [InlineData("off")]
    [InlineData("same-url-media")]
    [InlineData("selection-canceled")]
    public async Task Queued_sync_rejects_source_replacement_before_execution(string replacement)
    {
        using TempDir state = new();
        var (player, controller, ui, media) = Create(state);
        using ManualResetEventSlim release = new();
        using CancellationTokenSource selection = new();
        try
        {
            Assert.Null(player.Open(media).Error);
            await LoadBoth(player, controller, ui, state);
            controller.SeekTo(TimeSpan.FromSeconds(4));
            CueInfo stale = Assert.Single(controller.GetCues(0));
            TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Task barrier = controller.Recognition.MutateAsync(() =>
            {
                entered.TrySetResult();
                Assert.True(release.Wait(TimeSpan.FromSeconds(15)));
            });
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Task sync = controller.SynchronizeSubtitleToCurrentTimeAsync(stale, 0, selection.Token);
            if (replacement == "same-url-media") Assert.Null(player.Open(media).Error);
            else if (replacement == "track") await Load(player, controller, ui, state, 0, "replacement", "1\n00:00:03,500 --> 00:00:06,000\nReplacement source\n\n");
            else if (replacement == "off") player.Commands.SubtitlesOffAction("0");
            else if (replacement == "selection-canceled") selection.Cancel();
            else player.SubtitlesManager[0].Load([new() { Text = stale.Text, StartTime = stale.Start, EndTime = stale.End }]);
            ui.RunAll();
            Assert.Equal(replacement == "selection-canceled", controller.IsCueCurrent(stale, 0));
            long[] delays = [player.Config.Subtitles[0].Delay, player.Config.Subtitles[1].Delay];
            long playhead = player.CurTime;
            release.Set();
            await barrier;
            await sync;
            if (replacement != "selection-canceled") controller.SeekToCue(stale, 0);
            Assert.Equal(delays, new[] { player.Config.Subtitles[0].Delay, player.Config.Subtitles[1].Delay });
            Assert.Equal(playhead, player.CurTime);
            if (replacement == "off") Assert.Empty(controller.GetCues(0));
        }
        finally { release.Set(); await controller.DisposeAsync(); player.Dispose(); ui.RunAll(); }
    }

    [AvaloniaFact]
    public async Task Foreign_and_fabricated_cues_cannot_change_delay_or_seek()
    {
        using TempDir state = new();
        var (player, controller, ui, media) = Create(state);
        FlyleafPlaybackController? other = null;
        try
        {
            Assert.Null(player.Open(media).Error);
            await LoadBoth(player, controller, ui, state);
            // A second adapter over the same Player isolates adapter ownership from media/item identity.
            CollectionSyncBridge otherBridge = new(ui);
            other = new(player, ui, otherBridge);
            CueInfo foreign = Assert.Single(other.GetCues(0));
            CueInfo fabricated = foreign with { Identity = new SubtitleCueIdentity(new object()) };
            long playhead = player.CurTime;
            foreach (CueInfo cue in new[] { foreign, fabricated, foreign with { Identity = null } })
            {
                Assert.False(controller.IsCueCurrent(cue, 0));
                controller.SeekToCue(cue, 0);
                await controller.SynchronizeSubtitleToCurrentTimeAsync(cue, 0);
            }
            Assert.Equal(0, player.Config.Subtitles[0].Delay);
            Assert.Equal(0, player.Config.Subtitles[1].Delay);
            Assert.Equal(playhead, player.CurTime);
        }
        finally { if (other != null) await other.DisposeAsync(); await controller.DisposeAsync(); player.Dispose(); ui.RunAll(); }
    }

    static async Task LoadBoth(Player player, FlyleafPlaybackController controller, QueuedDispatcher ui, TempDir state)
    {
        await Load(player, controller, ui, state, 0, "primary", "1\n00:00:03,500 --> 00:00:06,000\nPrimary cue\n\n");
        await Load(player, controller, ui, state, 1, "secondary", "1\n00:00:01,250 --> 00:00:05,500\nSecondary cue\n\n");
    }

    static async Task Load(Player player, FlyleafPlaybackController controller, QueuedDispatcher ui, TempDir state, int slot, string name, string text)
    {
        string path = Path.Combine(state.Path, name + ".srt");
        await File.WriteAllTextAsync(path, text);
        SubtitlesSelectedHelper.CurIndex = slot;
        Assert.Null(player.Open(path, forceSubtitles: true).Error);
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(15));
        while (true)
        {
            ui.RunAll();
            if (!player.SubtitlesManager[slot].IsLoading && controller.GetCues(slot).Count == 1) break;
            await Task.Delay(10, timeout.Token);
        }
    }

    static (Player Player, FlyleafPlaybackController Controller, QueuedDispatcher Dispatcher, string Media) Create(TempDir state)
    {
        string? ffmpeg = Environment.GetEnvironmentVariable("LLPLAYER_FFMPEG_DIR");
        string? media = Environment.GetEnvironmentVariable("LLPLAYER_TEST_MEDIA");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(ffmpeg), "Requires configured native FFmpeg shared libraries.");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(media), "Requires configured canonical video fixture.");
        Assert.True(File.Exists(media));
        QueuedDispatcher dispatcher = new();
        CollectionSyncBridge bridge = new(dispatcher);
        EngineBootstrap.InstallSeams(dispatcher, new AvaloniaHostServices(() => null), bridge, new KeyMapper());
        if (!Engine.IsLoaded) EngineBootstrap.Start(EngineBootstrap.CreateEngineConfig(ffmpeg!, new(state.Path, state.Path)));
        Config config = EngineBootstrap.CreatePlayerConfig(new AppPrefs().Normalize());
        config.Player.AutoPlay = false;
        config.Subtitles.SearchLocal = false;
        config.Subtitles.LanguageAutoDetect = false;
        Player player = new(config);
        return (player, new(player, dispatcher, bridge), dispatcher, media!);
    }

    sealed class QueuedDispatcher : IUIDispatcher
    {
        readonly ConcurrentQueue<Action> pending = new();
        public bool CheckAccess() => false;
        public void Post(Action action) => pending.Enqueue(action);
        public void Invoke(Action action) => action();
        public void RunAll() { while (pending.TryDequeue(out Action? action)) action(); }
    }
}
