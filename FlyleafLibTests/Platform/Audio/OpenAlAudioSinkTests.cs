using System.Runtime.InteropServices;
using AwesomeAssertions;

namespace FlyleafLib.Platform.Audio;

// F-13 (Linux port): OpenAlAudioSink buffer accounting / clock / flush / underrun / volume / lifetime, verified against
// FakeOpenAl (no libopenal or sound device needed). Real-device behaviour: OpenAlWaveIntegrationTests.
public class OpenAlAudioSinkTests
{
    const int Rate = 48000;

    static OpenAlAudioSink CreateSink(FakeOpenAl al, string? device = null, int channels = 2)
        => new(al, device, Rate, channels);

    /// <summary>Submits <paramref name="samples"/> stereo s16 frames whose bytes follow a recognizable pattern.</summary>
    static byte[] Submit(IAudioSink sink, int samples, int channels = 2, byte seed = 1)
    {
        byte[] pcm = new byte[samples * channels * 2];
        for (int i = 0; i < pcm.Length; i++)
            pcm[i] = (byte)(seed + i);

        nint ptr = Marshal.AllocHGlobal(Math.Max(1, pcm.Length));
        try
        {
            Marshal.Copy(pcm, 0, ptr, pcm.Length);
            sink.Submit(ptr, pcm.Length);
            Marshal.Copy(new byte[pcm.Length], 0, ptr, pcm.Length); // the sink must have copied already
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }

        return pcm;
    }

    // === Creation =================================================================================================

    [Fact]
    public void Create_OpensDeviceAtStreamRate_AndConfiguresPlainStereoSource()
    {
        FakeOpenAl al = new();
        using var sink = CreateSink(al, "Fake Headphones");

        al.OpenedDeviceNames.Should().Equal("Fake Headphones");
        var ctx = al.Contexts.Should().ContainSingle().Subject;
        ctx.Attributes.Should().Equal(Al.ALC_FREQUENCY, Rate, 0);

        var src = al.SingleSource;
        src.Ints[Al.SOURCE_RELATIVE].Should().Be(Al.TRUE);
        src.Floats[Al.ROLLOFF_FACTOR].Should().Be(0);
        src.Ints[Al.DIRECT_CHANNELS_SOFT].Should().Be(Al.TRUE, "stereo must not be panned when AL_SOFT_direct_channels exists");
        src.Floats[Al.GAIN].Should().Be(1);
        ctx.ListenerGain.Should().Be(1);
        sink.SampleRate.Should().Be(Rate);
        sink.Channels.Should().Be(2);
        sink.SamplesPlayed.Should().Be(0);
    }

    [Fact]
    public void Create_WithoutDirectChannelsExtension_DoesNotSetIt()
    {
        FakeOpenAl al = new();
        al.AlExtensions.Remove("AL_SOFT_direct_channels");
        using var sink = CreateSink(al);

        al.SingleSource.Ints.Should().NotContainKey(Al.DIRECT_CHANNELS_SOFT);
    }

    [Fact]
    public void Create_Throws_WhenDeviceCannotBeOpened()
    {
        FakeOpenAl al = new() { FailOpenDevice = true };

        var act = () => CreateSink(al, "Missing");

        act.Should().Throw<InvalidOperationException>().WithMessage("*Missing*");
        al.Contexts.Should().BeEmpty();
    }

    [Fact]
    public void Create_ClosesDevice_WhenContextCannotBeCreated()
    {
        FakeOpenAl al = new() { FailCreateContext = true };

        var act = () => CreateSink(al);

        act.Should().Throw<InvalidOperationException>().WithMessage("*context*");
        al.Devices.Should().ContainSingle().Which.Closed.Should().BeTrue();
    }

