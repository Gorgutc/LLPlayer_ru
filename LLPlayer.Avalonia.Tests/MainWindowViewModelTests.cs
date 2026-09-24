using AwesomeAssertions;
using LLPlayer.Avalonia.Services;
using LLPlayer.Avalonia.ViewModels;
using WpfKey = System.Windows.Input.Key;

namespace LLPlayer.Avalonia.Tests;

public class MainWindowViewModelTests
{
    static long Ticks(double seconds) => TimeSpan.FromSeconds(seconds).Ticks;

    [Fact]
    public void Transport_FollowsPlayerTimeAndDuration()
    {
        var (vm, player, _, _, _) = TestVm.Create();
        player.IsOpened = true;
        player.Duration = Ticks(90);
        player.CurTime = Ticks(12.4);

        vm.SeekMaximum.Should().Be(90);
        vm.SeekValue.Should().BeApproximately(12.4, 1e-9);
        vm.CurrentTimeText.Should().Be("0:12");
        vm.DurationText.Should().Be("1:30");
        player.Seeks.Should().BeEmpty("updates coming from the player must not seek back (no feedback loop)");
    }

    [Fact]
    public void SeekDrag_IgnoresPlayerTime_AndSeeksOnceOnRelease()
    {
        ManualTimeProvider time = new();
        var (vm, player, _, _, _) = TestVm.Create(time);
        player.IsOpened = true;
        player.Duration = Ticks(60);
        player.CurTime = Ticks(5);

        vm.BeginSeekDrag();
        vm.SeekValue = 30;
        vm.SeekValue = 42;
        player.CurTime = Ticks(6);                 // playback keeps reporting while dragging

        vm.SeekValue.Should().Be(42, "the thumb stays where the user drags it");
        vm.CurrentTimeText.Should().Be("0:42", "the time label previews the drag position");
        player.Seeks.Should().BeEmpty("no seek until the thumb is released");

        vm.EndSeekDrag();
        player.Seeks.Should().Equal(TimeSpan.FromSeconds(42));

        // until the engine reaches the target, stale positions do not yank the thumb back ...
        player.CurTime = Ticks(6.1);
        vm.SeekValue.Should().Be(42);
        // ... and once it arrives the bar follows the player again
        player.CurTime = Ticks(42.2);
        vm.SeekValue.Should().BeApproximately(42.2, 1e-9);
        player.Seeks.Should().HaveCount(1);
    }

    [Fact]
    public void SeekHold_ExpiresSoAStuckEngineDoesNotFreezeTheBar()
    {
        ManualTimeProvider time = new();
        var (vm, player, _, _, _) = TestVm.Create(time);
        player.IsOpened = true;
        player.Duration = Ticks(60);
        vm.SeekValue = 50;                          // click on the track (not a drag): immediate seek
        player.Seeks.Should().Equal(TimeSpan.FromSeconds(50));

        time.Advance(TimeSpan.FromSeconds(5));
        player.CurTime = Ticks(3);
        vm.SeekValue.Should().Be(3);
    }

    [Fact]
    public void Volume_TwoWay_WithoutEcho()
    {
        var (vm, player, _, prefs, _) = TestVm.Create();
        vm.Volume = 40;
        player.Volume.Should().Be(40);
        prefs.Volume.Should().Be(40);

        player.Volume = 70;                          // engine side change (e.g. arrow keys)
        vm.Volume.Should().Be(70);

        vm.Volume = 1000;                            // clamped to VolumeMax
        player.Volume.Should().Be(150);

        vm.ToggleMute();
        player.Mute.Should().BeTrue();
        vm.IsMuted.Should().BeTrue();
    }

    [Fact]
    public void Speed_MenuSetsSpeed_AndMarksSelection()
    {
        var (vm, player, _, _, _) = TestVm.Create();
        vm.SpeedItems.Single(i => i.IsSelected).Speed.Should().Be(1);
        vm.SpeedItems.Single(i => i.Speed == 1.5).Command.Execute(null);
        player.Speed.Should().Be(1.5);
        vm.SpeedText.Should().Be("1.5×");
        vm.SpeedItems.Single(i => i.IsSelected).Speed.Should().Be(1.5);
    }

