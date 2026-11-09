using FlyleafLib.MediaFramework.MediaDevice;
using System.Runtime.InteropServices;

namespace FlyleafLib;

public class CaptureDeviceTests
{
    [Theory]
    [InlineData("video=Камера 1 & input?=100%:α")]
    [InlineData("audio=@device_cm_{id}\\wave_{01}&?=%:")]
    public void Escaped_native_input_and_rational_format_survive_actual_demuxer_parser(string input)
    {
        var device = new CaptureDeviceInfo("dshow:video:unique", "Friendly", "dshow", input, CaptureDeviceKind.Video);
        var format = new CaptureVideoFormatInfo("format", device.Id, 1920, 1080, 30000, 1001, "yuyv422", null);
        FmtInputUrl.Parse(CaptureInputUrl.Build(device, format), out string backend, out string? actualInput, out var options);
        Assert.Equal("dshow", backend);
        Assert.Equal(input, actualInput);
        Assert.Equal("30000/1001", options!["framerate"]);
        Assert.Equal("1920x1080", options["video_size"]);
        Assert.Equal("yuyv422", options["pixel_format"]);
    }

    [Fact]
    public void Default_open_uses_the_same_escaped_native_identity_without_format_options()
    {
        var device = new CaptureDeviceInfo("native", "Friendly", "v4l2", "/dev/Камера&?=100%:", CaptureDeviceKind.Video);
        FmtInputUrl.Parse(CaptureInputUrl.Build(device), out _, out string? input, out var options);
        Assert.Equal(device.Input, input);
        Assert.Null(options);
    }

    [Theory]
    [InlineData("fmt://dshow?video=Literal%26Name&framerate=30", "dshow", "video=Literal%26Name")]
    [InlineData("device://gdigrab/?desktop", "gdigrab", "desktop")]
    [InlineData("fmt://rawvideo?C:\\x%20y.raw&framerate=29.97", "rawvideo", "C:\\x%20y.raw")]
    [InlineData("fmt://gdigrab", "gdigrab", null)]
    public void Legacy_input_is_not_decoded_or_reinterpreted(string url, string backend, string? input)
    {
        FmtInputUrl.Parse(url, out string actualBackend, out string? actualInput, out _);
        Assert.Equal(backend, actualBackend);
        Assert.Equal(input, actualInput);
    }

    [Fact]
    public void Untyped_v4l2_is_video_but_untyped_dshow_is_not_guessed()
    {
        var catalog = new CaptureDeviceCatalog(new Backend());
        var result = catalog.Refresh(CaptureDeviceKind.Video, true, ["v4l2", "dshow"], TestContext.Current.CancellationToken);
        Assert.Equal(CaptureAvailability.Available, result.Availability);
        var device = Assert.Single(result.Devices);
        Assert.Equal("v4l2", device.Backend);
        Assert.Equal("/dev/video0", device.Input);
    }

    [Fact]
    public void Missing_All_profile_is_unavailable_without_invoking_native_backend()
    {
        var backend = new Backend();
        var result = new CaptureDeviceCatalog(backend).Refresh(CaptureDeviceKind.Video, false, ["v4l2"], TestContext.Current.CancellationToken);
        Assert.Equal(CaptureAvailability.Unavailable, result.Availability);
        Assert.Equal(0, backend.Calls);
        Assert.NotNull(result.Reason);
    }

    [Fact]
    public void Format_from_another_device_is_rejected()
    {
        var device = new CaptureDeviceInfo("native", "Friendly", "v4l2", "/dev/video0", CaptureDeviceKind.Video);
        var format = new CaptureVideoFormatInfo("format", "other", 640, 480, 30, 1, "yuyv422", null);
        Assert.Throws<ArgumentException>(() => CaptureInputUrl.Build(device, format));
    }

    [Fact]
    public void Names_ending_with_subtitle_or_playlist_extensions_cannot_bypass_capture_demuxing()
    {
        foreach (string name in new[] { "camera.srt", "camera.m3u", "camera.pls" })
        {
            var device = new CaptureDeviceInfo("id", "Label", "v4l2", name, CaptureDeviceKind.Video);
            string url = CaptureInputUrl.Build(device);
            Assert.Equal("", Utils.GetUrlExtention(url));
            Assert.Equal(url, Utils.FixFileUrl(url));
            FmtInputUrl.Parse(url, out _, out var input, out _);
            Assert.Equal(name, input);
        }
    }

    [Fact]
    public void V4l2_uses_input_format_and_exact_ratio_while_dshow_uses_codec_selection()
    {
        var linux = new CaptureDeviceInfo("linux", "Camera", "video4linux2", "/dev/video0", CaptureDeviceKind.Video);
        var windows = new CaptureDeviceInfo("windows", "Camera", "dshow", "video=@device&x", CaptureDeviceKind.Video);
        foreach (var device in new[] { linux, windows })
        {
            var format = new CaptureVideoFormatInfo("mjpeg", device.Id, 1280, 720, 30000, 1001, null, "mjpeg");
            FmtInputUrl.Parse(CaptureInputUrl.Build(device, format), out _, out _, out var options);
            Assert.Equal("30000/1001", options!["framerate"]);
            Assert.Equal("mjpeg", options[device.Backend == "dshow" ? "vcodec" : "input_format"]);
        }
    }

