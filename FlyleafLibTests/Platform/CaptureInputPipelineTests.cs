using FlyleafLib.MediaFramework.MediaDevice;
using FlyleafLib.MediaPlayer;

namespace FlyleafLib.Platform;

/// <summary>Real Player -> plugin -> Demuxer -> FFmpeg path, with a local raw fixture; no physical capture device.</summary>
[Collection("PortableEngine")]
public class CaptureInputPipelineTests
{
    [Fact]
    public async Task Escaped_capture_input_reaches_real_demuxer_without_extension_or_option_confusion()
    {
        PortableVideoTestMedia.RequireEngine();
        string name = OperatingSystem.IsWindows() ? "Камера & =100%.srt" : "Камера & ?=100%:α.srt";
        string path = Path.Combine(Path.GetTempPath(), "llplayer-capture-" + Guid.NewGuid().ToString("N") + "-" + name);
        try
        {
            File.WriteAllBytes(path, Enumerable.Repeat((byte)128, 4 * 4 * 2 * 10).ToArray());
            var device = new CaptureDeviceInfo("raw-fixture", "Fixture", "rawvideo", path, CaptureDeviceKind.Video);
            var format = new CaptureVideoFormatInfo("raw-format", device.Id, 4, 4, 30000, 1001, "yuyv422", null);
            string url = CaptureInputUrl.Build(device, format);
            Config config = new();
            config.Player.AutoPlay = false;
            using Player player = new(config);
            var opened = player.Open(url, defaultAudio: false, defaultSubtitles: false);
            Assert.True(opened.Success, opened.Error);
            // Paused Open configures the stream; public visible dimensions require the first decoded frame.
            TaskCompletionSource<int> seekCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);
            EventHandler<int> onSeekCompleted = (_, position) => seekCompleted.TrySetResult(position);
            player.SeekCompleted += onSeekCompleted;
            try
            {
                player.Seek(0);
                Assert.Equal(0, await seekCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            }
            finally { player.SeekCompleted -= onSeekCompleted; }
            Assert.Equal(4, player.Video.Width);
            Assert.Equal(4, player.Video.Height);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
