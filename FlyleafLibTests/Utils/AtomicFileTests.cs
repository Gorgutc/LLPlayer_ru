using System;
using System.IO;
using System.Text;
using AwesomeAssertions;

namespace FlyleafLib;

public class AtomicFileTests : IDisposable
{
    private readonly string _dir;

    public AtomicFileTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "llplayer-atomicfile-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch { /* best-effort test cleanup */ }
    }

    [Fact]
    public void WriteAllText_WritesContents()
    {
        string path = Path.Combine(_dir, "config.json");

        AtomicFile.WriteAllText(path, "{\"a\":1}");

        File.ReadAllText(path).Should().Be("{\"a\":1}");
    }

    [Fact]
    public void WriteAllText_OverwritesExistingFile()
    {
        string path = Path.Combine(_dir, "config.json");
        File.WriteAllText(path, "OLD CONTENT that is longer than the new one");

        AtomicFile.WriteAllText(path, "new");

        File.ReadAllText(path).Should().Be("new");
    }

    [Fact]
    public void WriteAllText_LeavesNoTempFileBehind()
    {
        string path = Path.Combine(_dir, "config.json");

        AtomicFile.WriteAllText(path, "payload");

        // Only the target survives — the staged temp is always cleaned up.
        Directory.GetFiles(_dir).Should().ContainSingle().Which.Should().Be(path);
        Directory.GetFiles(_dir, "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public void WriteAllText_ByteIdenticalToDirectWrite_Utf8NoBom()
    {
        // The three config Save sites previously used File.WriteAllText(path, json) (UTF-8, no BOM). The atomic
        // helper must produce byte-identical output so the persisted config format is unchanged.
        const string json = "{\"greeting\":\"Привет\",\"n\":1}";
        string atomicPath = Path.Combine(_dir, "atomic.json");
        string directPath = Path.Combine(_dir, "direct.json");

        AtomicFile.WriteAllText(atomicPath, json);
        File.WriteAllText(directPath, json);

        File.ReadAllBytes(atomicPath).Should().Equal(File.ReadAllBytes(directPath));
        File.ReadAllBytes(atomicPath)[0].Should().NotBe((byte)0xEF, "no UTF-8 BOM should be written");
    }

    [Fact]
    public void WriteAllText_HonorsExplicitEncoding()
    {
        string path = Path.Combine(_dir, "config.json");

        AtomicFile.WriteAllText(path, "A", new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        File.ReadAllBytes(path)[0].Should().Be((byte)0xEF, "the BOM-emitting encoding was honored");
    }

    [Fact]
    public void WriteAllText_WhenWriteFails_LeavesExistingFileIntact()
    {
        // RED-without-fix: this is the whole point of the change. A crash/failure mid-write must NOT corrupt the
        // existing config. A plain File.WriteAllText(path, ...) truncates the target BEFORE encoding, so a throwing
        // write would leave the file empty; the atomic helper writes to a temp, so the real file is untouched.
        string path = Path.Combine(_dir, "config.json");
        File.WriteAllText(path, "GOOD-EXISTING-CONFIG");

        Action act = () => AtomicFile.WriteAllText(path, "new payload", new ThrowingEncoding());

        act.Should().Throw<IOException>();
        File.ReadAllText(path).Should().Be("GOOD-EXISTING-CONFIG");
        Directory.GetFiles(_dir, "*.tmp").Should().BeEmpty();
    }

    /// <summary>Encoding that fails during byte conversion, simulating an interrupted write.</summary>
    private sealed class ThrowingEncoding : Encoding
    {
        public override int GetByteCount(char[] chars, int index, int count) => throw new IOException("boom");
        public override int GetBytes(char[] chars, int charIndex, int charCount, byte[] bytes, int byteIndex) => throw new IOException("boom");
        public override int GetCharCount(byte[] bytes, int index, int count) => throw new IOException("boom");
        public override int GetChars(byte[] bytes, int byteIndex, int byteCount, char[] chars, int charIndex) => throw new IOException("boom");
        public override int GetMaxByteCount(int charCount) => 16;
        public override int GetMaxCharCount(int byteCount) => 16;
    }
}
