using System.Diagnostics;
using FlyleafLib.MediaFramework.MediaRenderer;

namespace FlyleafLib.Platform;

/// <summary>
/// F-13 video tests: engine bootstrap and synthetic test clips. Clips are generated once per machine with the FFmpeg
/// CLI next to the shared libraries (<c>$LLPLAYER_FFMPEG_DIR/../bin/ffmpeg</c>, or <c>$LLPLAYER_FFMPEG_CLI</c>) into
/// <c>$TMPDIR/llplayer-video-tests/v1</c>. Tests skip (never fail) when FFmpeg or the CLI is not available.
/// </summary>
internal static class PortableVideoTestMedia
{
    const string FFmpegDirVar   = "LLPLAYER_FFMPEG_DIR";
    const string FFmpegCliVar   = "LLPLAYER_FFMPEG_CLI";

    static readonly object engineLock = new();
    static readonly object mediaLock  = new();

    /// <summary>Starts the (process-global) engine with the FFmpeg libraries of LLPLAYER_FFMPEG_DIR or skips.</summary>
    public static void RequireEngine()
    {
        string? ffmpegDir = Environment.GetEnvironmentVariable(FFmpegDirVar);
        Assert.SkipWhen(string.IsNullOrEmpty(ffmpegDir), $"{FFmpegDirVar} is not set (folder with the FFmpeg 8 shared libraries).");
        Assert.SkipUnless(Directory.Exists(ffmpegDir), $"{FFmpegDirVar} '{ffmpegDir}' does not exist.");

        lock (engineLock)
        {
            if (Engine.IsLoaded)
                return;

            // Engine.Start runs through UIInvokeIfRequired, which is a no-op while Utils.IsTesting is set (other test
            // classes set it process-wide). Clear it first, otherwise the engine silently stays unloaded and the
            // first `new Config()` fails on Engine.Plugins == null depending on test order.
            Utils.IsTesting = false;
            Engine.Start(new EngineConfig
            {
                FFmpegPath      = ffmpegDir,
                PluginsPath     = null,
                UIRefresh       = false,
                LogLevel        = LogLevel.Quiet,
                FFmpegLogLevel  = Flyleaf.FFmpeg.LogLevel.Quiet
            });

            if (!Engine.IsLoaded)
                throw new InvalidOperationException("Engine.Start returned without loading the engine.");
        }
    }

