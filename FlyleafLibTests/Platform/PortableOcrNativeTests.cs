using AwesomeAssertions;
using FlyleafLib.MediaPlayer;

namespace FlyleafLib.Platform;

/// <summary>Opt-in system-native checks, separate from the dependency-free Linux gate.</summary>
[Collection("PortableEngine")]
public class PortableOcrNativeTests
{
    static string RequireSystemData()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("LLPLAYER_TEST_OCR") == "1",
            "Set LLPLAYER_TEST_OCR=1 after installing system Tesseract 5 and eng/rus language packages.");
        using TesseractNative native = TesseractNative.Shared;
        nint discovery = native.Create();
        try
        {
            native.Initialize(discovery, null, "").Should().Be(0);
            native.AvailableLanguages(discovery).Should().Contain("rus").And.Contain("eng");
            return native.DataPath(discovery);
        }
        finally { native.Delete(discovery); }
    }

    [Fact]
    public async Task RussianOnlyData_InitializesWithoutEnglish_AndSupportsRepeatedPages()
    {
        string systemData = RequireSystemData();
        string directory = Path.Combine(Path.GetTempPath(), "llp-rus-ocr-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            File.Copy(Path.Combine(systemData, "rus.traineddata"), Path.Combine(directory, "rus.traineddata"));
            using TesseractOCRService service = new(new Config(true).Subtitles, () => TesseractNative.Shared, () => directory);
            service.TryInitialize(Language.Get("ru"), out string error).Should().BeTrue(error);
            byte[] white = new byte[200 * 80 * 4];
            Array.Fill(white, (byte)255);
            for (int i = 0; i < 3; i++)
                (await service.RecognizeTextAsync(new(white, 200, 80))).Trim().Should().BeEmpty();
            service.TryInitialize(Language.English, out error).Should().BeFalse();
            error.Should().Contain("eng");
            (await service.RecognizeTextAsync(new(white, 200, 80))).Trim().Should().BeEmpty();
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void CorruptTraineddata_IsNotReportedAsInitialized_AndCanRetryValidData()
    {
        string systemData = RequireSystemData();
        string directory = Path.Combine(Path.GetTempPath(), "llp-corrupt-ocr-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "rus.traineddata"), "corrupt fixture");
            using TesseractOCRService service = new(new Config(true).Subtitles, () => TesseractNative.Shared, () => directory);
            service.TryInitialize(Language.Get("ru"), out string error).Should().BeFalse();
            error.Should().Contain("corrupt").And.Contain("rus.traineddata");
            File.Copy(Path.Combine(systemData, "rus.traineddata"), Path.Combine(directory, "rus.traineddata"), true);
            service.TryInitialize(Language.Get("ru"), out error).Should().BeTrue(error);
        }
        finally { Directory.Delete(directory, true); }
    }
}

[Collection("PortableEngine")]
public class PortableOcrBitmapSmokeTests
{
    [Fact]
    public async Task Cancellation_WaitsForInFlightCue_ThenStopsBeforeNextCueAndReleasesBitmaps()
    {
        PortableVideoTestMedia.RequireEngine();
        var previousFactory = SubtitlesOCR.ServiceFactory;
        BlockingOcr service = new();
        using SubtitlesOCR ocr = new(new Config(true).Subtitles, 1);
        using SubtitleData first = new() { IsBitmap = true, Bitmap = NativeBitmap((1, 1, new byte[] { 0 })) };
        using SubtitleData second = new() { IsBitmap = true, Bitmap = NativeBitmap((1, 1, new byte[] { 0 })) };
        Task? work = null, cancel = null;
        try
        {
            SubtitlesOCR.ServiceFactory = (_, _) => service;
            ocr.TryInitialize(0, Language.English, out _).Should().BeTrue();
            work = Task.Run(() => ocr.Do(0, [first, second]), TestContext.Current.CancellationToken);
            await service.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            // Capture the existing private CTS only to synchronize at the real cancellation boundary;
            // assertions below concern cue output, image ownership and the public wait operation.
            var ctss = (CancellationTokenSource?[])typeof(SubtitlesOCR)
                .GetField("_ctss", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(ocr)!;
            TaskCompletionSource canceled = new(TaskCreationOptions.RunContinuationsAsynchronously);
            using var registration = ctss[0]!.Token.Register(() => canceled.TrySetResult());
            cancel = Task.Run(() => ocr.TryCancelWait(0), TestContext.Current.CancellationToken);
            await canceled.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            cancel.IsCompleted.Should().BeFalse("the in-flight native cue must finish before cancellation returns");
            service.Result.TrySetResult("first cue");
            await Task.WhenAll(work, cancel).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            service.Calls.Should().Be(1);
            first.Text.Should().Be("first cue");
            second.Text.Should().BeNull();
            first.Bitmap.Should().BeNull();
            second.Bitmap.Should().BeNull();
        }
        finally
        {
            service.Result.TrySetResult("first cue");
            try
            {
                if (work != null) await work.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
                if (cancel != null) await cancel.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
            }
            finally { SubtitlesOCR.ServiceFactory = previousFactory; }
        }
    }

    sealed class BlockingOcr : IOCRService
    {
        public TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<string> Result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls;
        public bool TryInitialize(Language lang, out string err) { err = ""; return true; }
        public Task<string> RecognizeTextAsync(BgraBitmap bitmap) { Calls++; Entered.TrySetResult(); return Result.Task; }
        public string PostProcess(string text) => text;
        public void Dispose() { }
    }

    static (int width, int height, byte[] pixels) LoadFixture(string name)
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("LLPLAYER_TEST_OCR") == "1", "Set LLPLAYER_TEST_OCR=1 to run system-native OCR smoke.");
        string? directory = Environment.GetEnvironmentVariable("LLPLAYER_OCR_FIXTURE_DIR");
        Assert.SkipWhen(string.IsNullOrEmpty(directory), "LLPLAYER_OCR_FIXTURE_DIR must contain english.pgm and russian.pgm (opaque black-on-white test text).");
        byte[] data = File.ReadAllBytes(Path.Combine(directory!, name + ".pgm"));
        int first = Array.IndexOf(data, (byte)'\n');
        int second = Array.IndexOf(data, (byte)'\n', first + 1);
        int third = Array.IndexOf(data, (byte)'\n', second + 1);
        System.Text.Encoding.ASCII.GetString(data, 0, first).Should().Be("P5");
        string[] dimensions = System.Text.Encoding.ASCII.GetString(data, first + 1, second - first - 1).Split(' ');
        int width = int.Parse(dimensions[0]), height = int.Parse(dimensions[1]);
        System.Text.Encoding.ASCII.GetString(data, second + 1, third - second - 1).Should().Be("255");
        byte[] pixels = data[(third + 1)..];
        pixels.Length.Should().Be(width * height);
        return (width, height, pixels);
    }

    static BgraBitmap Bgra((int width, int height, byte[] pixels) image)
    {
        byte[] data = new byte[image.width * image.height * 4];
        for (int i = 0; i < image.pixels.Length; i++)
        {
            data[i * 4] = data[i * 4 + 1] = data[i * 4 + 2] = image.pixels[i];
            data[i * 4 + 3] = 255;
        }
        return new(data, image.width, image.height);
    }

    static string Normalize(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    static void SaveResult(string name, string text)
    {
        string? directory = Environment.GetEnvironmentVariable("LLPLAYER_OCR_FIXTURE_DIR");
        File.WriteAllText(Path.Combine(directory!, name + "-result.txt"), text);
    }

    [Theory]
    [InlineData("english", "en", "HELLO WORLD LEARNING LANGUAGES")]
    [InlineData("russian", "ru", "ПРИВЕТ МИР ИЗУЧАЕМ ЯЗЫКИ")]
    public async Task SystemService_RecognizesActualMultilineBitmap_AndReusesEngine(string fixture, string language, string expected)
    {
        var image = LoadFixture(fixture);
        using TesseractOCRService service = new(new Config(true).Subtitles);
        service.TryInitialize(Language.Get(language), out string error).Should().BeTrue(error);
        BgraBitmap bitmap = Bgra(image);
        for (int i = 0; i < 3; i++)
        {
            string text = service.PostProcess(await service.RecognizeTextAsync(bitmap));
            SaveResult(fixture + "-service-" + i, text);
            Normalize(text).Should().Be(expected);
            text.Should().Contain("\n", "PSM_AUTO must preserve both subtitle lines");
        }
    }

    [Fact]
    public void BitmapPipeline_ProcessesTwoTracks_RefreshesWithoutReinit_AndPreservesCueTiming()
    {
        var english = LoadFixture("english");
        var russian = LoadFixture("russian");
        PortableVideoTestMedia.RequireEngine();
        var previousFactory = SubtitlesOCR.ServiceFactory;
        try
        {
            SubtitlesOCR.ServiceFactory = (config, engine) => engine == SubOCREngineType.Tesseract ? new TesseractOCRService(config) : null;
            Config config = new(true);
            using SubtitlesOCR ocr = new(config.Subtitles, 2);
            ocr.TryInitialize(0, Language.English, out string error).Should().BeTrue(error);
            ocr.TryInitialize(1, Language.Get("ru"), out error).Should().BeTrue(error);
            using TesseractOCRService direct = new(config.Subtitles);
            direct.TryInitialize(Language.English, out error).Should().BeTrue(error);
            using (SubtitleBitmapData bitmap = NativeBitmap(english))
            {
                string text = SubtitlesOCR.Process(direct, 0, bitmap);
                SaveResult("english-process", text);
                Normalize(text).Should().Be("HELLO WORLD LEARNING LANGUAGES");
            }
            for (int pass = 0; pass < 2; pass++)
            {
                RunTrack(ocr, 0, english, "HELLO WORLD LEARNING LANGUAGES", "english-pipeline-" + pass);
                RunTrack(ocr, 1, russian, "ПРИВЕТ МИР ИЗУЧАЕМ ЯЗЫКИ", "russian-pipeline-" + pass);
            }
            // A secondary reinit must not invalidate the primary resident native engine.
            ocr.TryInitialize(1, Language.Get("ru"), out error).Should().BeTrue(error);
            RunTrack(ocr, 0, english, "HELLO WORLD LEARNING LANGUAGES", "english-after-secondary-reinit");
            ocr.Dispose();
            ocr.Dispose();
        }
        finally { SubtitlesOCR.ServiceFactory = previousFactory; }
    }

    static void RunTrack(SubtitlesOCR ocr, int track, (int width, int height, byte[] pixels) image, string expected, string resultName)
    {
        using SubtitleData cue = new()
        {
            IsBitmap = true,
            Bitmap = NativeBitmap(image),
            StartTime = TimeSpan.FromMilliseconds(1250),
            EndTime = TimeSpan.FromMilliseconds(3750)
        };
        SubtitleBitmapData bitmap = cue.Bitmap;
        ocr.Do(track, [cue]);
        SaveResult(resultName, cue.Text!);
        Normalize(cue.Text!).Should().Be(expected);
        cue.StartTime.Should().Be(TimeSpan.FromMilliseconds(1250));
        cue.EndTime.Should().Be(TimeSpan.FromMilliseconds(3750));
        cue.Bitmap.Should().BeNull("successful OCR releases the native subtitle bitmap");
        Assert.Throws<InvalidOperationException>(() => bitmap.SubToBitmap(true));
    }

    static unsafe SubtitleBitmapData NativeBitmap((int width, int height, byte[] pixels) image)
    {
        // FFmpeg frees this entire paletted graph via avsubtitle_free, exactly like decoded bitmap subtitles.
        var rect = (Flyleaf.FFmpeg.AVSubtitleRect*)Flyleaf.FFmpeg.Raw.av_mallocz((nuint)sizeof(Flyleaf.FFmpeg.AVSubtitleRect));
        var rects = (Flyleaf.FFmpeg.AVSubtitleRect**)Flyleaf.FFmpeg.Raw.av_mallocz((nuint)sizeof(nint));
        rects[0] = rect;
        rect->w = image.width;
        rect->h = image.height;
        rect->nb_colors = 2;
        rect->linesize[0] = image.width;
        rect->data[0] = (nint)Flyleaf.FFmpeg.Raw.av_malloc((nuint)image.pixels.Length);
        rect->data[1] = (nint)Flyleaf.FFmpeg.Raw.av_mallocz(256 * sizeof(uint));
        // Source subtitle text is white on transparent black. Process inverts, composites and pads it.
        ((uint*)rect->data[1])[0] = 0;
        ((uint*)rect->data[1])[1] = 0xffffffff;
        for (int i = 0; i < image.pixels.Length; i++)
            ((byte*)rect->data[0])[i] = image.pixels[i] == 0 ? (byte)1 : (byte)0;
        return new(new Flyleaf.FFmpeg.AVSubtitle { num_rects = 1, rects = rects });
    }
}
