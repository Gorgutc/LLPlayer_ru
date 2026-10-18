using AwesomeAssertions;
using FlyleafLib.MediaPlayer;

namespace FlyleafLib.Platform;

public class PortableOcrTests
{
    [Theory]
    [InlineData("en", "eng")]
    [InlineData("ru", "rus")]
    [InlineData("nb", "nor")]
    public void Initialization_MapsInstalledModelsWithoutEnglishPrerequisite(string language, string model)
    {
        using FakeTesseract native = new() { Languages = [model] };
        using TesseractOCRService service = Create(native);
        service.TryInitialize(Language.Get(language), out string error).Should().BeTrue(error);
        native.Initializations.Should().Equal("", model);
        native.PageSegmentation.Should().Be(3);
        native.LiveHandles.Should().ContainSingle();
    }

    [Fact]
    public void Chinese_UsesConfiguredInstalledVariant_AndCjkSpacing()
    {
        using FakeTesseract native = new() { Languages = ["chi_sim", "chi_tra"] };
        Config config = new(true);
        config.Subtitles.TesseractOcrRegions["zh"] = "chi_tra";
        using TesseractOCRService service = new(config.Subtitles, () => native, () => null);
        service.TryInitialize(Language.Get("zh"), out string error).Should().BeTrue(error);
        native.Initializations.Should().Equal("", "chi_tra");
        native.Variables.Should().Contain("preserve_interword_spaces=1");
    }

    [Fact]
    public void Chinese_UnavailableConfiguredVariant_FallsBackToInstalledModel()
    {
        using FakeTesseract native = new() { Languages = ["chi_sim"] };
        Config config = new(true);
        config.Subtitles.TesseractOcrRegions["zh"] = "chi_tra";
        using TesseractOCRService service = new(config.Subtitles, () => native, () => null);
        service.TryInitialize(Language.Get("zh"), out string error).Should().BeTrue(error);
        native.Initializations.Should().Equal("", "chi_sim");
    }

    [Fact]
    public void MissingLanguage_FailsWithoutLeakingDiscoveryEngine()
    {
        using FakeTesseract native = new() { Languages = ["eng"] };
        using TesseractOCRService service = Create(native);
        service.TryInitialize(Language.Get("ru"), out string error).Should().BeFalse();
        error.Should().Contain("rus").And.Contain("traineddata");
        native.LiveHandles.Should().BeEmpty();
    }

    [Fact]
    public void DiscoveryFailure_DisposesItsHandle_AndCanRetry()
    {
        using FakeTesseract native = new() { FailLanguage = "" };
        using TesseractOCRService service = Create(native);
        service.TryInitialize(Language.English, out string error).Should().BeFalse();
        error.Should().Contain("discovery");
        native.LiveHandles.Should().BeEmpty();
        native.FailLanguage = null;
        service.TryInitialize(Language.English, out error).Should().BeTrue(error);
    }

    [Fact]
    public async Task SeparateServices_KeepLanguageAndNativeLifetimeIsolated()
    {
        using FakeTesseract first = new(), second = new();
        using TesseractOCRService primary = Create(first), secondary = Create(second);
        primary.TryInitialize(Language.English, out _).Should().BeTrue();
        secondary.TryInitialize(Language.Get("ru"), out _).Should().BeTrue();
        secondary.Dispose();
        first.LiveHandles.Should().ContainSingle();
        (await primary.RecognizeTextAsync(Pixel())).Should().Be("text\n");
        primary.PostProcess("|").Should().Be("I");
        first.DisposeCalls.Should().Be(0);
        second.LiveHandles.Should().BeEmpty();
    }

    [Fact]
    public void MissingLibrary_FailsWithInstallationAdvice()
    {
        using TesseractOCRService service = new(new Config(true).Subtitles,
            () => throw new DllNotFoundException("libtesseract.so.5"), () => null);
        service.TryInitialize(Language.English, out string error).Should().BeFalse();
        error.Should().Contain("libtesseract.so.5").And.Contain("install");
    }

    [Fact]
    public void InvalidExplicitTessdata_DoesNotFallBackOrCreateDirectory()
    {
        string missing = Path.Combine(Path.GetTempPath(), "llp-missing-ocr-" + Guid.NewGuid());
        using FakeTesseract native = new();
        using TesseractOCRService service = new(new Config(true).Subtitles, () => native, () => missing);
        service.TryInitialize(Language.English, out string error).Should().BeFalse();
        error.Should().Contain("TESSDATA_PREFIX");
        Directory.Exists(missing).Should().BeFalse();
        native.Initializations.Should().BeEmpty();
    }

    [Fact]
    public void ExistingRelativeTessdata_IsNormalizedWithoutChangingWorkingDirectory()
    {
        string original = Environment.CurrentDirectory;
        string directory = Path.Combine(Path.GetTempPath(), "llp-relative-ocr-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            string relative = Path.GetRelativePath(original, directory);
            using FakeTesseract native = new();
            using TesseractOCRService service = new(new Config(true).Subtitles, () => native, () => relative);
            service.TryInitialize(Language.English, out string error).Should().BeTrue(error);
            native.InitializationPaths[0].Should().Be(directory);
            Environment.CurrentDirectory.Should().Be(original);
        }
        finally { Directory.Delete(directory); }
    }