    [Fact]
    public void Catalog_distinguishes_successful_empty_missing_api_and_native_error()
    {
        foreach (var expected in new[] { CaptureAvailability.Available, CaptureAvailability.Unavailable, CaptureAvailability.Failed })
        {
            var result = new CaptureDeviceCatalog(new DelegateBackend(b => new(b, expected, [], "reason")))
                .Refresh(CaptureDeviceKind.Video, true, ["v4l2"], TestContext.Current.CancellationToken);
            Assert.Equal(expected, result.Availability);
            Assert.Empty(result.Devices);
            Assert.Contains("reason", result.Reason);
        }
        var missing = new CaptureDeviceCatalog(new DelegateBackend(_ => throw new EntryPointNotFoundException("avdevice")))
            .Refresh(CaptureDeviceKind.Video, true, ["v4l2"], TestContext.Current.CancellationToken);
        Assert.Equal(CaptureAvailability.Unavailable, missing.Availability);
    }

    [Fact]
    public void Native_identity_kind_and_backend_make_unique_immutable_revisioned_snapshots()
    {
        CaptureBackendDevice[] rows =
        [
            new("native1", "Same label", [CaptureDeviceKind.Video]),
            new("native2", "Same label", [CaptureDeviceKind.Video]),
            new("native1", "Duplicate row", [CaptureDeviceKind.Video]),
            new("audio", "Same label", [CaptureDeviceKind.Audio])
        ];
        var catalog = new CaptureDeviceCatalog(new DelegateBackend(b => new(b, CaptureAvailability.Available, rows, null)));
        var first = catalog.Refresh(CaptureDeviceKind.Video, true, ["dshow"], TestContext.Current.CancellationToken);
        var second = catalog.Refresh(CaptureDeviceKind.Video, true, ["dshow"], TestContext.Current.CancellationToken);
        Assert.Equal(2, first.Devices.Count);
        Assert.NotEqual(first.Devices[0].Id, first.Devices[1].Id);
        Assert.StartsWith("video=", first.Devices[0].Input);
        Assert.True(second.Epoch > first.Epoch);
        Assert.Throws<NotSupportedException>(() => ((IList<CaptureDeviceInfo>)first.Devices).Clear());
        rows[0] = new("replaced", "Changed", []);
        Assert.Contains("native1", first.Devices[0].Id);
    }

    [Fact]
    public void Cancellation_is_observed_after_native_call_has_drained()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        bool drained = false;
        var catalog = new CaptureDeviceCatalog(new DelegateBackend(b =>
        {
            cts.Cancel();
            drained = true;
            return new(b, CaptureAvailability.Available, [], null);
        }));
        Assert.Throws<OperationCanceledException>(() => catalog.Refresh(CaptureDeviceKind.Video, true, ["v4l2"], cts.Token));
        Assert.True(drained);
    }

    [Fact]
    public void Host_supplied_device_constructor_keeps_default_open_without_eager_native_query()
    {
        var device = new CaptureDeviceInfo("native", "Camera", "v4l2", "/dev/video0", CaptureDeviceKind.Video);
        var compatibility = new VideoDevice(device.Label, device.Id, device.Url, Array.AsReadOnly(Array.Empty<VideoDeviceStream>()));
        Assert.Equal(device.Url, compatibility.Url);
        Assert.Empty(compatibility.Streams);
    }

    [Theory]
    [InlineData("@device_pnp_\\\\?\\usb#vid_1234&pid_5678#native", "\\\\?\\usb#vid_1234&pid_5678#native")]
    [InlineData("@device_pnp_\\\\?\\usb#device_name", "\\\\?\\usb#device:name")]
    public void Windows_format_matching_uses_native_moniker_not_friendly_label(string dshow, string mf)
        => Assert.Equal(CaptureFormats.NormalizeDshowIdentity(dshow), CaptureFormats.NormalizeDshowIdentity(mf));

    private sealed class Backend : ICaptureDeviceBackend
    {
        public int Calls { get; private set; }
        public CaptureBackendResult Enumerate(string backend)
        {
            Calls++;
            return new(backend, CaptureAvailability.Available,
                [new(backend == "v4l2" ? "/dev/video0" : "unknown", "Camera", [])], null);
        }
    }

    private sealed class DelegateBackend(Func<string, CaptureBackendResult> enumerate) : ICaptureDeviceBackend
    {
        public CaptureBackendResult Enumerate(string backend) => enumerate(backend);
    }
}

