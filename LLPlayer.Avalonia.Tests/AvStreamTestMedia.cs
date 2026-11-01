using System.Diagnostics;
using System.Text;

namespace LLPlayer.Avalonia.Tests;

/// <summary>Offline native fixtures, deliberately separate from LLPLAYER_TEST_MEDIA's single-stream gate fixture.</summary>
internal sealed class AvStreamTestMedia : IDisposable
{
    const string Version = "av-streams-v1";
    public string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), "LLPlayer-" + Version + "-" + Guid.NewGuid().ToString("N"));
    public string Media => Path.Combine(DirectoryPath, Version + ".mkv");
    public string ExternalVideo => Path.Combine(DirectoryPath, "external-green.mkv");
    public string ExternalAudio => Path.Combine(DirectoryPath, "external-32000.m4a");
    public string Primary => Path.Combine(DirectoryPath, "primary.srt");
    public string Secondary => Path.Combine(DirectoryPath, "secondary.srt");

    public static async Task<AvStreamTestMedia> CreateAsync()
    {
        string? native = Environment.GetEnvironmentVariable("LLPLAYER_FFMPEG_DIR");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(native), "Native A/V integration requires LLPLAYER_FFMPEG_DIR.");
        Assert.True(Directory.Exists(native), $"Configured native FFmpeg directory is missing: {native}");
        string? configured = Environment.GetEnvironmentVariable("LLPLAYER_FFMPEG_CLI");
        string cli = string.IsNullOrWhiteSpace(configured) && OperatingSystem.IsLinux() ? "ffmpeg" : configured ?? "";
        Assert.False(string.IsNullOrWhiteSpace(cli), "Configured Windows native gates must provide LLPLAYER_FFMPEG_CLI.");
        if (!string.IsNullOrWhiteSpace(configured))
            Assert.True(File.Exists(cli), $"Configured FFmpeg fixture CLI is missing: {cli}");
        AvStreamTestMedia fixture = new();
        Directory.CreateDirectory(fixture.DirectoryPath);
        try
        {
            await File.WriteAllTextAsync(fixture.Primary, "1\n00:00:00,000 --> 00:00:11,900\nPrimary original\n\n", new UTF8Encoding(false));
            await File.WriteAllTextAsync(fixture.Secondary, "1\n00:00:00,000 --> 00:00:11,900\nSecondary original\n\n", new UTF8Encoding(false));
            string chapters = Path.Combine(fixture.DirectoryPath, "chapters.ffmeta");
            await File.WriteAllTextAsync(chapters, ";FFMETADATA1\n[CHAPTER]\nTIMEBASE=1/1000\nSTART=0\nEND=4000\ntitle=Opening\n[CHAPTER]\nTIMEBASE=1/1000\nSTART=4000\nEND=8000\ntitle=Middle\n[CHAPTER]\nTIMEBASE=1/1000\nSTART=8000\nEND=12000\ntitle=Ending\n", new UTF8Encoding(false));
            await RunAsync(cli,
                "-f", "lavfi", "-i", "color=c=red:s=320x180:r=25:d=12",
                "-f", "lavfi", "-i", "color=c=blue:s=640x360:r=25:d=12",
                "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000:duration=12",
                "-f", "lavfi", "-i", "sine=frequency=880:sample_rate=44100:duration=12",
                "-i", fixture.Primary, "-i", fixture.Secondary, "-f", "ffmetadata", "-i", chapters,
                "-map", "0:v", "-map", "1:v", "-map", "2:a", "-map", "3:a", "-map", "4:s", "-map", "5:s", "-map_chapters", "6",
                "-c:v", "libx264", "-preset", "ultrafast", "-pix_fmt", "yuv420p", "-g", "25", "-c:a", "aac", "-ac:a:0", "1", "-ac:a:1", "2", "-c:s", "srt",
                "-metadata:s:v:0", "title=Red 320", "-metadata:s:v:1", "title=Blue 640",
                "-metadata:s:a:0", "language=eng", "-metadata:s:a:0", "title=Mono 48000", "-metadata:s:a:1", "language=jpn", "-metadata:s:a:1", "title=Stereo 44100",
                "-metadata:s:s:0", "language=eng", "-metadata:s:s:1", "language=jpn", "-t", "12", fixture.Media);
            await RunAsync(cli, "-f", "lavfi", "-i", "color=c=green:s=480x270:r=25:d=12", "-an", "-c:v", "libx264", "-preset", "ultrafast", "-pix_fmt", "yuv420p", "-g", "25", fixture.ExternalVideo);
            await RunAsync(cli, "-f", "lavfi", "-i", "sine=frequency=1200:sample_rate=32000:duration=12", "-vn", "-c:a", "aac", "-ac", "1", fixture.ExternalAudio);
            Assert.All(new[] { fixture.Media, fixture.ExternalVideo, fixture.ExternalAudio }, path => Assert.True(new FileInfo(path).Length > 0, path));
            return fixture;
        }
        catch { fixture.Dispose(); throw; }
    }

    static async Task RunAsync(string cli, params string[] arguments)
    {
        ProcessStartInfo start = new(cli) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        start.ArgumentList.Add("-hide_banner"); start.ArgumentList.Add("-nostdin"); start.ArgumentList.Add("-y");
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using Process process = Process.Start(start) ?? throw new IOException("FFmpeg fixture CLI did not start.");
        Task<string> errors = process.StandardError.ReadToEndAsync();
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken); }
        catch { if (!process.HasExited) process.Kill(entireProcessTree: true); throw; }
        string stderr = await errors; await output;
        Assert.True(process.ExitCode == 0, $"FFmpeg fixture generation failed ({process.ExitCode}): {stderr}");
    }

    // Same FFmpeg-owned palette graph as PortableOcrNativeTests: this verifies cache ownership, not PGS decoding.
    public static unsafe FlyleafLib.MediaPlayer.SubtitleBitmapData CreateNativeBitmap()
    {
        var rect = (Flyleaf.FFmpeg.AVSubtitleRect*)Flyleaf.FFmpeg.Raw.av_mallocz((nuint)sizeof(Flyleaf.FFmpeg.AVSubtitleRect));
        var rects = (Flyleaf.FFmpeg.AVSubtitleRect**)Flyleaf.FFmpeg.Raw.av_mallocz((nuint)sizeof(nint));
        rects[0] = rect;
        rect->w = 2; rect->h = 2; rect->nb_colors = 2; rect->linesize[0] = 2;
        rect->data[0] = (nint)Flyleaf.FFmpeg.Raw.av_malloc(4);
        rect->data[1] = (nint)Flyleaf.FFmpeg.Raw.av_mallocz(256 * sizeof(uint));
        ((uint*)rect->data[1])[1] = 0xffff00ff;
        for (int index = 0; index < 4; index++) ((byte*)rect->data[0])[index] = 1;
        return new(new Flyleaf.FFmpeg.AVSubtitle { num_rects = 1, rects = rects });
    }
    public void Dispose() { if (Directory.Exists(DirectoryPath)) Directory.Delete(DirectoryPath, recursive: true); }
}