    [Fact]
    public void Shortcuts_AppChordsRunAppActions_OthersAreForwarded()
    {
        var (vm, player, shell, _, _) = TestVm.Create();
        player.PrimaryText = "Hello world";

        vm.HandleKeyDown(WpfKey.B, ctrl: true, alt: false, shift: false, textInputFocused: false).Should().BeTrue();
        vm.SidebarVisible.Should().BeTrue();

        vm.HandleKeyDown(WpfKey.F1, false, false, false, false).Should().BeTrue();
        vm.HandleKeyDown(WpfKey.OemQuestion, false, false, true, false).Should().BeTrue();   // "?"
        shell.CheatSheetShown.Should().Be(2);

        double size = vm.SubtitleFontSize;
        vm.HandleKeyDown(WpfKey.Right, false, false, true, false);
        vm.SubtitleFontSize.Should().Be(size + 2);
        vm.HandleKeyDown(WpfKey.Left, false, false, true, false);
        vm.SubtitleFontSize.Should().Be(size);

        vm.HandleKeyDown(WpfKey.C, true, false, false, false);
        shell.Clipboard.Should().Equal("Hello world");

        player.KeysDown.Should().BeEmpty("app shortcuts are not forwarded to the engine");

        vm.HandleKeyDown(WpfKey.Space, false, false, false, false).Should().BeTrue();
        vm.HandleKeyDown(WpfKey.Right, false, false, false, false).Should().BeTrue();   // engine: SeekForward2
        vm.HandleKeyDown(WpfKey.O, true, false, false, false).Should().BeTrue();        // engine: OpenFromFileDialog
        player.KeysDown.Should().Equal(WpfKey.Space, WpfKey.Right, WpfKey.O);

        vm.HandleKeyDown(WpfKey.Space, false, false, false, textInputFocused: true).Should().BeFalse();
        player.KeysDown.Should().HaveCount(3, "keys typed into a text box stay there");
    }

    [Fact]
    public void Drop_MediaWithSubtitles_OpensMediaThenLoadsSubtitles()
    {
        var (vm, player, _, prefs, saved) = TestVm.Create();
        vm.HandleDrop(["/videos/movie.mkv", "/videos/movie.en.srt"]);

        player.Opened.Should().Equal("/videos/movie.mkv");
        player.OpenedSubtitles.Should().BeEmpty("subtitles wait for the media to open");
        prefs.LastFolder.Should().Be("/videos");

        player.IsOpened = true;
        player.RaiseOpenCompleted("/videos/movie.mkv", false, null);
        player.OpenedSubtitles.Should().Equal("/videos/movie.en.srt");
        vm.RecentFiles.Should().Equal("/videos/movie.mkv");
        saved.Should().NotBeEmpty();
    }

    [Fact]
    public void Drop_SubtitlesOnly_LoadsIntoOpenVideo_OrExplains()
    {
        var (vm, player, _, _, _) = TestVm.Create();
        vm.HandleDrop(["/x/a.srt"]);
        player.OpenedSubtitles.Should().BeEmpty();
        vm.Toasts.Should().ContainSingle(t => t.Title == "Open a video first");

        player.IsOpened = true;
        vm.HandleDrop(["/x/a.srt", "/x/b.ass"]);
        player.OpenedSubtitles.Should().Equal("/x/a.srt", "/x/b.ass");
        player.Opened.Should().BeEmpty();
    }

    [Fact]
    public void OpenCommand_UsesPicker_AndOpenErrorsBecomeToasts()
    {
        var (vm, player, shell, _, _) = TestVm.Create();
        shell.NextPick = "/m/a.mp4";
        vm.OpenMediaCommand.Execute(null);
        player.Opened.Should().Equal("/m/a.mp4");

        player.RaiseOpenCompleted("/m/a.mp4", false, "unsupported codec");
        vm.Toasts.Should().Contain(t => t.Variant == ToastVariant.Destructive && t.Message == "unsupported codec");
        vm.RecentFiles.Should().BeEmpty();
    }

    [Fact]
    public async Task OpenCommand_WithoutFilePicker_ExplainsAlternatives()
    {
        var (vm, player, shell, _, _) = TestVm.Create();
        shell.PickError = new InvalidOperationException("no portal");
        await vm.OpenMediaCommand.ExecuteAsync(null);
        player.Opened.Should().BeEmpty();
        vm.Toasts.Should().ContainSingle(t => t.Title == "No file picker available" && t.Message!.Contains("LLPLAYER_MANAGED_DIALOGS"));
    }

