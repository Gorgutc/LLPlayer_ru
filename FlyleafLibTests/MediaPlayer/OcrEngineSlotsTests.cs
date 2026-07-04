using System.Drawing;
using System.Threading.Tasks;
using AwesomeAssertions;

namespace FlyleafLib.MediaPlayer;

public class OcrEngineSlotsTests
{
    [Fact]
    public void Install_ReplacingSameTrack_DisposesPreviousEngine()
    {
        var slots = new OcrEngineSlots(2);
        var a = new FakeOcrService();
        var b = new FakeOcrService();

        slots.Install(0, a);
        slots.Install(0, b);

        a.DisposeCount.Should().Be(1); // previous engine disposed on re-init (fixes the native Tesseract leak)
        b.DisposeCount.Should().Be(0);
        slots.Get(0).Should().BeSameAs(b);
    }

    [Fact]
    public void Install_DifferentTracks_KeepsDistinctEnginesWithoutDisposing()
    {
        var slots = new OcrEngineSlots(2);
        var primary = new FakeOcrService();
        var secondary = new FakeOcrService();

        slots.Install(0, primary);
        slots.Install(1, secondary);

        primary.DisposeCount.Should().Be(0); // secondary-track init must not clobber/dispose the primary engine
        secondary.DisposeCount.Should().Be(0);
        slots.Get(0).Should().BeSameAs(primary);
        slots.Get(1).Should().BeSameAs(secondary);
    }

    [Fact]
    public void Get_BeforeAnyInstall_ReturnsNull()
    {
        new OcrEngineSlots(2).Get(0).Should().BeNull();
    }

    [Fact]
    public void Clear_DisposesAndClearsEngine_Idempotent()
    {
        var slots = new OcrEngineSlots(2);
        var a = new FakeOcrService();
        slots.Install(0, a);

        slots.Clear(0);
        a.DisposeCount.Should().Be(1); // engine freed on teardown
        slots.Get(0).Should().BeNull();

        slots.Clear(0); // idempotent — empty slot is a no-op
        a.DisposeCount.Should().Be(1);
    }

    private sealed class FakeOcrService : IOCRService
    {
        public int DisposeCount { get; private set; }

        public bool TryInitialize(Language lang, out string err)
        {
            err = "";
            return true;
        }

        public Task<string> RecognizeTextAsync(Bitmap bitmap) => Task.FromResult("");

        public string PostProcess(string text) => text;

        public void Dispose() => DisposeCount++;
    }
}