    [Fact]
    public void Create_ReleasesEverything_WhenSourceSetupFails()
    {
        FakeOpenAl al = new() { FailSetContext = true };

        var act = () => CreateSink(al);

        act.Should().Throw<InvalidOperationException>();
        al.Contexts.Should().ContainSingle().Which.Destroyed.Should().BeTrue();
        al.Devices.Should().ContainSingle().Which.Closed.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void Create_RejectsUnsupportedChannelCounts(int channels)
    {
        FakeOpenAl al = new();

        var act = () => CreateSink(al, channels: channels);

        act.Should().Throw<ArgumentOutOfRangeException>();
        al.OpenDeviceCalls.Should().Be(0);
    }

    // === Submit / clock ===========================================================================================

    [Fact]
    public void Submit_CopiesPcmIntoAQueuedBuffer_AndStartsPlayback()
    {
        FakeOpenAl al = new();
        using var sink = CreateSink(al);

        byte[] pcm = Submit(sink, 1024);

        var src = al.SingleSource;
        src.State.Should().Be(Al.PLAYING);
        var buffer = src.Queue.Should().ContainSingle().Subject;
        buffer.Samples.Should().Be(1024);
        buffer.Format.Should().Be(Al.FORMAT_STEREO16);
        buffer.Frequency.Should().Be(Rate);
        buffer.Data.Should().Equal(pcm, "the PCM must be copied during Submit (the caller's memory is reused)");
    }

    [Fact]
    public void Submit_Mono_UsesMono16AndTwoBytesPerSample()
    {
        FakeOpenAl al = new();
        using var sink = CreateSink(al, channels: 1);

        Submit(sink, 100, channels: 1);

        var buffer = al.SingleSource.Queue.Single();
        buffer.Format.Should().Be(Al.FORMAT_MONO16);
        buffer.Samples.Should().Be(100);
    }

    [Fact]
    public void Submit_IgnoresEmptyInput_AndTruncatesPartialFrames()
    {
        FakeOpenAl al = new();
        using var sink = CreateSink(al);

        sink.Submit(0, 0);
        sink.Submit(0, 3); // less than one frame
        al.SingleSource.Queue.Should().BeEmpty();

        nint ptr = Marshal.AllocHGlobal(4001);
        try { sink.Submit(ptr, 4001); } finally { Marshal.FreeHGlobal(ptr); }

        al.SingleSource.Queue.Single().Samples.Should().Be(1000);
    }

    [Fact]
    public void SamplesPlayed_IsProcessedBuffersPlusOffsetInCurrentBuffer()
    {
        FakeOpenAl al = new();
        using var sink = CreateSink(al);
        Submit(sink, 1000);
        Submit(sink, 1000);
        Submit(sink, 1000);

        sink.SamplesPlayed.Should().Be(0);

        al.Advance(500);
        sink.SamplesPlayed.Should().Be(500);

        al.Advance(1000);
        sink.SamplesPlayed.Should().Be(1500);
        al.SingleSource.Queue.Should().HaveCount(2, "the processed buffer is unqueued for reuse");

        al.Advance(1400);
        sink.SamplesPlayed.Should().Be(2900);
    }

    [Fact]
    public void SamplesPlayed_NeverExceedsSubmitted_AndStaysMonotonic()
    {
        FakeOpenAl al = new();
        using var sink = CreateSink(al);
        Random rnd = new(1234);
        ulong submitted = 0, last = 0;

        for (int i = 0; i < 2000; i++)
        {
            switch (rnd.Next(4))
            {
                case 0:
                case 1:
                    if (sink.QueuedBuffers < OpenAlAudioSink.MaxQueuedBuffers)
                    {
                        int n = rnd.Next(1, 2048);
                        Submit(sink, n);
                        submitted += (ulong)n;
                    }
                    break;
                case 2:
                    al.Advance(rnd.Next(0, 3000));
                    break;
                case 3:
                    if (rnd.Next(20) == 0)
                    {
                        sink.Flush();
                        submitted = sink.SamplesPlayed; // flushed audio is gone (Audio.ClearBuffer does the same)
                    }
                    break;
            }

            ulong played = sink.SamplesPlayed;
            played.Should().BeGreaterThanOrEqualTo(last, "SamplesPlayed is monotonic");
            played.Should().BeLessThanOrEqualTo(submitted, "never more than submitted");
            last = played;
        }

        // drain: everything submitted is eventually reported as played
        al.Advance(long.MaxValue / 4);
        sink.SamplesPlayed.Should().Be(submitted);
        al.ContextViolations.Should().Be(0);
    }

    [Fact]
    public void Submit_ReusesProcessedBuffers()
    {
        FakeOpenAl al = new();
        using var sink = CreateSink(al);

        for (int i = 0; i < 500; i++)
        {
            Submit(sink, 1024);
            al.Advance(1024);
        }

        sink.SamplesPlayed.Should().Be(500UL * 1024);
        al.GenBufferCalls.Should().BeLessThanOrEqualTo(2, "processed buffers are recycled instead of allocating new ones");
        sink.AllocatedBuffers.Should().Be(al.GenBufferCalls);
    }

    [Fact]
    public void Submit_Throws_WhenTheQueueIsFull_LikeXAudio2()
    {
        FakeOpenAl al = new();
        using var sink = CreateSink(al);
        for (int i = 0; i < OpenAlAudioSink.MaxQueuedBuffers; i++)
            Submit(sink, 10);

        var act = () => Submit(sink, 10);

        act.Should().Throw<InvalidOperationException>().WithMessage("*queued buffers*");
        sink.QueuedBuffers.Should().Be(OpenAlAudioSink.MaxQueuedBuffers);
        al.GenBufferCalls.Should().Be(OpenAlAudioSink.MaxQueuedBuffers, "no buffer is allocated for rejected audio");

        al.Advance(10); // one buffer done: room again
        Submit(sink, 10);
        sink.QueuedBuffers.Should().Be(OpenAlAudioSink.MaxQueuedBuffers);
    }

    [Fact]
    public void Submit_Throws_WhenTheDeviceWasDisconnected()
    {
        FakeOpenAl al = new();
        using var sink = CreateSink(al);
        al.Disconnect();

        var act = () => Submit(sink, 10);

        act.Should().Throw<InvalidOperationException>().WithMessage("*disconnected*");
    }

    // === Underrun ================================================================================================

    [Fact]
    public void Underrun_NextSubmitRestartsPlayback_WithOnlyTheNewAudio()
    {
        FakeOpenAl al = new();
        using var sink = CreateSink(al);
        Submit(sink, 1000);

        al.Advance(5000); // starved: the source stops
        al.SingleSource.State.Should().Be(Al.STOPPED);
        sink.SamplesPlayed.Should().Be(1000);

        Submit(sink, 500);

        var src = al.SingleSource;
        src.State.Should().Be(Al.PLAYING);
        src.Queue.Should().ContainSingle().Which.Samples.Should().Be(500);
        sink.UnderrunRestarts.Should().Be(1);

        al.Advance(200);
        sink.SamplesPlayed.Should().Be(1200);
    }

    [Fact]
    public void Underrun_RacingWithSubmit_DoesNotReplayFinishedAudio()
    {
        // The source finishes its last buffer after Submit reclaimed the processed ones but before the new buffer is
        // queued: a stopped source restarts at its queue head, so the finished buffer must be dropped first.
        FakeOpenAl al = new();
        using var sink = CreateSink(al);
        Submit(sink, 1000);
        al.Advance(900);
        sink.SamplesPlayed.Should().Be(900);

        al.BeforeQueueBuffer = () => { al.BeforeQueueBuffer = null; al.Advance(100); };
        Submit(sink, 500);

        var src = al.SingleSource;
        src.State.Should().Be(Al.PLAYING);
        src.Queue.Should().ContainSingle().Which.Samples.Should().Be(500, "the finished 1000-sample buffer must not be replayed");
        sink.SamplesPlayed.Should().Be(1000);
        sink.UnderrunRestarts.Should().Be(1);

        al.Advance(500);
        sink.SamplesPlayed.Should().Be(1500);
    }

    [Fact]
    public void Submit_WhilePlaying_DoesNotRestartTheSource()
    {
        FakeOpenAl al = new();
        using var sink = CreateSink(al);
        Submit(sink, 1000);
        al.Advance(300);

        Submit(sink, 1000);

        al.SingleSource.PlayCalls.Should().Be(1);
        sink.SamplesPlayed.Should().Be(300);
    }

    // === Flush ===================================================================================================

    [Fact]
    public void Flush_DropsQueuedAudio_WithoutCountingIt_LikeNullAudioSink()
    {
        FakeOpenAl al = new();
        using var sink = CreateSink(al);
        Submit(sink, 1000);
        Submit(sink, 1000);
        Submit(sink, 1000);
        al.Advance(1500);

        sink.Flush();

        sink.SamplesPlayed.Should().Be(1500, "only the audio played before the flush counts");
        al.SingleSource.Queue.Should().BeEmpty();
        al.SingleSource.State.Should().Be(Al.INITIAL);
        al.Calls.Should().ContainInConsecutiveOrder("pause", "stop", "rewind");

        al.Advance(10_000); // nothing queued: the clock does not move
        sink.SamplesPlayed.Should().Be(1500);

        Submit(sink, 1000);
        sink.UnderrunRestarts.Should().Be(0, "a flush is not an underrun");
        al.Advance(400);
        sink.SamplesPlayed.Should().Be(1900);
    }

    [Fact]
    public void Flush_ReusesTheFlushedBuffers()
    {
        FakeOpenAl al = new();
        using var sink = CreateSink(al);
        for (int i = 0; i < 10; i++)
            Submit(sink, 100);

        sink.Flush();
        for (int i = 0; i < 10; i++)
            Submit(sink, 100);

        al.GenBufferCalls.Should().Be(10);
    }

    [Fact]
    public void Flush_OnAStoppedOrEmptySink_IsHarmless()
    {
        FakeOpenAl al = new();
        using var sink = CreateSink(al);

        sink.Flush();
        sink.SamplesPlayed.Should().Be(0);

        Submit(sink, 100);
        al.Advance(1000);
        sink.Flush();
        sink.SamplesPlayed.Should().Be(100);
    }

    // === Volume / latency ========================================================================================

    [Fact]
    public void Volume_SetsSourceGain_RaisingMaxGainAbove100Percent()
    {
        FakeOpenAl al = new();
        using var sink = CreateSink(al);

        sink.Volume = 1.5f;
        al.SingleSource.Floats[Al.GAIN].Should().Be(1.5f);
        al.SingleSource.Floats[Al.MAX_GAIN].Should().BeGreaterThanOrEqualTo(1.5f);
        sink.Volume.Should().Be(1.5f);

        sink.Volume = 0;
        al.SingleSource.Floats[Al.GAIN].Should().Be(0);

        sink.Volume = -1;
        sink.Volume.Should().Be(0);

        sink.Volume = float.NaN;
        sink.Volume.Should().Be(0);
        al.SingleSource.Floats[Al.GAIN].Should().Be(0, "invalid gains never reach AL");
    }

    [Fact]
    public void MasterVolume_SetsListenerGain()
    {
        FakeOpenAl al = new();
        using var sink = CreateSink(al);

        sink.MasterVolume = 0.75f;

        al.Contexts.Single().ListenerGain.Should().Be(0.75f);
        sink.MasterVolume.Should().Be(0.75f);
    }

    [Fact]
    public void LatencySamples_PrefersSourceLatency_ThenDeviceClock_ThenConstant()
    {
        FakeOpenAl al = new() { SourceLatencyNs = 20_000_000, DeviceLatencyNs = 10_000_000 };
        al.AlcExtensions.Add("ALC_SOFT_device_clock");
        using (var sink = CreateSink(al))
            sink.LatencySamples.Should().Be(960, "20 ms at 48 kHz (AL_SOFT_source_latency)");

        al.AlExtensions.Remove("AL_SOFT_source_latency");
        using (var sink = CreateSink(al))
            sink.LatencySamples.Should().Be(480, "10 ms at 48 kHz (ALC_SOFT_device_clock)");

        al.AlcExtensions.Remove("ALC_SOFT_device_clock");
        using (var sink = CreateSink(al))
            sink.LatencySamples.Should().Be(OpenAlAudioSink.FallbackLatencyMs * Rate / 1000);
    }

    // === Lifetime / threading ====================================================================================

    [Fact]
    public void Dispose_IsIdempotentAndThreadSafe_AndReleasesEverything()
    {
        FakeOpenAl al = new();
        var sink = CreateSink(al);
        Submit(sink, 1000);
        Submit(sink, 1000);
        al.Advance(1200);
        ulong played = sink.SamplesPlayed;

        Parallel.For(0, 16, _ => sink.Dispose());

        al.DestroyContextCalls.Should().Be(1);
        al.CloseDeviceCalls.Should().Be(1);
        al.Devices.Single().Closed.Should().BeTrue();
        al.Contexts.Single().Destroyed.Should().BeTrue();
        al.LiveBuffers.Should().Be(0);
        al.SingleSource.Deleted.Should().BeTrue();

        sink.SamplesPlayed.Should().Be(played);
        sink.LatencySamples.Should().Be(0);
        sink.Flush();
        sink.Volume = 0.5f;
        var submit = () => Submit(sink, 10);
        submit.Should().Throw<ObjectDisposedException>();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ConcurrentSinks_NeverUseAnotherSinksContext(bool threadLocal)
    {
        // Several players = several contexts. Without ALC_EXT_thread_local_context the process-wide current context
        // must be switched under one lock; the fake throws on any AL call made while another context is current.
        FakeOpenAl al = new() { ThreadLocalContexts = threadLocal };
        var sinks = Enumerable.Range(0, 4).Select(_ => CreateSink(al)).ToArray();
        try
        {
            Parallel.For(0, sinks.Length * 2, new ParallelOptions { MaxDegreeOfParallelism = 8 }, i =>
            {
                var sink = sinks[i % sinks.Length];
                for (int n = 0; n < 300; n++)
                {
                    if (sink.QueuedBuffers < 8)
                        Submit(sink, 64);
                    _ = sink.SamplesPlayed;
                    _ = sink.LatencySamples;
                    if (n % 50 == 0)
                        sink.Flush();
                    sink.Volume = n % 2;
                }
            });
        }
        finally
        {
            foreach (var sink in sinks)
                sink.Dispose();
        }

        al.ContextViolations.Should().Be(0);
        if (threadLocal)
            al.MakeContextCurrentCalls.Should().Be(0, "thread-local contexts need no process-wide switch");
        else
            al.SetThreadContextCalls.Should().Be(0);
        al.Contexts.Should().OnlyContain(c => c.Destroyed);
    }

    [Fact]
    public void ContextFailure_OnThePlaybackThread_DoesNotThrowFromReadPaths()
    {
        FakeOpenAl al = new();
        using var sink = CreateSink(al);
        Submit(sink, 1000);
        al.Advance(250);
        sink.SamplesPlayed.Should().Be(250);

        al.FailSetContext = true;

        sink.SamplesPlayed.Should().Be(250, "the clock freezes instead of throwing on the playback thread");
        sink.Flush();
        sink.LatencySamples.Should().Be(OpenAlAudioSink.FallbackLatencyMs * Rate / 1000);
        sink.FaultReason.Should().NotBeNull();
        var submit = () => Submit(sink, 10);
        submit.Should().Throw<InvalidOperationException>("the player then clears its buffer, as after an XAudio2 device error");
    }
}
