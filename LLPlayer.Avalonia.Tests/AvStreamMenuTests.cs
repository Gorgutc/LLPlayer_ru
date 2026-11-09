using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.ViewModels;
using LLPlayer.Avalonia.Views;

namespace LLPlayer.Avalonia.Tests;

public class AvStreamMenuTests
{
    [AvaloniaFact]
    public void MenusExposeAudioVideoAndChaptersWithinExistingMenuSurfaces()
    {
        using WindowFixture fixture = new();
        Menu menu = fixture.Window.FindControl<Menu>("MainMenu")!;
        Assert.Equal(new[] { "Words", "Translation", "Recognition", "_Media", "_Playback", "_Subtitles", "_View", "_Help", "App" },
            menu.Items.OfType<MenuItem>().Select(item => item.Header?.ToString()));
        MenuItem playback = Assert.Single(menu.Items.OfType<MenuItem>(), item => Equals(item.Header, "_Playback"));
        AssertChoiceHeaders(playback.Items.OfType<MenuItem>());
        ContextMenu context = Assert.IsType<ContextMenu>(fixture.Window.FindControl<Panel>("VideoHost")!.ContextMenu);
        AssertChoiceHeaders(context.Items.OfType<MenuItem>());
    }

    [AvaloniaFact]
    public void PlaybackChoicesPreserveMetadataOrderingAndCurrentFlagsWithoutOffEntries()
    {
        var (vm, player, _, _, _) = TestVm.Create();
        using (vm)
        {
            AddChoices(player);
            player.IsOpened = true;
            vm.RefreshPlaybackChoices();

            Assert.Equal(new[] { "Embedded #1 English AAC", "External: commentary.flac" }, vm.AudioTracks.Select(item => item.Label));
            Assert.Equal(new[] { true, false }, vm.AudioTracks.Select(item => item.IsSelected));
            Assert.Equal(new[] { "Embedded #0 H.264 1280×720", "External: alternative.mp4" }, vm.VideoTracks.Select(item => item.Label));
            Assert.Equal(new[] { false, true }, vm.VideoTracks.Select(item => item.IsSelected));
            Assert.Equal(new[] { "[0:00] Prologue", "[1:01:01] Finale" }, vm.Chapters.Select(item => item.Label));
        }
    }

    [AvaloniaFact]
    public async Task CommandsRouteExactSnapshotsAndKeepActualSelectionUntilNotification()
    {
        var (vm, player, _, _, _) = TestVm.Create();
        using (vm)
        {
            AddChoices(player);
            player.IsOpened = true;
            vm.RefreshPlaybackChoices();
            await Execute(vm.AudioTracks[1].Command);
            await Execute(vm.VideoTracks[0].Command);
            await Execute(vm.Chapters[1].Command);

            Assert.Same(player.AudioTracks[1], Assert.Single(player.SelectedAudioTracks));
            Assert.Same(player.VideoTracks[0], Assert.Single(player.SelectedVideoTracks));
            Assert.Same(player.Chapters[1], Assert.Single(player.SelectedChapters));
            Assert.Equal(new[] { true, false }, vm.AudioTracks.Select(item => item.IsSelected));
            Assert.Equal(new[] { false, true }, vm.VideoTracks.Select(item => item.IsSelected));
        }
    }

    [AvaloniaFact]
    public async Task NotificationRefreshesActualFlagsAndRevokesPreviousSnapshotCommands()
    {
        var (vm, player, _, _, _) = TestVm.Create();
        using (vm)
        {
            AddChoices(player);
            player.IsOpened = true;
            vm.RefreshPlaybackChoices();
            IRelayCommand oldAudio = vm.AudioTracks[0].Command;
            IRelayCommand oldVideo = vm.VideoTracks[0].Command;
            IRelayCommand oldChapter = vm.Chapters[0].Command;
            player.AudioTracks[0] = player.AudioTracks[0] with { IsSelected = false };
            player.AudioTracks[1] = player.AudioTracks[1] with { IsSelected = true };
            player.RaisePlaybackChoicesChanged();

            Assert.Equal(new[] { false, true }, vm.AudioTracks.Select(item => item.IsSelected));
            Assert.False(oldAudio.CanExecute(null));
            await Execute(oldAudio);
            await Execute(oldVideo);
            await Execute(oldChapter);
            Assert.Empty(player.SelectedAudioTracks);
            Assert.Empty(player.SelectedVideoTracks);
            Assert.Empty(player.SelectedChapters);
        }
    }

