using AwesomeAssertions;

namespace FlyleafLib.Platform.Audio;

public class AudioRecoveryTests
{
    // Catches losing the real backend when auto starts before any output exists.
    [Fact]
    public void AutoBackend_RecoversAfterAnOutputAppears_WithoutRestartingTheEngine()
    {
        FakeOpenAl al = new() { FailOpenDevice = true };
        var backend = AudioBackendFactory.Create("auto", () => (al, null), _ => { });
        using (var silent = backend.CreateSink(null, 48000, 2))
            silent.Should().BeOfType<NullAudioSink>();

        al.FailOpenDevice = false;
        backend.EnumerateDevices().Select(device => device.Id).Should().Contain("Fake Speakers");
        using var recovered = backend.CreateSink(null, 48000, 2);
        recovered.Should().BeOfType<OpenAlAudioSink>();
    }

    // Recovery is driven by the host's bounded refresh, never by PCM submission or sink construction.
    [Fact]
    public void AutoBackend_DoesNotProbeAgainUntilTheHostRefreshesDevices()
    {
        FakeOpenAl al = new() { FailOpenDevice = true };
        var backend = AudioBackendFactory.Create("auto", () => (al, null), _ => { });
        al.FailOpenDevice = false;
        for (int attempt = 0; attempt < 3; attempt++)
            using (var silent = backend.CreateSink(null, 48000, 2))
                silent.Should().BeOfType<NullAudioSink>();
        al.OpenDeviceCalls.Should().Be(1);

        backend.EnumerateDevices();
        using var recovered = backend.CreateSink(null, 48000, 2);
        recovered.Should().BeOfType<OpenAlAudioSink>();
    }

    [Fact]
    public void ForcedNull_DoesNotLoadOrRecoverTheRealBackend()
    {
        int loads = 0;
        var backend = AudioBackendFactory.Create("null", () => { loads++; return (new FakeOpenAl(), null); }, _ => { });
        for (int attempt = 0; attempt < 3; attempt++)
        {
            backend.EnumerateDevices().Should().BeEmpty();
            using var sink = backend.CreateSink(null, 48000, 2);
            sink.Should().BeOfType<NullAudioSink>();
        }
        loads.Should().Be(0);
    }
}
