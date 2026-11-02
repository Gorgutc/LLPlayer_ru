using AwesomeAssertions;
using System.Runtime.InteropServices;

namespace FlyleafLib.Platform.Audio;

public class PortableAudioOutputTests
{
    [Fact]
    public void ThrowingOutputClock_DropsTheBrokenQueueWithoutEscapingThePlaybackRead()
    {
        TestBackend backend = new();
        using PortableAudioOutput output = new(backend, null, 48000, 2);
        output.Submit(0, 40000);
        backend.Created.Single().ReadFailure = new InvalidOperationException("clock unavailable");

        output.BufferedSamples.Should().Be(0);
        output.Recovering.Should().BeTrue();
        output.CurrentSink.Should().BeOfType<NullAudioSink>();
        backend.Attempts.Should().Be(1);
    }

    [Theory]
    [InlineData("clock")]
    [InlineData("latency")]
    [InlineData("volume")]
    [InlineData("master")]
    [InlineData("disconnect")]
    [InlineData("clock-error")]
    [InlineData("latency-error")]
    [InlineData("gain-error")]
    public void QueuedOutputReadOrGainFailure_RetiresTheFrozenClockBeforeAnotherSubmit(string failure)
    {
        FakeOpenAl al = new();
        OpenAlAudioBackend backend = new(al);
        using PortableAudioOutput output = new(backend, null, 48000, 2);
        nint pcm = Marshal.AllocHGlobal(40000);
        try
        {
            Marshal.Copy(new byte[40000], 0, pcm, 40000);
            output.Submit(pcm, 40000).Should().BeTrue();
            output.BufferedSamples.Should().Be(10000, "more than 40ms queued prevents the playback loop reaching another Submit");
            string[] devices = backend.EnumerateDevices().Select(device => device.Id).ToArray();
            switch (failure)
            {
                case "disconnect": al.Disconnect(); break;
                case "clock-error": al.FailClockRead = true; break;
                case "latency-error": al.FailLatencyRead = true; break;
                case "gain-error": al.FailGainUpdate = true; break;
                default: al.FailSetContext = true; break;
            }

            switch (failure)
            {
                case "latency":
                case "latency-error": _ = output.LatencySamples; break;
                case "volume": output.Volume = .5f; break;
                case "gain-error": output.Volume = .5f; break;
                case "master": output.MasterVolume = 1.5f; break;
                default: _ = output.BufferedSamples; break;
            }

            output.Recovering.Should().BeTrue("a swallowed native read/gain fault must reach the recovery owner");
            output.CurrentSink.Should().BeOfType<NullAudioSink>();
            output.BufferedSamples.Should().Be(0, "a frozen native clock must not trap the playback loop before Submit");
            al.OpenDeviceCalls.Should().Be(1, "observing a broken output never reopens a native device");
            backend.EnumerateDevices().Select(device => device.Id).Should().Equal(devices, "physical removal need not change enumeration");

            al.FailSetContext = false;
            al.FailClockRead = false;
            al.FailLatencyRead = false;
            al.FailGainUpdate = false;
            output.TryRecover(null).Should().BeTrue("the next bounded host refresh must recover even when device IDs did not change");
            al.OpenDeviceCalls.Should().Be(2);
            output.Recovering.Should().BeFalse();
            output.BufferedSamples.Should().Be(0, "the old output's PCM is not replayed");
        }
        finally { Marshal.FreeHGlobal(pcm); }
    }

    [Fact]
    public void OpenAlQueuePressure_RetainsItsNativeOutput_WhileDisconnectRequestsRecovery()
    {
        FakeOpenAl al = new();
        using PortableAudioOutput output = new(new OpenAlAudioBackend(al), null, 48000, 2);
        nint pcm = Marshal.AllocHGlobal(400);
        try
        {
            Marshal.Copy(new byte[400], 0, pcm, 400);
            for (int buffer = 0; buffer < 64; buffer++) output.Submit(pcm, 400).Should().BeTrue();
            output.Submit(pcm, 400).Should().BeFalse();
            output.Recovering.Should().BeFalse();
            output.CurrentSink.Should().BeOfType<OpenAlAudioSink>();
            output.BufferedSamples.Should().Be(0);
            al.OpenDeviceCalls.Should().Be(1);

            al.Disconnect();
            output.Submit(pcm, 400).Should().BeFalse();
            output.Recovering.Should().BeTrue();
            output.CurrentSink.Should().BeOfType<NullAudioSink>();
            al.OpenDeviceCalls.Should().Be(1, "a playback failure never opens a native device");
        }
        finally { Marshal.FreeHGlobal(pcm); }
    }

