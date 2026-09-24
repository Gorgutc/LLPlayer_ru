using System.Text;
using AwesomeAssertions;

namespace FlyleafLib.Platform.Audio;

// F-13 (Linux port): OpenAL device enumeration / default device / sink creation, ALC string-list parsing and the host's
// backend selection (AudioBackendFactory incl. the LLPLAYER_AUDIO_BACKEND override). No libopenal needed.
public class OpenAlAudioBackendTests
{
    // === Enumeration =============================================================================================

    [Fact]
    public void EnumerateDevices_UsesTheFullListOfEnumerateAllExt_WithNamesAsStableIds()
    {
        FakeOpenAl al = new();
        al.AllDevices.Add("Fake Speakers"); // duplicate
        al.AllDevices.Add("");
        OpenAlAudioBackend backend = new(al);

        var devices = backend.EnumerateDevices();

        devices.Select(d => d.Id).Should().Equal("Fake Speakers", "Fake Headphones");
        devices.Should().OnlyContain(d => d.Id == d.Name);
        backend.GetDefaultDevice()!.Id.Should().Be("Fake Speakers");
        backend.Name.Should().Be("OpenAL");
    }

    [Fact]
    public void EnumerateDevices_FallsBackToTheBasicDeviceSpecifier()
    {
        FakeOpenAl al = new();
        al.AlcExtensions.Remove("ALC_ENUMERATE_ALL_EXT");
        OpenAlAudioBackend backend = new(al);

        backend.EnumerateDevices().Select(d => d.Id).Should().Equal("OpenAL Soft");
        backend.GetDefaultDevice()!.Id.Should().Be("OpenAL Soft");
    }

    [Fact]
    public void EnumerateDevices_IsEmpty_WithoutEnumerationExtensions()
    {
        FakeOpenAl al = new();
        al.AlcExtensions.Clear();
        OpenAlAudioBackend backend = new(al);

        backend.EnumerateDevices().Should().BeEmpty();
        backend.GetDefaultDevice().Should().BeNull();
    }

    [Fact]
    public void GetDefaultDevice_IsNull_WhenOpenAlReportsNone()
    {
        FakeOpenAl al = new() { DefaultAllDevice = null };

        new OpenAlAudioBackend(al).GetDefaultDevice().Should().BeNull();
    }

    [Fact]
    public void CreateSink_OpensTheNamedDevice_OrTheDefaultForNull()
    {
        FakeOpenAl al = new();
        OpenAlAudioBackend backend = new(al);

        using (var sink = backend.CreateSink("Fake Headphones", 44100, 2))
            sink.SampleRate.Should().Be(44100);
        using (backend.CreateSink(null, 48000, 2)) { }

        al.OpenedDeviceNames.Should().Equal("Fake Headphones", "<default>");
        var act = () => backend.CreateSink("Unplugged", 48000, 2);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ParseStringList_SplitsDoubleNulTerminatedUtf8Lists()
    {
        byte[] data = Encoding.UTF8.GetBytes("Built-in Audio Analog Stereo\0HDMI – Écran\0\0garbage\0\0");

        OpenAlNative.ParseStringList(data).Should().Equal("Built-in Audio Analog Stereo", "HDMI – Écran");
        OpenAlNative.ParseStringList("\0\0"u8).Should().BeEmpty();
        OpenAlNative.ParseStringList([]).Should().BeEmpty();
        OpenAlNative.ParseStringList("Unterminated"u8).Should().Equal("Unterminated");
    }

    [Fact]
    public void Native_BindsTheSystemLibrary_AndEnumeratesWithoutOpeningDevices()
    {
        Assert.SkipUnless(OpenAlAudioBackend.TryCreate(out var backend, out string? error), $"OpenAL is not installed: {error}");

        var devices = backend!.EnumerateDevices();
        var defaultDevice = backend.GetDefaultDevice();

        devices.Should().OnlyContain(d => !string.IsNullOrEmpty(d.Id) && d.Id == d.Name);
        devices.Select(d => d.Id).Should().OnlyHaveUniqueItems();
        if (defaultDevice != null && devices.Count > 0)
            devices.Select(d => d.Id).Should().Contain(defaultDevice.Id);
        OpenAlNative.LibraryNames.Should().StartWith("libopenal.so.1");
    }

    // === AudioBackendFactory =====================================================================================

    static (IOpenAl? api, string? error) Loaded(FakeOpenAl al) => (al, null);

    static (IOpenAl? api, string? error) Missing() => (null, "OpenAL library not found (tried libopenal.so.1)");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("auto")]
    [InlineData(" AUTO ")]
    public void Factory_Auto_PicksOpenAl_WhenTheDefaultDeviceOpens(string? env)
    {
        FakeOpenAl al = new();
        List<string> log = [];

        var backend = AudioBackendFactory.Create(env, () => Loaded(al), log.Add);

        backend.Should().BeOfType<OpenAlAudioBackend>();
        al.OpenedDeviceNames.Should().Equal("<default>");
        al.Devices.Should().OnlyContain(d => d.Closed, "the probe device is closed again");
        log.Should().ContainSingle().Which.Should().Contain("OpenAL");
    }