    [Fact]
    public void OpenRecent_MissingFile_IsRemovedFromRecent()
    {
        AppPrefs prefs = new();
        prefs.AddRecent("/definitely/missing.mp4");
        var (vm, player, _, _, saved) = TestVm.Create(prefs: prefs.Normalize());
        vm.RecentFiles.Should().Equal("/definitely/missing.mp4");
        vm.HasRecentFiles.Should().BeTrue();

        vm.OpenRecentCommand.Execute("/definitely/missing.mp4");
        player.Opened.Should().BeEmpty();
        vm.RecentFiles.Should().BeEmpty();
        vm.HasRecentFiles.Should().BeFalse();
        prefs.RecentFiles.Should().BeEmpty();
        saved.Should().NotBeEmpty();

        vm.OpenRecentCommand.Execute("https://example.com/stream.m3u8");   // URLs are not checked on disk
        player.Opened.Should().Equal("https://example.com/stream.m3u8");
    }

    [Fact]
    public void Tracks_MenuListsOffPlusTracks_AndSelects()
    {
        var (vm, player, _, _, _) = TestVm.Create();
        SubtitleTrack embedded = new("#2 English", false, true, false, new object());
        SubtitleTrack file = new("movie.ru.srt (Russian)", true, false, true, new object());
        player.Tracks.AddRange([embedded, file]);

        vm.RefreshTracks();
        vm.PrimaryTracks.Select(t => t.Label).Should().Equal("Off", "Embedded #2 English", "File: movie.ru.srt (Russian)");
        vm.PrimaryTracks.Single(t => t.IsSelected).Label.Should().Be("Embedded #2 English");
        vm.SecondaryTracks.Single(t => t.IsSelected).Label.Should().Be("File: movie.ru.srt (Russian)");

        vm.SecondaryTracks[1].Command.Execute(null);
        vm.PrimaryTracks[0].Command.Execute(null);
        player.SelectedTracks.Should().Equal((1, embedded), (0, (SubtitleTrack?)null));
    }

    [Fact]
    public void FullScreenIdle_HidesControlsAndCursor_ActivityShowsThem()
    {
        ManualTimeProvider time = new();
        var (vm, player, _, _, _) = TestVm.Create(time);
        player.IsOpened = true;
        player.IsPlaying = true;

        vm.OnFullScreenChanged(true);
        time.Advance(TimeSpan.FromSeconds(1));
        vm.UpdateIdle();
        vm.ControlsVisible.Should().BeTrue();

        time.Advance(MainWindowViewModel.IdleTimeout);
        vm.UpdateIdle();
        vm.ControlsVisible.Should().BeFalse();
        vm.CursorHidden.Should().BeTrue();

        vm.NotifyActivity();
        vm.ControlsVisible.Should().BeTrue();
        vm.CursorHidden.Should().BeFalse();

        // paused or windowed: never hidden
        time.Advance(TimeSpan.FromSeconds(10));
        player.IsPlaying = false;
        vm.UpdateIdle();
        vm.ControlsVisible.Should().BeTrue();
    }

    [Fact]
    public void Theme_SwitchPersistsAndAppliesThroughTheShell()
    {
        var (vm, _, shell, prefs, saved) = TestVm.Create();
        vm.IsDarkTheme.Should().BeTrue();
        vm.ToggleTheme();
        vm.IsDarkTheme.Should().BeFalse();
        prefs.Theme.Should().Be("Light");
        shell.Themes.Should().Equal("Light");
        saved.Should().ContainSingle();
    }

    [Fact]
    public void Prefs_AreAppliedToThePlayerAtStart()
    {
        AppPrefs prefs = new() { Volume = 33, Mute = true, SubtitlesVisible = false, SubtitleFontSize = 40, SidebarVisible = true };
        var (vm, player, _, _, _) = TestVm.Create(prefs: prefs.Normalize());
        player.Volume.Should().Be(33);
        player.Mute.Should().BeTrue();
        player.SubtitlesVisible.Should().BeFalse();
        vm.SubtitleFontSize.Should().Be(40);
        vm.SecondaryFontSize.Should().Be(32);
        vm.SidebarVisible.Should().BeTrue();
    }

    [Fact]
    public async Task WordClick_PausesPlayback_AndResumingClosesThePopup()
    {
        var (vm, player, _, _, _) = TestVm.Create();
        player.IsOpened = true;
        player.IsPlaying = true;

        vm.OnWordClicked("fox", "The quick brown fox", 0, 10, 10);
        await Task.Yield();
        player.PauseCalls.Should().Be(1);
        vm.WordPopup.IsOpen.Should().BeTrue();
        vm.WordPopup.Context.Should().Be("The quick brown fox");

        player.Play();
        vm.WordPopup.IsOpen.Should().BeFalse("like the WPF popup, it closes when playback resumes");
    }
}
