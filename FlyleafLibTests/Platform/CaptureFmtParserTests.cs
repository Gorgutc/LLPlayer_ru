using FlyleafLib.MediaFramework.MediaDevice;

namespace FlyleafLib.Platform;

public class CaptureFmtParserTests
{
    [Theory]
    [InlineData("capturefmt://d/sh/ow?video%3Dcamera")]
    [InlineData("capturefmt://dshow/?video%3Dcamera")]
    [InlineData("capturefmt:///dshow?video%3Dcamera")]
    [InlineData("capturefmt://?video%3Dcamera")]
    [InlineData("capturefmt://d%73how?video%3Dcamera")]
    [InlineData("capturefmt://dshow")]
    [InlineData("capturefmt://dshow?")]
    [InlineData("capturefmt://dshow?&framerate=30")]
    public void Malformed_capture_backend_or_empty_input_is_rejected_before_native_lookup(string url)
        => Assert.Throws<FormatException>(() => FmtInputUrl.Parse(url, out _, out _, out _));

    [Theory]
    [InlineData("capturefmt://dshow?camera%")]
    [InlineData("capturefmt://dshow?camera%2")]
    [InlineData("capturefmt://dshow?camera%GG")]
    [InlineData("capturefmt://dshow?camera%2G")]
    [InlineData("capturefmt://dshow?camera%00suffix")]
    [InlineData("capturefmt://dshow?camera\0suffix")]
    [InlineData("capturefmt://dshow?camera&pixel%=nv12")]
    [InlineData("capturefmt://dshow?camera&pixel%2=nv12")]
    [InlineData("capturefmt://dshow?camera&pixel%GG=nv12")]
    [InlineData("capturefmt://dshow?camera&pixel_format=nv12%")]
    [InlineData("capturefmt://dshow?camera&pixel_format=nv12%2")]
    [InlineData("capturefmt://dshow?camera&pixel_format=nv12%GG")]
    [InlineData("capturefmt://dshow?camera&pixel_format%00suffix=nv12")]
    [InlineData("capturefmt://dshow?camera&pixel_format=nv12%00suffix")]
    [InlineData("capturefmt://dshow?camera&pixel_format\0suffix=nv12")]
    [InlineData("capturefmt://dshow?camera&pixel_format=nv12\0suffix")]
    public void Malformed_escapes_and_native_c_string_aliases_are_rejected(string url)
        => Assert.Throws<FormatException>(() => FmtInputUrl.Parse(url, out _, out _, out _));

    [Theory]
    [InlineData("capturefmt://dshow?camera&")]
    [InlineData("capturefmt://dshow?camera&&framerate=30")]
    [InlineData("capturefmt://dshow?camera&=30")]
    [InlineData("capturefmt://dshow?camera&framerate")]
    [InlineData("capturefmt://dshow?camera&framerate=30&framerate=60")]
    [InlineData("capturefmt://dshow?camera&framerate=30&%66ramerate=60")]
    public void Malformed_or_duplicate_capture_options_cannot_silently_change_native_options(string url)
        => Assert.Throws<FormatException>(() => FmtInputUrl.Parse(url, out _, out _, out _));

    [Theory]
    [InlineData("video=Камера 1 & input?=100%:α.srt")]
    [InlineData("/dev/камера.&?=100%:α")]
    public void Builder_produced_input_and_options_round_trip_without_reinterpreting_punctuation(string input)
    {
        const string pixelFormat = "формат & ? = 100%:α.nv12";
        var device = new CaptureDeviceInfo("native", "Friendly", "rawvideo", input, CaptureDeviceKind.Video);
        var format = new CaptureVideoFormatInfo("format", device.Id, 1920, 1080, 30000, 1001, pixelFormat, null);

        string url = CaptureInputUrl.Build(device, format);
        FmtInputUrl.Parse(url, out string backend, out string? actualInput, out var options);

        Assert.Equal("rawvideo", backend);
        Assert.Equal(input, actualInput);
        Assert.Equal("", Utils.GetUrlExtention(url));
        Assert.Equal("1920x1080", options!["video_size"]);
        Assert.Equal("30000/1001", options["framerate"]);
        Assert.Equal(pixelFormat, options["pixel_format"]);
    }

    [Fact]
    public void Valid_option_keys_and_values_are_decoded_once_and_preserve_empty_values()
    {
        FmtInputUrl.Parse("capturefmt://rawvideo?camera%252Eraw&%D0%BA%D0%BB%D1%8E%D1%87%26%3F%3D%25%3A%2E=value%26%3F%3D%25%3A%2E&empty=",
            out _, out string? input, out var options);

        Assert.Equal("camera%2Eraw", input);
        Assert.Equal("value&?=%:.", options!["ключ&?=%:."]);
        Assert.Equal("", options["empty"]);
    }

    [Fact]
    public void Legacy_backend_cleanup_undecoded_escapes_and_duplicate_option_behavior_stay_unchanged()
    {
        FmtInputUrl.Parse("fmt://d/sh/ow?camera%GG&pixel%00=nv12%00&framerate=30&framerate=60",
            out string backend, out string? input, out var options);

        Assert.Equal("dshow", backend);
        Assert.Equal("camera%GG", input);
        Assert.Equal("nv12%00", options!["pixel%00"]);
        Assert.Equal("60", options["framerate"]);
        FmtInputUrl.Parse("device://gdigrab/?", out backend, out input, out options);
        Assert.Equal("gdigrab", backend);
        Assert.Equal("", input);
        Assert.Null(options);
    }
}