    [AvaloniaTheory]
    [InlineData("opening")]
    [InlineData("closed")]
    [InlineData("external")]
    [InlineData("app-open")]
    [InlineData("url")]
    [InlineData("disposed")]
    public async Task MediaLifecycleClearsChoicesAndRejectsRetainedCommands(string transition)
    {
        var (vm, player, _, _, _) = TestVm.Create();
        using (vm)
        {
            AddChoices(player);
            player.IsOpened = true;
            vm.RefreshPlaybackChoices();
            IRelayCommand audio = vm.AudioTracks[0].Command;
            IRelayCommand video = vm.VideoTracks[0].Command;
            IRelayCommand chapter = vm.Chapters[0].Command;
            switch (transition)
            {
                case "opening": player.IsOpening = true; break;
                case "closed": player.IsOpened = false; break;
                case "external": player.RaiseExternalMediaOpening(); break;
                case "app-open": vm.Open("/next.mkv"); break;
                case "url": player.Url = "/replacement.mkv"; break;
                case "disposed": vm.Dispose(); break;
            }

            Assert.Empty(vm.AudioTracks);
            Assert.Empty(vm.VideoTracks);
            Assert.Empty(vm.Chapters);
            await Execute(audio);
            await Execute(video);
            await Execute(chapter);
            Assert.Empty(player.SelectedAudioTracks);
            Assert.Empty(player.SelectedVideoTracks);
            Assert.Empty(player.SelectedChapters);
        }
    }

    [AvaloniaFact]
    public void PendingAppOpenKeepsChoicesEmptyUntilThatRequestCompletes()
    {
        var (vm, player, _, _, _) = TestVm.Create();
        using (vm)
        {
            AddChoices(player);
            player.IsOpened = true;
            vm.RefreshPlaybackChoices();
            vm.Open("/next.mkv");
            vm.RefreshPlaybackChoices();
            player.RaisePlaybackChoicesChanged();
            Assert.Empty(vm.AudioTracks);
            Assert.Empty(vm.VideoTracks);
            Assert.Empty(vm.Chapters);

            player.AudioTracks.Clear();
            player.AudioTracks.Add(new("Next media audio", true, new object()));
            player.CompleteOpen(0);
            Assert.Equal("Next media audio", Assert.Single(vm.AudioTracks).Label);
        }
    }
    [AvaloniaFact]
    public void EmptyClosedOpeningAndFailedSnapshotsLeaveNoChoices()
    {
        var (vm, player, _, _, _) = TestVm.Create();
        using (vm)
        {
            vm.RefreshPlaybackChoices();
            Assert.Equal(0, player.PlaybackChoicesReads);
            player.IsOpened = true;
            vm.RefreshPlaybackChoices();
            Assert.Empty(vm.AudioTracks);
            Assert.Empty(vm.VideoTracks);
            Assert.Empty(vm.Chapters);

            AddChoices(player);
            vm.RefreshPlaybackChoices();
            Assert.NotEmpty(vm.AudioTracks);
            player.PlaybackChoicesError = new InvalidOperationException("metadata unavailable");
            vm.RefreshPlaybackChoices();
            Assert.Empty(vm.AudioTracks);
            Assert.Empty(vm.VideoTracks);
            Assert.Empty(vm.Chapters);
            Assert.Contains(vm.Toasts, toast => toast.Variant == ToastVariant.Destructive && toast.Message == "metadata unavailable");
            player.PlaybackChoicesError = null;
            player.IsOpening = true;
            int reads = player.PlaybackChoicesReads;
            vm.RefreshPlaybackChoices();
            Assert.Equal(reads, player.PlaybackChoicesReads);
            Assert.Empty(vm.AudioTracks);
        }
    }

    [AvaloniaFact]
    public async Task SelectionFailureIsReportedWithoutChangingSelectedFlags()
    {
        var (vm, player, _, _, _) = TestVm.Create();
        using (vm)
        {
            AddChoices(player);
            player.IsOpened = true;
            vm.RefreshPlaybackChoices();
            player.PlaybackChoiceActionError = new InvalidOperationException("cannot select track");
            await Execute(vm.AudioTracks[1].Command);
            Assert.Contains(vm.Toasts, toast => toast.Variant == ToastVariant.Destructive && toast.Message == "cannot select track");
            Assert.Equal(new[] { true, false }, vm.AudioTracks.Select(item => item.IsSelected));
        }
    }

    [AvaloniaFact]
    public async Task SelectionFailureFromRevokedMediaDoesNotPublishIntoReplacementMedia()
    {
        var (vm, player, _, _, _) = TestVm.Create();
        using (vm)
        {
            AddChoices(player);
            player.IsOpened = true;
            vm.RefreshPlaybackChoices();
            TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            player.PlaybackChoiceAction = () => completion.Task;
            Task pending = Execute(vm.AudioTracks[1].Command);
            player.RaiseExternalMediaOpening();
            completion.SetException(new InvalidOperationException("retired media failure"));
            await pending;
            Assert.DoesNotContain(vm.Toasts, toast => toast.Message == "retired media failure");
        }
    }

