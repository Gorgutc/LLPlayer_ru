using System.Diagnostics;
using AwesomeAssertions;
using FlyleafLib.MediaPlayer;

namespace FlyleafLib.Platform;

// F-13 (Linux port): unit tests of the portable replacements for Windows-only APIs (managed StrCmpLogicalW,
// NullAudioSink, OCR preprocessing). Compiled for the portable net10.0 TFM only.
public class PortableShimTests
{
    // === Utils.NativeMethods.StrCmpLogicalW (natural sort used by GetMoviesSorted) ================================

    [Fact]
    public void StrCmpLogicalW_OrdersDigitRunsNumerically()
    {
        Utils.NativeMethods.StrCmpLogicalW("ep2.mkv", "ep10.mkv").Should().BeNegative();
        Utils.NativeMethods.StrCmpLogicalW("ep10.mkv", "ep2.mkv").Should().BePositive();
        Utils.NativeMethods.StrCmpLogicalW("ep02.mkv", "ep2.mkv").Should().Be(0);
    }

    [Fact]
    public void StrCmpLogicalW_IgnoresCase()
    {
        Utils.NativeMethods.StrCmpLogicalW("Movie A", "movie a").Should().Be(0);
        Utils.NativeMethods.StrCmpLogicalW("a2", "A10").Should().BeNegative();
    }

    [Fact]
    public void GetMoviesSorted_UsesNaturalOrder()
    {
        List<string> sorted = Utils.GetMoviesSorted(["show 10.mkv", "show 2.mkv", "notes.txt", "show 1.mkv"]);

        sorted.Should().Equal("show 1.mkv", "show 2.mkv", "show 10.mkv");
    }

    // === NullAudioSink (headless audio output that keeps A/V sync) ==============================================

    static void Submit(IAudioSink sink, byte[] pcm)
    {
        nint buffer = System.Runtime.InteropServices.Marshal.AllocHGlobal(pcm.Length);
        try
        {
            System.Runtime.InteropServices.Marshal.Copy(pcm, 0, buffer, pcm.Length);
            sink.Submit(buffer, pcm.Length);
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.FreeHGlobal(buffer);
        }
    }

    [Fact]
    public void NullAudioSink_NeverReportsMoreThanSubmitted()
    {
        using NullAudioSink sink = new(48000, 2);
        byte[] pcm = new byte[480 * 4]; // 10 ms of s16 stereo

        Submit(sink, pcm);

        Thread.Sleep(100);

        sink.SamplesPlayed.Should().Be(480UL);
    }

    [Fact]
    public void NullAudioSink_ConsumesNoFasterThanRealTime()
    {
        using NullAudioSink sink = new(48000, 2);
        byte[] pcm = new byte[48000 * 4]; // 1 s

        Stopwatch sw = Stopwatch.StartNew();
        Submit(sink, pcm);

        Thread.Sleep(150);
        ulong played = sink.SamplesPlayed;
        double elapsed = sw.Elapsed.TotalSeconds;

        played.Should().BeGreaterThan(0UL);
        ((double)played).Should().BeLessThanOrEqualTo(elapsed * 48000 + 1);
    }

    [Fact]
    public void NullAudioSink_Flush_DropsQueuedAudioWithoutCountingItAsPlayed()
    {
        using NullAudioSink sink = new(48000, 2);
        byte[] pcm = new byte[48000 * 4]; // 1 s

        Submit(sink, pcm);

        Thread.Sleep(20);
        sink.Flush();
        ulong afterFlush = sink.SamplesPlayed;
        Thread.Sleep(50);

        afterFlush.Should().BeLessThan(48000UL);
        sink.SamplesPlayed.Should().Be(afterFlush, "an empty queue does not advance the clock");
    }

    [Fact]
    public void NullAudioBackend_CreatesSinkWithRequestedFormat()
    {
        using IAudioSink sink = NullAudioBackend.Instance.CreateSink(null, 44100, 2);

        sink.SampleRate.Should().Be(44100);
        sink.Channels.Should().Be(2);
        sink.LatencySamples.Should().Be(0);
        NullAudioBackend.Instance.EnumerateDevices().Should().BeEmpty();
    }

    // === OcrImageProcessor (managed replacement of the GDI+ ImageProcessor) =====================================

    [Fact]
    public void OcrImageProcessor_BlackText_CompositesOverWhite()
    {
        // 2 px: opaque black, fully transparent (any color)
        byte[] data = [0, 0, 0, 255,   10, 20, 30, 0];

        BgraBitmap result = OcrImageProcessor.BlackText(new BgraBitmap(data, 2, 1));

        result.Data.Should().Equal(0, 0, 0, 255,   255, 255, 255, 255);
    }

    [Fact]
    public void OcrImageProcessor_AddPadding_AddsWhiteBorder()
    {
        byte[] data = [1, 2, 3, 255];

        BgraBitmap result = OcrImageProcessor.AddPadding(new BgraBitmap(data, 1, 1), 2);

        result.Width.Should().Be(5);
        result.Height.Should().Be(5);
        int center = (2 * 5 + 2) * 4;
        result.Data[center..(center + 4)].Should().Equal(1, 2, 3, 255);
        result.Data[0..4].Should().Equal(255, 255, 255, 255);
    }
}
