using LLPlayer.Avalonia.ViewModels;

namespace LLPlayer.Avalonia.Tests;

public sealed class SubtitleOpenRequestRegressionTests
{
    [Theory]
    [InlineData("/media/a.mp4", "/media/b.mp4")]
    [InlineData("/media/repeated.mp4", "/media/repeated.mp4")]
    public void Superseded_media_completion_does_not_consume_latest_sidecars(string first, string second)
    {
        var (vm, player, _, _, _) = TestVm.Create();
        using (vm)
        {
            vm.Open(first, ["/subs/first.srt"]);
            vm.Open(second, ["/subs/second.srt"]);

            player.CompleteOpen(0);
            Assert.Empty(player.OpenedSubtitles);

            player.CompleteOpen(1);
            Assert.Equal(["/subs/second.srt"], player.OpenedSubtitles);
        }
    }

    [Theory]
    [InlineData("/media/native.mp4")]
    [InlineData("/media/app.mp4")]
    public void Native_media_replacement_invalidates_an_app_requests_pending_sidecars(string nativeMedia)
    {
        var (vm, player, _, _, _) = TestVm.Create();
        using (vm)
        {
            vm.Open("/media/app.mp4", ["/subs/app.srt"]);
            player.RaiseExternalMediaOpening();
            player.RaiseOpenCompleted(nativeMedia, false, null);
            player.CompleteOpen(0);
            Assert.Empty(player.OpenedSubtitles);
        }
    }

    [Fact]
    public void Subtitle_selected_during_native_shortcut_open_still_loads_on_completion()
    {
        var (vm, player, _, _, _) = TestVm.Create();
        using (vm)
        {
            player.IsOpening = true;
            vm.OpenSubtitlesFile("/subs/native.srt");
            Assert.Empty(player.OpenedSubtitles);

            player.IsOpening = false;
            player.IsOpened = true;
            player.RaiseOpenCompleted("/media/native.mp4", false, null);
            Assert.Equal(["/subs/native.srt"], player.OpenedSubtitles);
        }
    }

    [Fact]
    public void Failed_media_request_does_not_leave_its_sidecars_for_an_unrelated_open()
    {
        var (vm, player, _, _, _) = TestVm.Create();
        using (vm)
        {
            vm.Open("/media/failed.mp4", ["/subs/failed.srt"]);
            player.CompleteOpen(0, "Could not open media");
            player.RaiseOpenCompleted("/media/engine-shortcut.mp4", false, null);
            Assert.Empty(player.OpenedSubtitles);
        }
    }
}