    static string RequireCli()
    {
        string? cli = Environment.GetEnvironmentVariable(FFmpegCliVar);
        if (string.IsNullOrEmpty(cli))
        {
            string? dir = Environment.GetEnvironmentVariable(FFmpegDirVar);
            if (!string.IsNullOrEmpty(dir))
                cli = Path.GetFullPath(Path.Combine(dir, "..", "bin", OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg"));
        }

        Assert.SkipWhen(string.IsNullOrEmpty(cli) || !File.Exists(cli), $"FFmpeg CLI not found (set {FFmpegCliVar} or keep bin/ffmpeg next to {FFmpegDirVar}).");
        return cli!;
    }

    static string CacheDir
    {
        get
        {
            string dir = Path.Combine(Path.GetTempPath(), "llplayer-video-tests", "v1");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    /// <summary>Runs the FFmpeg CLI (generation only; the libraries next to it are put on the loader path).</summary>
    static void RunCli(params string[] args)
    {
        string cli = RequireCli();
        ProcessStartInfo psi = new(cli)
        {
            RedirectStandardError   = true,
            RedirectStandardOutput  = true,
            UseShellExecute         = false,
        };
        psi.ArgumentList.Add("-hide_banner");
        psi.ArgumentList.Add("-loglevel");
        psi.ArgumentList.Add("error");
        psi.ArgumentList.Add("-y");
        foreach (var a in args)
            psi.ArgumentList.Add(a);

        string? libDir = Environment.GetEnvironmentVariable(FFmpegDirVar);
        if (!string.IsNullOrEmpty(libDir) && !OperatingSystem.IsWindows())
            psi.Environment["LD_LIBRARY_PATH"] = libDir;

        using var p = Process.Start(psi)!;
        var stderr = p.StandardError.ReadToEndAsync();
        _ = p.StandardOutput.ReadToEndAsync();
        if (!p.WaitForExit(TimeSpan.FromMinutes(3)))
        {
            try { p.Kill(true); } catch { }
            throw new TimeoutException("ffmpeg timed out: " + string.Join(' ', args));
        }

        if (p.ExitCode != 0)
            throw new InvalidOperationException($"ffmpeg failed ({p.ExitCode}): {stderr.Result}\nargs: {string.Join(' ', args)}");
    }

    static string Clip(string name, Action<string> generate)
    {
        lock (mediaLock)
        {
            string path = Path.Combine(CacheDir, name);
            if (File.Exists(path) && new FileInfo(path).Length > 0)
                return path;

            string tmp = Path.Combine(CacheDir, "tmp-" + Guid.NewGuid().ToString("N") + Path.GetExtension(name));
            try
            {
                generate(tmp);
                File.Move(tmp, path, overwrite: true);
            }
            finally
            {
                if (File.Exists(tmp))
                    File.Delete(tmp);
            }

            return path;
        }
    }

    const string QuadGraph =
        "color=c=red:s=320x180:r=25:d=2," +
        "drawbox=x=0:y=0:w=160:h=90:color=blue:t=fill," +
        "drawbox=x=160:y=90:w=160:h=90:color=lime:t=fill," +
        "drawbox=x=0:y=90:w=160:h=90:color=white:t=fill";

    /// <summary>320x180, 2 s: quadrants top-left blue, top-right red, bottom-left white, bottom-right lime.</summary>
    public static string Quad => Clip("quad.mp4", o => RunCli("-f", "lavfi", "-i", QuadGraph, "-c:v", "libx264", "-qp", "0", "-pix_fmt", "yuv444p", o));

    /// <summary><see cref="Quad"/> with a display matrix of 90 degrees counter-clockwise (FFmpeg -display_rotation 90).</summary>
    public static string QuadRotated => Clip("quad-rot90.mp4", o => RunCli("-display_rotation:v:0", "90", "-i", Quad, "-c", "copy", o));

    /// <summary>320x180 solid colour (e.g. 0x808080), 2 s, lossless 4:2:0.</summary>
    public static string Solid(string rgbHex) => Clip($"solid-{rgbHex}.mp4",
        o => RunCli("-f", "lavfi", "-i", $"color=c=0x{rgbHex}:s=320x180:r=25:d=2", "-c:v", "libx264", "-qp", "0", "-pix_fmt", "yuv420p", o));

    /// <summary>320x240 25i (top field first, flagged interlaced), scrolling testsrc2 fields: combing on every frame.</summary>
    public static string Interlaced => Clip("interlaced-scroll.mp4",
        o => RunCli("-f", "lavfi", "-i", "testsrc2=s=320x240:r=50:d=2,scroll=h=0.02,tinterlace=mode=interleave_top,setfield=tff",
            "-c:v", "libx264", "-qp", "0", "-pix_fmt", "yuv420p", "-flags", "+ildct+ilme", "-x264-params", "interlaced=1:tff=1", o));

    /// <summary>MPEG-TS, 1 s of 640x360 then 1 s of 1280x720 (in-band resolution change).</summary>
    public static string ResolutionSwitch => Clip("switch.ts", o =>
    {
        string a = Path.Combine(CacheDir, "switch-a.ts"), b = Path.Combine(CacheDir, "switch-b.ts");
        RunCli("-f", "lavfi", "-i", "testsrc2=s=640x360:r=25:d=1", "-c:v", "libx264", "-g", "25", "-pix_fmt", "yuv420p", a);
        RunCli("-f", "lavfi", "-i", "testsrc2=s=1280x720:r=25:d=1", "-c:v", "libx264", "-g", "25", "-pix_fmt", "yuv420p", "-output_ts_offset", "1", b);
        RunCli("-i", $"concat:{a}|{b}", "-c", "copy", o);
    });

    /// <summary>320x180, 1 s red then 1 s blue, a key frame every 5 frames.</summary>
    public static string RedThenBlue => Clip("red-blue.mp4",
        o => RunCli("-f", "lavfi", "-i", "color=c=red:s=320x180:r=25:d=1[a];color=c=blue:s=320x180:r=25:d=1[b];[a][b]concat=n=2:v=1:a=0[out0]",
            "-c:v", "libx264", "-g", "5", "-pix_fmt", "yuv420p", o));

    /// <summary>320x180 HDR10 (PQ, BT.2020, 10-bit) solid grey at ~1000 nits, lossless HEVC.</summary>
    public static string HdrGrey => Clip("hdr-grey.mkv",
        o => RunCli("-f", "lavfi", "-i", "color=c=0xC0C0C0:s=320x180:r=25:d=1,format=yuv420p10le,setparams=color_primaries=bt2020:color_trc=smpte2084:colorspace=bt2020nc:range=tv",
            "-c:v", "libx265", "-x265-params", "log-level=error:lossless=1", o));

    /// <summary>1920x1080 30 fps testsrc2, 3 s (performance measurement).</summary>
    public static string FullHd => Clip("test-1080p.mp4",
        o => RunCli("-f", "lavfi", "-i", "testsrc2=s=1920x1080:r=30:d=3", "-c:v", "libx264", "-preset", "veryfast", "-pix_fmt", "yuv420p", o));

    /// <summary>1280x720 30 fps testsrc2, 3 s (performance measurement).</summary>
    public static string Hd => Clip("test-720p-short.mp4",
        o => RunCli("-f", "lavfi", "-i", "testsrc2=s=1280x720:r=30:d=3", "-c:v", "libx264", "-preset", "veryfast", "-pix_fmt", "yuv420p", o));
}

/// <summary>Captures the frames presented by the portable renderer (copies the last one).</summary>
internal sealed class CapturingSurface : IVideoSurface
{
    readonly object lk = new();
    byte[] last = [];
    readonly HashSet<(int, int)> sizes = [];

    public int Frames;
    public int Clears;
    public int Width, Height;

    public void PresentFrame(ReadOnlySpan<byte> bgra, int width, int height, int stride)
    {
        lock (lk)
        {
            int size = width * height * 4;
            if (last.Length != size)
                last = new byte[size];

            for (int y = 0; y < height; y++)
                bgra.Slice(y * stride, width * 4).CopyTo(last.AsSpan(y * width * 4));

            Width   = width;
            Height  = height;
            sizes.Add((width, height));
            Frames++;
        }
    }

    public void ClearFrame()
        => Interlocked.Increment(ref Clears);

    public bool SawSize(int w, int h) { lock (lk) return sizes.Contains((w, h)); }

    /// <summary>Copy of the last presented frame (tightly packed BGRA).</summary>
    public (byte[] Pixels, int Width, int Height, int Frames) Snapshot()
    {
        lock (lk)
            return ((byte[])last.Clone(), Width, Height, Frames);
    }

    public static (int R, int G, int B) Pixel(byte[] px, int width, int x, int y)
    {
        int i = (y * width + x) * 4;
        return (px[i + 2], px[i + 1], px[i]);
    }

    /// <summary>Average of a small block around (x, y) (robust against chroma edges).</summary>
    public static (int R, int G, int B) Average(byte[] px, int width, int height, int x, int y, int radius = 3)
    {
        int r = 0, g = 0, b = 0, n = 0;
        for (int yy = Math.Max(0, y - radius); yy <= Math.Min(height - 1, y + radius); yy++)
            for (int xx = Math.Max(0, x - radius); xx <= Math.Min(width - 1, x + radius); xx++)
            {
                var p = Pixel(px, width, xx, yy);
                r += p.R; g += p.G; b += p.B; n++;
            }

        return (r / n, g / n, b / n);
    }
}
