using System.Collections.Concurrent;
using System.ComponentModel;
using Avalonia.Headless.XUnit;
using FlyleafLib;
using FlyleafLib.MediaPlayer;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.ViewModels;

namespace LLPlayer.Avalonia.Tests;

[Collection(FlyleafGlobalsCollection.Name)]
public sealed class SubtitlePlaybackRegressionTests
{
    [AvaloniaFact]
    public async Task Explicit_txt_subtitle_open_preserves_the_current_video()
    {
        using TempDir state = new();
        var (player, controller, media) = Create(state);
        try
        {
            var opened = await OpenAndWait(controller, media, () => controller.Open(media));
            Assert.Null(opened.Error);
            Assert.True(controller.IsOpened);
            string subtitles = Path.Combine(state.Path, "explicit-subtitles.txt");
            await File.WriteAllTextAsync(subtitles, "1\n00:00:03,500 --> 00:00:05,000\nExplicit text subtitle\n\n");

            var loaded = await OpenAndWait(controller, subtitles, () => controller.OpenSubtitles(subtitles));

            Assert.True(loaded.IsSubtitles, "An explicitly selected subtitle must use the subtitle open path even when its extension is .txt.");
            Assert.Null(loaded.Error);
            Assert.Equal(media, controller.Url);
            Assert.True(player.Video.IsOpened);
            await WaitForCue(controller);
            Assert.Equal("Explicit text subtitle", Assert.Single(controller.GetCues(0)).Text);
        }
        finally { await controller.DisposeAsync(); player.Dispose(); }
    }

    [AvaloniaFact]
    public async Task Sidebar_seek_applies_primary_delay_and_rounds_fractional_cue_up()
    {
        using TempDir state = new();
        var (player, controller, media) = Create(state);
        try
        {
            var opened = await OpenAndWait(controller, media, () => controller.Open(media));
            Assert.Null(opened.Error);
            Assert.True(controller.IsOpened);
            await LoadSubtitleCue(controller, state);
            player.Config.Subtitles[0].Delay = TimeSpan.FromSeconds(1).Ticks;
            Assert.Equal(TimeSpan.FromSeconds(1).Ticks, player.Config.Subtitles[0].Delay);
            long requestedTime = -1;
            void CaptureFirstSeek(object? sender, PropertyChangedEventArgs e)
            {
                if (e.PropertyName == nameof(Player.CurTime))
                    Interlocked.CompareExchange(ref requestedTime, player.CurTime, -1);
            }
            player.PropertyChanged += CaptureFirstSeek;
            try
            {
                SubtitlesSidebarViewModel sidebar = new(controller);
                CueItemViewModel cue = new(new CueInfo(0, TimeSpan.FromTicks(35_005_000), TimeSpan.FromSeconds(6), "Fractional cue"));
                sidebar.SeekCommand.Execute(cue);
                Assert.Equal(TimeSpan.FromMilliseconds(4501).Ticks, requestedTime);
            }
            finally { player.PropertyChanged -= CaptureFirstSeek; }
        }
        finally { await controller.DisposeAsync(); player.Dispose(); }
    }

    [AvaloniaFact]
    public async Task Generic_transport_seek_ignores_subtitle_delay_and_retains_millisecond_truncation()
    {
        using TempDir state = new();
        var (player, controller, media) = Create(state);
        try
        {
            var opened = await OpenAndWait(controller, media, () => controller.Open(media));
            Assert.Null(opened.Error);
            await LoadSubtitleCue(controller, state);
            player.Config.Subtitles[0].Delay = TimeSpan.FromSeconds(1).Ticks;
            Assert.Equal(TimeSpan.FromSeconds(1).Ticks, player.Config.Subtitles[0].Delay);
            long requestedTime = -1;
            void CaptureFirstSeek(object? sender, PropertyChangedEventArgs e)
            {
                if (e.PropertyName == nameof(Player.CurTime))
                    Interlocked.CompareExchange(ref requestedTime, player.CurTime, -1);
            }
            player.PropertyChanged += CaptureFirstSeek;
            try
            {
                controller.SeekTo(TimeSpan.FromTicks(35_005_000));
                Assert.Equal(TimeSpan.FromMilliseconds(3500).Ticks, requestedTime);
            }
            finally { player.PropertyChanged -= CaptureFirstSeek; }
        }
        finally { await controller.DisposeAsync(); player.Dispose(); }
    }