public class CaptureV4L2FormatTests
{
    private static CaptureDeviceInfo Camera => new("native", "Camera", "v4l2", "/dev/video0", CaptureDeviceKind.Video);

    [Fact]
    public void Kernel_x64_uapi_sizes_offsets_and_ioctl_numbers_are_exact()
    {
        Assert.Equal(64, Marshal.SizeOf<LinuxCaptureFormats.FormatDescription>());
        Assert.Equal(44, Marshal.SizeOf<LinuxCaptureFormats.FrameSize>());
        Assert.Equal(52, Marshal.SizeOf<LinuxCaptureFormats.FrameInterval>());
        Assert.Equal(44, Marshal.OffsetOf<LinuxCaptureFormats.FormatDescription>(nameof(LinuxCaptureFormats.FormatDescription.PixelFormat)).ToInt32());
        Assert.Equal(12, Marshal.OffsetOf<LinuxCaptureFormats.FrameSize>(nameof(LinuxCaptureFormats.FrameSize.Width)).ToInt32());
        Assert.Equal(20, Marshal.OffsetOf<LinuxCaptureFormats.FrameInterval>(nameof(LinuxCaptureFormats.FrameInterval.Numerator)).ToInt32());
        Assert.Equal(0xC0405602ul, LinuxCaptureFormats.EnumFormat);
        Assert.Equal(0xC02C564Aul, LinuxCaptureFormats.EnumFrameSizes);
        Assert.Equal(0xC034564Bul, LinuxCaptureFormats.EnumFrameIntervals);
    }

    [Fact]
    public void Discrete_interval_is_inverted_exactly_and_file_descriptor_zero_is_closed()
    {
        var api = new Ioctl();
        var result = LinuxCaptureFormats.Query(Camera, 17, TestContext.Current.CancellationToken, api);
        var format = Assert.Single(result.Formats);
        Assert.Equal(17, result.CatalogEpoch);
        Assert.Equal(30000, format.FrameRateNumerator);
        Assert.Equal(1001, format.FrameRateDenominator);
        Assert.Equal("nv12", format.PixelFormat);
        Assert.True(api.Closed);
    }

    [Theory]
    [InlineData(2u)]
    [InlineData(3u)]
    public void Continuous_and_stepwise_ranges_never_become_invented_discrete_choices(uint type)
    {
        var api = new Ioctl { SizeType = type };
        var result = LinuxCaptureFormats.Query(Camera, 1, TestContext.Current.CancellationToken, api);
        Assert.Equal(CaptureAvailability.Available, result.Availability);
        Assert.Empty(result.Formats);
        Assert.Contains("Default format", result.Reason);
        Assert.True(api.Closed);
    }

    [Fact]
    public void Native_permission_failure_is_unavailable_and_still_closes_open_descriptor()
    {
        var api = new Ioctl { FormatError = 13 };
        var result = LinuxCaptureFormats.Query(Camera, 1, TestContext.Current.CancellationToken, api);
        Assert.Equal(CaptureAvailability.Unavailable, result.Availability);
        Assert.Empty(result.Formats);
        Assert.Contains("errno 13", result.Reason);
        Assert.True(api.Closed);
    }

    [Fact]
    public void Cancellation_after_ioctl_returns_drains_and_closes_descriptor()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var api = new Ioctl { AfterFormat = cts.Cancel };
        Assert.Throws<OperationCanceledException>(() => LinuxCaptureFormats.Query(Camera, 1, cts.Token, api));
        Assert.True(api.Closed);
    }

    private sealed class Ioctl : LinuxCaptureFormats.ICaptureIoctl
    {
        public int ErrorNumber { get; private set; } = 22;
        public uint SizeType { get; init; } = 1;
        public int FormatError { get; init; }
        public Action? AfterFormat { get; init; }
        public bool Closed { get; private set; }
        public int OpenDevice(string path) => 0;
        public void CloseDevice(int descriptor) { Assert.Equal(0, descriptor); Closed = true; }
        public int GetFormat(int descriptor, ref LinuxCaptureFormats.FormatDescription value)
        {
            if (FormatError != 0) { ErrorNumber = FormatError; return -1; }
            if (value.Index != 0) { ErrorNumber = 22; return -1; }
            value.PixelFormat = 0x3231564E;
            AfterFormat?.Invoke();
            return 0;
        }
        public int GetSize(int descriptor, ref LinuxCaptureFormats.FrameSize value)
        {
            if (value.Index != 0) { ErrorNumber = 22; return -1; }
            value.Type = SizeType;
            value.Width = 640;
            value.Height = 480;
            return 0;
        }
        public int GetInterval(int descriptor, ref LinuxCaptureFormats.FrameInterval value)
        {
            if (value.Index != 0) { ErrorNumber = 22; return -1; }
            value.Type = 1;
            value.Numerator = 1001;
            value.Denominator = 30000;
            return 0;
        }
    }
}
