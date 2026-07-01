using AwesomeAssertions;

namespace FlyleafLib;

// Utils.FindNextAvailableFile picks the next free "name (N).ext" beside an existing file. Expectations are
// derived from the code (Utils.cs): unchanged path when the file does not exist; a trailing " (N)" on the
// input's stem is stripped via regex before numbering; candidates are probed for N = 1..100; null when all
// 100 numbered slots are taken. Each test works in its own temp directory (real File.Exists probes).
public sealed class UtilsFindNextAvailableFileTests : IDisposable
{
    private readonly string _dir;

    public UtilsFindNextAvailableFileTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "llplayer-fnaf-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best-effort cleanup */ }
    }

    private string P(string name) => Path.Combine(_dir, name);

    private void Touch(string name) => File.WriteAllText(P(name), "");

    [Fact]
    public void MissingFile_ReturnsInputUnchanged()
    {
        Utils.FindNextAvailableFile(P("base.txt")).Should().Be(P("base.txt"));
    }

    [Fact]
    public void ExistingFile_ReturnsFirstNumberedCandidate()
    {
        Touch("base.txt");

        Utils.FindNextAvailableFile(P("base.txt")).Should().Be(P("base (1).txt"));
    }

    [Fact]
    public void ExistingFileAndTakenSlots_SkipsToFirstFreeNumber()
    {
        Touch("base.txt");
        Touch("base (1).txt");
        Touch("base (2).txt");

        Utils.FindNextAvailableFile(P("base.txt")).Should().Be(P("base (3).txt"));
    }

    [Fact]
    public void NumberedInput_StripsSuffixBeforeNumbering()
    {
        // Regex strips the trailing " (2)" from the stem: candidates restart at "base (1)", NOT "base (2) (1)".
        Touch("base (2).txt");

        Utils.FindNextAvailableFile(P("base (2).txt")).Should().Be(P("base (1).txt"));
    }

    [Theory]
    [InlineData("movie.mkv", "movie (1).mkv")]
    [InlineData("README", "README (1)")] // extensionless: GetExtension returns "" and is appended verbatim
    public void PreservesExtension(string existing, string expected)
    {
        Touch(existing);

        Utils.FindNextAvailableFile(P(existing)).Should().Be(P(expected));
    }

    [Fact]
    public void NinetyNineSlotsTaken_UsesHundredthSlot()
    {
        // Pins the lower side of the 1..100 boundary: slot 100 must actually be probed — a silent narrowing
        // of the loop to 99 slots would still pass the all-taken → null test below.
        Touch("b.txt");
        for (int i = 1; i <= 99; i++)
            Touch($"b ({i}).txt");

        Utils.FindNextAvailableFile(P("b.txt")).Should().Be(P("b (100).txt"));
    }

    [Fact]
    public void AllHundredSlotsTaken_ReturnsNull()
    {
        Touch("b.txt");
        for (int i = 1; i <= 100; i++)
            Touch($"b ({i}).txt");

        Utils.FindNextAvailableFile(P("b.txt")).Should().BeNull();
    }
}