    [AvaloniaFact]
    public async Task Queued_native_opening_does_not_discard_newer_app_sidecars_for_the_same_url()
    {
        using TempDir state = new();
        QueuedDispatcher ui = new();
        var (player, controller, media) = Create(state, ui);
        MainWindowViewModel vm = new(controller, new AppPrefs().Normalize(), _ => { }, new FakeTranslator(), new FakeShell());
        try
        {
            string sidecar = Path.Combine(state.Path, "newer.srt");
            await File.WriteAllTextAsync(sidecar, "1\n00:00:03,500 --> 00:00:06,000\nNewer app sidecar\n\n");
            Assert.Null(player.Open(media).Error); // External notification is queued but deliberately not delivered.
            vm.Open(media, [sidecar]);
            await DrainUntil(ui, () => controller.GetCues(0).Count != 0);
            Assert.Equal("Newer app sidecar", Assert.Single(controller.GetCues(0)).Text);
        }
        finally { vm.Dispose(); await controller.DisposeAsync(); player.Dispose(); }
    }

    [AvaloniaFact]
    public async Task Native_opening_rejects_already_queued_app_sidecars_for_the_same_url()
    {
        using TempDir state = new();
        QueuedDispatcher ui = new();
        var (player, controller, media) = Create(state, ui);
        MainWindowViewModel vm = new(controller, new AppPrefs().Normalize(), _ => { }, new FakeTranslator(), new FakeShell());
        List<string?> subtitleCompletions = [];
        controller.OpenCompleted += (url, isSubtitles, _) => { if (isSubtitles) subtitleCompletions.Add(url); };
        try
        {
            string sidecar = Path.Combine(state.Path, "superseded.srt");
            await File.WriteAllTextAsync(sidecar, "1\n00:00:03,500 --> 00:00:06,000\nSuperseded app sidecar\n\n");
            vm.Open(media, [sidecar]);
            await WaitUntil(() => !controller.Recognition.IsDispatching); // App completion remains queued.
            Assert.Null(player.Open(media).Error);
            ui.RunAll();
            await DrainUntil(ui, () => !controller.Recognition.IsDispatching);
            Assert.Empty(subtitleCompletions);
            Assert.Empty(controller.GetCues(0));
        }
        finally { vm.Dispose(); await controller.DisposeAsync(); player.Dispose(); }
    }

    [AvaloniaFact]
    public async Task Reused_native_async_worker_reports_only_the_latest_same_url_request()
    {
        using TempDir state = new();
        QueuedDispatcher ui = new();
        string mediaPath = Environment.GetEnvironmentVariable("LLPLAYER_TEST_MEDIA") ?? "";
        int scheduled = 0;
        void QueueNext(object? sender, OpenCompletedArgs e)
        {
            if (!e.IsSubtitles && Interlocked.Increment(ref scheduled) < 3)
                ((Player)sender!).OpenAsync(mediaPath);
        }
        var (player, controller, media) = Create(state, ui, p => p.OpenCompleted += QueueNext);
        ConcurrentQueue<long> openingIds = new();
        ConcurrentQueue<long> completionIds = new();
        player.Opening += (_, e) => { if (!e.IsSubtitles) openingIds.Enqueue(e.RequestId); };
        TaskCompletionSource allCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int completed = 0;
        player.OpenCompleted += (_, e) =>
        {
            if (!e.IsSubtitles)
            {
                completionIds.Enqueue(e.RequestId);
                if (Interlocked.Increment(ref completed) == 3) allCompleted.TrySetResult();
            }
        };
        List<string?> mediaCompletions = [];
        controller.OpenCompleted += (url, isSubtitles, error) =>
        {
            if (!isSubtitles) { Assert.Null(error); mediaCompletions.Add(url); }
        };
        try
        {
            player.OpenAsync(media);
            await allCompleted.Task.WaitAsync(TimeSpan.FromSeconds(30));
            ui.RunAll();
            Assert.Equal(media, Assert.Single(mediaCompletions));
            Assert.Equal(3, openingIds.Distinct().Count());
            Assert.All(openingIds, id => Assert.True(id > 0));
            Assert.Equal(openingIds.ToArray(), completionIds.ToArray());
        }
        finally { await controller.DisposeAsync(); player.Dispose(); }
    }

