using System.Collections.Concurrent;
using Avalonia.Headless.XUnit;
using FlyleafLib;
using FlyleafLib.MediaFramework.MediaPlaylist;
using FlyleafLib.MediaPlayer;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.ViewModels;

namespace LLPlayer.Avalonia.Tests;

[Collection(FlyleafGlobalsCollection.Name)]
public sealed class OnlinePlaylistNavigationTests
{
    [Fact]
    public async Task NavigationUsesActualCurrentItemAndReopenUsesTheSameCurrentToken()
    {
        PlaylistFake player = new();
        using PlaylistViewModel model = new(player);
        Assert.Equal("3 playable / 5 expected. Unavailable or unselected entries are omitted.", model.Summary);
        Assert.True(model.SelectedItem!.IsCurrent);
        await model.NextCommand.ExecuteAsync(null);
        Assert.Equal(2, Assert.Single(player.OpenedRows).Index);
        await model.PreviousCommand.ExecuteAsync(null);
        Assert.Equal(0, player.OpenedRows[1].Index);
        await model.ReopenCommand.ExecuteAsync(null);
        Assert.Equal(1, player.OpenedRows[2].Index);
        model.SelectedItem = model.Items[0];
        await model.OpenSelectedCommand.ExecuteAsync(null);
        Assert.Equal(0, player.OpenedRows[3].Index);
    }

    [AvaloniaFact]
    public async Task NativePlaylistSelectAndReopenPreserveThePlaylistAndRejectStaleRows()
    {
        using TempDir state = new();
        var (player, controller, dispatcher, media) = Create(state);
        try
        {
            Assert.Null(player.Open(media).Error);
            PlaylistItem first = player.Playlist.Selected;
            PlaylistItem second = new() { Title = "Second lesson", Url = media, DirectUrl = media };
            player.Playlist.AddItem(second, "fixture");
            player.Playlist.Title = "Lessons"; player.Playlist.ExpectingItems = 2;
            dispatcher.RunAll();
            var rows = controller.GetPlaylistItems();
            Assert.Equal(2, rows.Count);
            Assert.True(rows[0].IsCurrent);
            Assert.IsNotType<PlaylistItem>(rows[1].Token);
            await controller.SelectPlaylistItemAsync(rows[1]);
            dispatcher.RunAll();
            Assert.Same(second, player.Playlist.Selected);
            Assert.Equal(2, player.Playlist.Items.Count);
            var current = Assert.Single(controller.GetPlaylistItems(), row => row.IsCurrent);
            Assert.Equal("Second lesson", current.Title);
            long generation = player.MediaGeneration;
            await controller.SelectPlaylistItemAsync(rows[0]);
            Assert.Equal(generation, player.MediaGeneration);
            Assert.Same(second, player.Playlist.Selected);
            await controller.SelectPlaylistItemAsync(current);
            dispatcher.RunAll();
            Assert.Same(second, player.Playlist.Selected);
            Assert.True(player.MediaGeneration > generation);
            Assert.Equal(2, player.Playlist.Items.Count);
            Assert.Same(first, player.Playlist.Items[0]);
        }
        finally { await controller.DisposeAsync(); player.Dispose(); dispatcher.RunAll(); }
    }