    [Fact]
    public async Task FailedReplacement_KeepsOldEngine_AndSuccessfulReplacementDisposesIt()
    {
        using FakeTesseract native = new();
        using TesseractOCRService service = Create(native);
        service.TryInitialize(Language.English, out _).Should().BeTrue();
        nint original = native.LiveHandles.Single();
        native.FailLanguage = "rus";
        service.TryInitialize(Language.Get("ru"), out string error).Should().BeFalse();
        error.Should().Contain("rus").And.Contain("corrupt");
        native.LiveHandles.Should().Equal(original);
        (await service.RecognizeTextAsync(Pixel())).Should().Be("text\n");
        native.FailLanguage = null;
        service.TryInitialize(Language.Get("ru"), out error).Should().BeTrue(error);
        native.LiveHandles.Should().ContainSingle().Which.Should().NotBe(original);
        native.Deleted.Should().Contain(original);
    }

    [Fact]
    public async Task RepeatedRecognition_ReusesEngine_AndConvertsBgraToRgb()
    {
        using FakeTesseract native = new();
        using TesseractOCRService service = Create(native);
        service.TryInitialize(Language.English, out _).Should().BeTrue();
        nint handle = native.LiveHandles.Single();
        BgraBitmap bitmap = new([10, 20, 30, 255, 0, 0, 0, 255, 255, 255, 255, 255], 3, 1);
        await service.RecognizeTextAsync(bitmap);
        await service.RecognizeTextAsync(bitmap);
        native.Images.Should().HaveCount(2);
        native.Images[0].Should().Equal(30, 20, 10, 0, 0, 0, 255, 255, 255);
        native.RecognitionHandles.Should().Equal(handle, handle);
        native.Initializations.Should().Equal("", "eng");
    }

    [Fact]
    public async Task RecognitionFailure_LeavesResidentEngineUsable()
    {
        using FakeTesseract native = new();
        using TesseractOCRService service = Create(native);
        service.TryInitialize(Language.English, out _).Should().BeTrue();
        native.FailRecognition = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RecognizeTextAsync(Pixel()));
        native.FailRecognition = false;
        (await service.RecognizeTextAsync(Pixel())).Should().Be("text\n");
        native.LiveHandles.Should().ContainSingle();
    }

    [Fact]
    public async Task Dispose_IsIdempotent_AndRejectsFurtherRecognitionOrInitialization()
    {
        using FakeTesseract native = new();
        TesseractOCRService service = Create(native);
        service.TryInitialize(Language.English, out _).Should().BeTrue();
        service.Dispose();
        service.Dispose();
        native.LiveHandles.Should().BeEmpty();
        native.DisposeCalls.Should().Be(1);
        service.TryInitialize(Language.English, out string error).Should().BeFalse();
        error.Should().Contain("disposed");
        await Assert.ThrowsAsync<ObjectDisposedException>(() => service.RecognizeTextAsync(Pixel()));
    }

    [Fact]
    public async Task InvalidImage_IsRejectedBeforeNativeRecognition()
    {
        using FakeTesseract native = new();
        using TesseractOCRService service = Create(native);
        service.TryInitialize(Language.English, out _).Should().BeTrue();
        await Assert.ThrowsAsync<ArgumentException>(() => service.RecognizeTextAsync(new([], 0, 0)));
        native.Images.Should().BeEmpty();
    }

    [Fact]
    public void PostProcess_PreservesWindowsEnglishCorrectionsAndOtherLanguages()
    {
        using FakeTesseract native = new();
        using TesseractOCRService service = Create(native);
        service.TryInitialize(Language.English, out _).Should().BeTrue();
        service.PostProcess("  | I1t's ’'\n").Should().Be("I It's '");
        service.TryInitialize(Language.Get("ru"), out _).Should().BeTrue();
        service.PostProcess("  | I1t's ’'\n").Should().Be("| I1t's ’'");
    }

    static BgraBitmap Pixel() => new([0, 0, 0, 255], 1, 1);
    static TesseractOCRService Create(FakeTesseract native) => new(new Config(true).Subtitles, () => native, () => null);

    sealed class FakeTesseract : ITesseractNative
    {
        public string[] Languages = ["eng", "rus"];
        public string? FailLanguage;
        public bool FailRecognition;
        public HashSet<nint> LiveHandles = [];
        public List<nint> Deleted = [];
        public List<string> Initializations = [];
        public List<string?> InitializationPaths = [];
        public List<string> Variables = [];
        public List<byte[]> Images = [];
        public List<nint> RecognitionHandles = [];
        public int PageSegmentation;
        public int DisposeCalls;
        nint next;
        public nint Create() { nint handle = ++next; LiveHandles.Add(handle); return handle; }
        public void Delete(nint handle) { Assert.True(LiveHandles.Remove(handle)); Deleted.Add(handle); }
        public int Initialize(nint handle, string? path, string language) { Initializations.Add(language); InitializationPaths.Add(path); return language == FailLanguage ? -1 : 0; }
        public string[] AvailableLanguages(nint handle) => Languages;
        public string DataPath(nint handle) => "/system/tessdata";
        public bool SetVariable(nint handle, string key, string value) { Variables.Add(key + "=" + value); return true; }
        public void SetPageSegMode(nint handle, int mode) => PageSegmentation = mode;
        public string Recognize(nint handle, byte[] rgb, int width, int height)
        {
            Assert.Contains(handle, LiveHandles);
            Images.Add(rgb);
            RecognitionHandles.Add(handle);
            if (FailRecognition) throw new InvalidOperationException("native recognition failed");
            return "text\n";
        }
        public void Dispose() => DisposeCalls++;
    }
}
