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
    [AvaloniaTheory]
    [InlineData("cancel")]
    [InlineData("replace")]
    [InlineData("shutdown")]
    public async Task Cancel_during_native_video_open_interrupts_owned_decoder_and_never_autoplays_stale_media(string action)
    {
        using TempDir state = new();
        var (player, controller, media) = Create(state);
        player.Config.Player.AutoPlay = true;
        TaskCompletionSource<CancellationToken> entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<bool> replacement = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int opens = 0, staleCompletions = 0;
        bool nativeWasInterrupted = false;
        void Stall(object? sender, Player.OpeningVideoStreamArgs args)
        {
            if (Interlocked.Increment(ref opens) != 1) return;
            var token = FlyleafLib.Plugins.OnlineVideoRequest.CurrentCancellation;
            entered.TrySetResult(token);
            if (!token.WaitHandle.WaitOne(TimeSpan.FromSeconds(15))) return;
            nativeWasInterrupted = SpinWait.SpinUntil(() => player.decoder.Interrupt, TimeSpan.FromSeconds(10));
            // Return into the real native-open pipeline: cancellation must suppress its autoplay itself.
        }
        player.OpeningVideoStream += Stall;
        try
        {
            controller.Open(media, _ => Interlocked.Increment(ref staleCompletions));
            CancellationToken token = await entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
            Assert.True(token.CanBeCanceled);
            Assert.False(token.IsCancellationRequested);
            if (action == "replace")
            {
                player.Config.Player.AutoPlay = false;
                controller.Open(media, error => replacement.TrySetResult(error == null));
            }
            else if (action == "shutdown") await controller.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(20));
            else controller.CancelOpen();
            if (action == "replace") Assert.True(await replacement.Task.WaitAsync(TimeSpan.FromSeconds(30)));
            else if (action != "shutdown") await controller.Recognition.MutateAsync(() => { }).WaitAsync(TimeSpan.FromSeconds(20));
            Assert.True(nativeWasInterrupted);
            Assert.False(controller.IsPlaying);
            if (action != "replace")
            {
                Assert.False(controller.IsOpened);
                Assert.Null(player.decoder.VideoStream);
            }
            Assert.Equal(0, staleCompletions);
        }
        finally
        {
            player.OpeningVideoStream -= Stall;
            await controller.DisposeAsync();
            player.Dispose();
        }
    }

    [AvaloniaTheory]
    [InlineData("cancel")]
    [InlineData("replace")]
    [InlineData("shutdown")]
    public async Task App_open_scope_survives_Opening_and_releases_on_cancel_replacement_or_shutdown(string action)
    {
        using TempDir state = new();
        var (player, controller, media) = Create(state);
        TaskCompletionSource<CancellationToken> entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<bool> replacement = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int opens = 0, staleCompletions = 0;
        bool initialTokenWasCanceled = false;
        // Stall at the same synchronous engine boundary as the resolver without running a helper or network.
        void Stall(object? sender, OpeningArgs args)
        {
            if (args.IsSubtitles || Interlocked.Increment(ref opens) != 1) return;
            CancellationToken token = FlyleafLib.Plugins.OnlineVideoRequest.CurrentCancellation;
            initialTokenWasCanceled = token.IsCancellationRequested;
            entered.TrySetResult(token);
            if (!token.WaitHandle.WaitOne(TimeSpan.FromSeconds(15))) throw new TimeoutException("Host open cancellation did not reach the engine scope.");
            token.ThrowIfCancellationRequested();
        }
        player.Opening += Stall;
        try
        {
            controller.Open(media, _ => Interlocked.Increment(ref staleCompletions));
            CancellationToken token = await entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.True(token.CanBeCanceled);
            Assert.False(initialTokenWasCanceled); // Opening must not invalidate its own app-admitted token.
            var tools = new OnlineVideoSettingsStore(Path.Combine(state.Path, "tools.json"));
            var settingsDialog = new OnlineVideoViewModel(new(), draft => controller.SaveOnlineVideoSettingsAsync(tools, draft), _ => { });
            await settingsDialog.SaveCommand.ExecuteAsync(null);
            Assert.False(settingsDialog.Busy);
            Assert.Contains("Cancel the current opening", settingsDialog.Message);
            Assert.False(File.Exists(Path.Combine(state.Path, "tools.json")));
            if (action == "replace") controller.Open(media, error => replacement.TrySetResult(error == null));
            else if (action == "shutdown") await controller.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(15));
            else controller.CancelOpen();
            if (action == "replace") Assert.True(await replacement.Task.WaitAsync(TimeSpan.FromSeconds(30)));
            else if (action != "shutdown") await controller.Recognition.MutateAsync(() => { }).WaitAsync(TimeSpan.FromSeconds(15));
            Assert.True(token.IsCancellationRequested);
            Assert.Equal(0, staleCompletions);
        }
        finally
        {
            player.Opening -= Stall;
            await controller.DisposeAsync();
            player.Dispose();
        }
    }

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

    [AvaloniaFact]
    public async Task Stale_cancel_after_native_replacement_preserves_its_completion()
    {
        using TempDir state = new();
        QueuedDispatcher ui = new();
        var (player, controller, media) = Create(state, ui);
        using var vm = new MainWindowViewModel(controller, new AppPrefs().Normalize(), _ => { }, new FakeTranslator(), new FakeShell());
        List<string?> completions = [];
        controller.OpenCompleted += (url, subs, error) => { if (!subs && error == null) completions.Add(url); };
        try
        {
            Assert.True(FlyleafLib.Plugins.OnlineVideoRequest.TryCreate("https://example.org/video#part", out var request));
            TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            player.Opening += (_, args) =>
            {
                if (args.Url != request) return;
                var token = FlyleafLib.Plugins.OnlineVideoRequest.CurrentCancellation;
                entered.TrySetResult();
                if (!token.WaitHandle.WaitOne(TimeSpan.FromSeconds(10))) throw new TimeoutException();
                token.ThrowIfCancellationRequested();
            };
            vm.OpenOnlineVideo(request);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(vm.CancelOpenCommand.CanExecute(null));
            // The native shortcut has replaced the app request before its UI notifications arrive.
            player.OpenAsync(media);
            Assert.False(controller.CanCancelOpen);
            vm.CancelOpenCommand.Execute(null);
            await DrainUntil(ui, () => completions.Count != 0);
            Assert.Equal(media, Assert.Single(completions));
            Assert.Equal(media, Assert.Single(vm.RecentFiles));
        }
        finally { await controller.DisposeAsync(); player.Dispose(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Controller_only_admits_explicit_online_open(bool explicitAction)
    {
        using TempDir state = new();
        var (player, controller, _) = Create(state);
        using var vm = new MainWindowViewModel(controller, new AppPrefs().Normalize(), _ => { }, new FakeTranslator(), new FakeShell());
        Assert.True(FlyleafLib.Plugins.OnlineVideoRequest.TryCreate("https://example.org/video#part", out var request));
        TaskCompletionSource<bool> admission = new(TaskCreationOptions.RunContinuationsAsynchronously);
        player.Opening += (sender, args) =>
        {
            var token = FlyleafLib.Plugins.OnlineVideoRequest.CurrentCancellation;
            admission.TrySetResult(FlyleafLib.Plugins.OnlineVideoRequest.TryGetAdmittedUrl(args.Url, out _));
            if (!token.WaitHandle.WaitOne(TimeSpan.FromSeconds(10))) throw new TimeoutException();
            token.ThrowIfCancellationRequested();
        };
        try
        {
            if (explicitAction) vm.OpenOnlineVideo(request); else vm.Open(request);
            Assert.Equal(explicitAction, await admission.Task.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.True(controller.CancelOpen());
        }
        finally { await controller.DisposeAsync(); player.Dispose(); }
    }

    [AvaloniaFact]
    public async Task Cancel_can_revoke_a_queued_app_open_before_engine_dispatch()
    {
        using TempDir state = new();
        var (player, controller, media) = Create(state);
        using ManualResetEventSlim release = new();
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int openings = 0;
        player.Opening += (_, _) => Interlocked.Increment(ref openings);
        try
        {
            var previous = controller.Recognition.MutateAsync(() =>
            {
                entered.TrySetResult();
                if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
            });
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            controller.Open(media);
            Assert.True(controller.CanCancelOpen);
            Assert.True(controller.CancelOpen());
            release.Set();
            await previous;
            await WaitUntil(() => !controller.Recognition.IsDispatching);
            Assert.False(controller.CanCancelOpen);
            Assert.Equal(0, openings);
        }
        finally { release.Set(); await controller.DisposeAsync(); player.Dispose(); }
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