    [Fact]
    public void Factory_Auto_FallsBackToNull_WithAWarning_WhenNoDeviceOpens()
    {
        FakeOpenAl al = new() { FailOpenDevice = true };
        List<string> log = [];

        var backend = AudioBackendFactory.Create(null, () => Loaded(al), log.Add);

        backend.Should().BeSameAs(NullAudioBackend.Instance);
        log.Should().ContainSingle().Which.Should().Contain("Null").And.Contain("default device cannot be opened");
    }

    [Fact]
    public void Factory_Auto_FallsBackToNull_WithTheReason_WhenTheLibraryIsMissing()
    {
        List<string> log = [];

        var backend = AudioBackendFactory.Create("auto", Missing, log.Add);

        backend.Should().BeSameAs(NullAudioBackend.Instance);
        log.Should().ContainSingle().Which.Should().Contain("libopenal.so.1");
    }

    [Theory]
    [InlineData("null")]
    [InlineData("NULL")]
    public void Factory_NullOverride_NeverTouchesOpenAl(string env)
    {
        bool loaded = false;
        List<string> log = [];

        var backend = AudioBackendFactory.Create(env, () => { loaded = true; return Missing(); }, log.Add);

        backend.Should().BeSameAs(NullAudioBackend.Instance);
        loaded.Should().BeFalse();
        log.Should().ContainSingle().Which.Should().Contain(AudioBackendFactory.BackendVariable);
    }

    [Fact]
    public void Factory_OpenAlOverride_SkipsTheDeviceProbe()
    {
        FakeOpenAl al = new() { FailOpenDevice = true };
        List<string> log = [];

        var backend = AudioBackendFactory.Create("OpenAL", () => Loaded(al), log.Add);

        backend.Should().BeOfType<OpenAlAudioBackend>();
        al.OpenDeviceCalls.Should().Be(0);
    }

    [Fact]
    public void Factory_OpenAlOverride_StillFallsBackToNull_WhenTheLibraryIsMissing()
    {
        List<string> log = [];

        var backend = AudioBackendFactory.Create("openal", Missing, log.Add);

        backend.Should().BeSameAs(NullAudioBackend.Instance);
        log.Should().ContainSingle().Which.Should().Contain("unavailable");
    }

    [Fact]
    public void Factory_UnknownValue_WarnsAndUsesAuto()
    {
        FakeOpenAl al = new();
        List<string> log = [];

        var backend = AudioBackendFactory.Create("pulse", () => Loaded(al), log.Add);

        backend.Should().BeOfType<OpenAlAudioBackend>();
        log.Should().HaveCount(2);
        log[0].Should().Contain("unknown").And.Contain("pulse");
    }

    [Fact]
    public void CreateDefault_ReadsTheEnvironmentVariable()
    {
        string? saved = Environment.GetEnvironmentVariable(AudioBackendFactory.BackendVariable);
        try
        {
            Environment.SetEnvironmentVariable(AudioBackendFactory.BackendVariable, "null");
            List<string> log = [];

            AudioBackendFactory.CreateDefault(log.Add).Should().BeSameAs(NullAudioBackend.Instance);
            log.Should().ContainSingle();
        }
        finally
        {
            Environment.SetEnvironmentVariable(AudioBackendFactory.BackendVariable, saved);
        }
    }
}