    [AvaloniaFact]
    public async Task QueuedPlaylistRowCannotOpenAfterSameUrlNativeReplacement()
    {
        using TempDir state = new();
        var (player, controller, dispatcher, media) = Create(state);
        using ManualResetEventSlim entered = new(), release = new();
        Task? blocker = null;
        try
        {
            Assert.Null(player.Open(media).Error);
            player.Playlist.AddItem(new() { Title = "Removed by replacement", Url = media, DirectUrl = media }, "fixture");
            var row = controller.GetPlaylistItems()[1];
            blocker = controller.Recognition.MutateAsync(() => { entered.Set(); Assert.True(release.Wait(TimeSpan.FromSeconds(10))); });
            Assert.True(await Task.Run(() => entered.Wait(TimeSpan.FromSeconds(5))));
            Task selection = controller.SelectPlaylistItemAsync(row);
            Assert.False(selection.IsCompleted);
            Assert.Null(player.Open(media).Error);
            PlaylistItem replacement = player.Playlist.Selected;
            long generation = player.MediaGeneration;
            release.Set(); await blocker; await selection;
            dispatcher.RunAll();
            Assert.Same(replacement, player.Playlist.Selected);
            Assert.Equal(generation, player.MediaGeneration);
            Assert.Single(player.Playlist.Items);
            Assert.False(player.decoder.Interrupt);
        }
        finally
        {
            release.Set(); if (blocker != null) await blocker;
            await controller.DisposeAsync(); player.Dispose(); dispatcher.RunAll();
        }
    }

    [AvaloniaFact]
    public async Task FabricatedForeignAndRemovedRowsCannotOpenMedia()
    {
        using TempDir state = new();
        var (player, controller, dispatcher, media) = Create(state);
        var (foreign, foreignController, foreignDispatcher, _) = Create(state);
        try
        {
            Assert.Null(player.Open(media).Error); Assert.Null(foreign.Open(media).Error);
            PlaylistItem removed = new() { Title = "removed", Url = media, DirectUrl = media };
            player.Playlist.AddItem(removed, "fixture");
            var row = controller.GetPlaylistItems()[1];
            player.Playlist.Items.Remove(removed);
            long generation = player.MediaGeneration;
            await controller.SelectPlaylistItemAsync(row);
            await controller.SelectPlaylistItemAsync(foreignController.GetPlaylistItems()[0]);
            await controller.SelectPlaylistItemAsync(new(0, "fake", false, new object()));
            Assert.Equal(generation, player.MediaGeneration);
            Assert.False(controller.CanCancelOpen);
        }
        finally
        {
            await controller.DisposeAsync(); player.Dispose(); dispatcher.RunAll();
            await foreignController.DisposeAsync(); foreign.Dispose(); foreignDispatcher.RunAll();
        }
    }

    sealed class PlaylistFake : FakePlaybackController, IPlaybackController
    {
        public List<PlaybackPlaylistItem> OpenedRows { get; } = [];
        public string PlaylistTitle => "Lessons";
        public int PlaylistExpectedCount => 5;
        public IReadOnlyList<PlaybackPlaylistItem> GetPlaylistItems()
            => [new(0, "one", false, new object()), new(1, "two", true, new object()), new(2, "three", false, new object())];
        public Task SelectPlaylistItemAsync(PlaybackPlaylistItem item) { OpenedRows.Add(item); return Task.CompletedTask; }
    }
    static (Player Player, FlyleafPlaybackController Controller, QueuedDispatcher Dispatcher, string Media) Create(TempDir state)
    {
        string? ffmpeg = Environment.GetEnvironmentVariable("LLPLAYER_FFMPEG_DIR");
        string? media = Environment.GetEnvironmentVariable("LLPLAYER_TEST_MEDIA");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(ffmpeg), "Requires configured native FFmpeg shared libraries.");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(media), "Requires configured canonical single-stream media fixture.");
        Assert.True(File.Exists(media));
        QueuedDispatcher dispatcher = new(); CollectionSyncBridge bridge = new(dispatcher);
        EngineBootstrap.InstallSeams(dispatcher, new AvaloniaHostServices(() => null), bridge, new KeyMapper());
        if (!Engine.IsLoaded) EngineBootstrap.Start(EngineBootstrap.CreateEngineConfig(ffmpeg!, new(state.Path, state.Path)));
        Config config = EngineBootstrap.CreatePlayerConfig(new AppPrefs().Normalize());
        config.Player.AutoPlay = false; config.Subtitles.SearchLocal = false; config.Subtitles.LanguageAutoDetect = false;
        Player player = new(config);
        return (player, new FlyleafPlaybackController(player, dispatcher, bridge), dispatcher, media!);
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