    [AvaloniaFact]
    public void PlaybackRefreshesOnlyWhenItOpensAndGeneratedRowsUseActualStateBindings()
    {
        using WindowFixture fixture = new();
        AddChoices(fixture.Player);
        fixture.Player.IsOpened = true;
        MenuItem playback = fixture.Window.FindControl<MenuItem>("PlaybackMenu")!;
        playback.IsSubMenuOpen = true;
        Dispatcher.UIThread.RunJobs();
        TrackMenuItem first = fixture.Vm.AudioTracks[0];
        int reads = fixture.Player.PlaybackChoicesReads;
        MenuItem audio = Choice(playback.Items.OfType<MenuItem>(), "Audio track");
        audio.IsSubMenuOpen = true;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(reads, fixture.Player.PlaybackChoicesReads);
        Assert.Same(first, fixture.Vm.AudioTracks[0]);
        AssertTrackContainer(audio, 0, first);
        AssertTrackContainer(audio, 1, fixture.Vm.AudioTracks[1]);
        audio.IsSubMenuOpen = false;
        playback.IsSubMenuOpen = false;
        fixture.Player.AudioTracks[0] = fixture.Player.AudioTracks[0] with { Label = "Updated English" };
        playback.IsSubMenuOpen = true;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Updated English", fixture.Vm.AudioTracks[0].Label);
        Assert.NotSame(first, fixture.Vm.AudioTracks[0]);
    }

    [AvaloniaFact]
    public void ContextMenuRefreshesChoicesAndBindsRadioTracksAndPlainChapterCommands()
    {
        using WindowFixture fixture = new();
        AddChoices(fixture.Player);
        fixture.Player.IsOpened = true;
        fixture.Player.AudioTracks[0] = fixture.Player.AudioTracks[0] with { Label = "Fresh context audio" };
        Panel host = fixture.Window.FindControl<Panel>("VideoHost")!;
        ContextMenu context = Assert.IsType<ContextMenu>(host.ContextMenu);
        int reads = fixture.Player.PlaybackChoicesReads;
        host.RaiseEvent(new ContextRequestedEventArgs { RoutedEvent = Control.ContextRequestedEvent });
        Dispatcher.UIThread.RunJobs();
        Assert.True(context.IsOpen);
        Assert.True(fixture.Player.PlaybackChoicesReads > reads);
        try
        {
            Assert.Equal("Fresh context audio", fixture.Vm.AudioTracks[0].Label);
            MenuItem audio = Choice(context.Items.OfType<MenuItem>(), "Audio track");
            audio.IsSubMenuOpen = true;
            Dispatcher.UIThread.RunJobs();
            AssertTrackContainer(audio, 0, fixture.Vm.AudioTracks[0]);
            audio.IsSubMenuOpen = false;
            MenuItem video = Choice(context.Items.OfType<MenuItem>(), "Video track");
            video.IsSubMenuOpen = true;
            Dispatcher.UIThread.RunJobs();
            AssertTrackContainer(video, 1, fixture.Vm.VideoTracks[1]);
            video.IsSubMenuOpen = false;
            MenuItem chapters = Choice(context.Items.OfType<MenuItem>(), "Chapters");
            chapters.IsSubMenuOpen = true;
            Dispatcher.UIThread.RunJobs();
            MenuItem chapter = Assert.IsType<MenuItem>(chapters.ContainerFromIndex(1));
            Assert.Equal("[1:01:01] Finale", chapter.Header);
            Assert.Same(fixture.Vm.Chapters[1].Command, chapter.Command);
            Assert.Equal(MenuItemToggleType.None, chapter.ToggleType);
        }
        finally { context.Close(); }
    }

    static Task Execute(IRelayCommand command) => Assert.IsAssignableFrom<IAsyncRelayCommand>(command).ExecuteAsync(null);
    static MenuItem Choice(IEnumerable<MenuItem> items, string header) => Assert.Single(items, item => Equals(item.Header, header));

    static void AssertTrackContainer(MenuItem parent, int index, TrackMenuItem model)
    {
        MenuItem item = Assert.IsType<MenuItem>(parent.ContainerFromIndex(index));
        Assert.Equal(model.Label, item.Header);
        Assert.Same(model.Command, item.Command);
        Assert.Equal(MenuItemToggleType.Radio, item.ToggleType);
        Assert.Equal(model.IsSelected, item.IsChecked);
    }

    static void AddChoices(FakePlaybackController player)
    {
        player.AudioTracks.AddRange([
            new("Embedded #1 English AAC", true, new object()),
            new("External: commentary.flac", false, new object())]);
        player.VideoTracks.AddRange([
            new("Embedded #0 H.264 1280×720", false, new object()),
            new("External: alternative.mp4", true, new object())]);
        player.Chapters.AddRange([
            new("Prologue", TimeSpan.Zero, new object()),
            new("Finale", TimeSpan.FromSeconds(3661), new object())]);
    }
    static void AssertChoiceHeaders(IEnumerable<MenuItem> items)
    {
        string?[] headers = items.Select(item => item.Header?.ToString()).ToArray();
        Assert.Contains("Audio track", headers);
        Assert.Contains("Video track", headers);
        Assert.Contains("Chapters", headers);
    }

    sealed class WindowFixture : IDisposable
    {
        public FakePlaybackController Player { get; } = new();
        public MainWindow Window { get; } = new(new KeyMapper(), null) { Width = 1000, Height = 600 };
        public MainWindowViewModel Vm { get; }

        public WindowFixture()
        {
            Vm = new(Player, new AppPrefs().Normalize(), _ => { }, new FakeTranslator(), Window);
            Window.Attach(Vm, renderer: null);
            Window.Show();
            Dispatcher.UIThread.RunJobs();
        }

        public void Dispose() => Window.Close();
    }
}