    sealed class QueuedDispatcher : IUIDispatcher
    {
        readonly ConcurrentQueue<Action> pending = new();
        public bool CheckAccess() => false;
        public void Post(Action action) => pending.Enqueue(action);
        public void Invoke(Action action) => action();
        public void RunAll() { while (pending.TryDequeue(out Action? action)) action(); }
    }

    static async Task DrainUntil(QueuedDispatcher ui, Func<bool> condition)
    {
        await WaitUntil(() => { ui.RunAll(); return condition(); });
        ui.RunAll();
    }

    static async Task WaitUntil(Func<bool> condition)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        while (!condition()) await Task.Delay(10, timeout.Token);
    }

    static async Task LoadSubtitleCue(FlyleafPlaybackController controller, TempDir state)
    {
        string path = Path.Combine(state.Path, "seek-cue.srt");
        await File.WriteAllTextAsync(path, "1\n00:00:03,500 --> 00:00:06,000\nSeek cue\n\n");
        var opened = await OpenAndWait(controller, path, () => controller.OpenSubtitles(path));
        Assert.True(opened.IsSubtitles);
        Assert.Null(opened.Error);
        await WaitForCue(controller);
    }

    static async Task WaitForCue(FlyleafPlaybackController controller)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        while (controller.GetCues(0).Count == 0)
            await Task.Delay(10, timeout.Token);
    }

    static (Player Player, FlyleafPlaybackController Controller, string Media) Create(TempDir state, IUIDispatcher? dispatcherOverride = null, Action<Player>? beforeController = null)
    {
        string? ffmpeg = Environment.GetEnvironmentVariable("LLPLAYER_FFMPEG_DIR");
        string? media = Environment.GetEnvironmentVariable("LLPLAYER_TEST_MEDIA");
        Assert.SkipWhen(string.IsNullOrEmpty(ffmpeg), "Requires existing native FFmpeg shared libraries in LLPLAYER_FFMPEG_DIR.");
        Assert.SkipWhen(string.IsNullOrEmpty(media), "Requires existing video fixture in LLPLAYER_TEST_MEDIA.");
        Assert.True(File.Exists(media), $"Media fixture does not exist: {media}");
        IUIDispatcher dispatcher = dispatcherOverride ?? new AvaloniaUIDispatcher();
        CollectionSyncBridge bridge = new(dispatcher);
        EngineBootstrap.InstallSeams(dispatcher, new AvaloniaHostServices(() => null), bridge, new KeyMapper());
        if (!Engine.IsLoaded)
            EngineBootstrap.Start(EngineBootstrap.CreateEngineConfig(ffmpeg!, new AppPaths(Path.Combine(state.Path, "config"), Path.Combine(state.Path, "state"))));
        Config config = EngineBootstrap.CreatePlayerConfig(new AppPrefs().Normalize());
        config.Player.AutoPlay = false;
        config.Subtitles.SearchLocal = false;
        config.Subtitles.LanguageAutoDetect = false;
        Player player = new(config);
        beforeController?.Invoke(player);
        return (player, new FlyleafPlaybackController(player, dispatcher, bridge), media!);
    }

    static async Task<(bool IsSubtitles, string? Error)> OpenAndWait(FlyleafPlaybackController controller, string path, Action open)
    {
        TaskCompletionSource<(bool, string?)> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        void Completed(string? url, bool isSubtitles, string? error)
        {
            if (url == path) completion.TrySetResult((isSubtitles, error));
        }
        controller.OpenCompleted += Completed;
        try
        {
            open();
            return await completion.Task.WaitAsync(TimeSpan.FromSeconds(30));
        }
        finally { controller.OpenCompleted -= Completed; }
    }
}