    [Fact]
    public void FatalSubmit_DropsQueuedPcm_AndDoesNotReopenUntilExplicitRecovery()
    {
        TestBackend backend = new();
        using PortableAudioOutput output = new(backend, null, 48000, 2);
        TestSink old = backend.Created.Single();
        output.Submit(0, 400).Should().BeTrue();
        old.SubmitFailure = new InvalidOperationException("disconnected");

        output.Submit(0, 800).Should().BeFalse();
        output.Recovering.Should().BeTrue();
        old.Disposed.Should().BeTrue();
        output.BufferedSamples.Should().Be(0);
        for (int frame = 0; frame < 10; frame++) output.Submit(0, 40);
        backend.Attempts.Should().Be(1, "playback frames cannot reopen native outputs");

        output.TryRecover(null).Should().BeTrue();
        TestSink recovered = backend.Created.Last();
        recovered.AcceptedBytes.Should().BeEmpty("old PCM is never replayed on another output");
        output.Recovering.Should().BeFalse();
        output.Submit(0, 120).Should().BeTrue();
        recovered.AcceptedBytes.Should().Equal(120);
        output.BufferedSamples.Should().Be(30);
    }

    [Fact]
    public void QueuePressure_FlushesWithoutDisconnectOrReopen()
    {
        TestBackend backend = new();
        using PortableAudioOutput output = new(backend, null, 48000, 2);
        TestSink sink = backend.Created.Single();
        output.Submit(0, 400);
        sink.SubmitFailure = new AudioQueueFullException("full");

        output.Submit(0, 800).Should().BeFalse();

        sink.Flushes.Should().Be(1);
        sink.Disposed.Should().BeFalse();
        output.Recovering.Should().BeFalse();
        output.BufferedSamples.Should().Be(0);
        output.TryRecover(null).Should().BeFalse();
        backend.Attempts.Should().Be(1);
    }

    [Fact]
    public void FailedRecovery_AttemptsOnceAndKeepsSilentClockUntilNextRefresh()
    {
        TestBackend backend = new() { Fail = true };
        using PortableAudioOutput output = new(backend, null, 48000, 2);
        output.CurrentSink.Should().BeOfType<NullAudioSink>();
        output.Recovering.Should().BeTrue();

        output.TryRecover(null).Should().BeFalse();
        backend.Attempts.Should().Be(2);
        for (int frame = 0; frame < 10; frame++) output.Submit(0, 400);
        backend.Attempts.Should().Be(2);
        output.CurrentSink.Should().BeOfType<NullAudioSink>();
    }

    [Fact]
    public void ForcedNull_RemainsSilentWithoutRecoveryRequests()
    {
        using PortableAudioOutput output = new(NullAudioBackend.Instance, null, 48000, 2);
        output.Recovering.Should().BeFalse();
        output.TryRecover(null).Should().BeFalse();
        output.CurrentSink.Should().BeOfType<NullAudioSink>();
    }

    [Fact]
    public void DefaultDeviceChange_ReplacesHealthyOutputAndPreservesBothGains()
    {
        TestBackend backend = new();
        using PortableAudioOutput output = new(backend, null, 48000, 2);
        output.Volume = .65f;
        output.MasterVolume = 1.5f;
        output.Submit(0, 400);
        TestSink old = backend.Created.Single();

        output.TryRecover(null, force: true).Should().BeTrue();

        old.Disposed.Should().BeTrue();
        TestSink current = backend.Created.Last();
        current.Volume.Should().Be(.65f);
        current.MasterVolume.Should().Be(1.5f);
        current.AcceptedBytes.Should().BeEmpty();
        output.BufferedSamples.Should().Be(0);
    }

    [Fact]
    public void Dispose_PreventsLaterRecoveryAndPcmAdmission()
    {
        TestBackend backend = new();
        PortableAudioOutput output = new(backend, null, 48000, 2);
        output.Dispose();

        output.TryRecover("headphones", force: true).Should().BeFalse();
        output.Submit(0, 400).Should().BeFalse();
        backend.Attempts.Should().Be(1);
        backend.Created.Single().Disposed.Should().BeTrue();
    }

    sealed class TestBackend : IAudioBackend
    {
        public string Name => "test";
        public bool Fail;
        public int Attempts;
        public List<TestSink> Created { get; } = [];
        public IReadOnlyList<AudioEngine.AudioEndpoint> EnumerateDevices() => [];
        public AudioEngine.AudioEndpoint? GetDefaultDevice() => null;
        public IAudioSink CreateSink(string? deviceId, int sampleRate, int channels)
        {
            Attempts++;
            if (Fail) throw new InvalidOperationException("unavailable");
            TestSink sink = new(sampleRate, channels);
            Created.Add(sink);
            return sink;
        }
    }

    sealed class TestSink(int sampleRate, int channels) : IAudioSink
    {
        public int SampleRate => sampleRate;
        public int Channels => channels;
        public Exception? SubmitFailure;
        public Exception? ReadFailure;
        public bool Disposed;
        public int Flushes;
        public List<int> AcceptedBytes { get; } = [];
        public ulong SamplesPlayed => ReadFailure == null ? 0UL : throw ReadFailure;
        public int LatencySamples => 0;
        public float Volume { get; set; } = 1;
        public float MasterVolume { get; set; } = 1;
        public void Submit(nint data, int byteCount)
        {
            if (SubmitFailure != null) throw SubmitFailure;
            AcceptedBytes.Add(byteCount);
        }
        public void Flush() => Flushes++;
        public void Dispose() => Disposed = true;
    }
}
