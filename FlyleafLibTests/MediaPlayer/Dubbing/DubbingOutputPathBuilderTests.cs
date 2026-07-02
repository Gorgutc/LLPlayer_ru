using AwesomeAssertions;
using FlyleafLib.MediaPlayer.Dubbing;

namespace FlyleafLib.MediaPlayer;

public class DubbingOutputPathBuilderTests
{
    [Fact]
    public void BuildRussianDubPath_DefaultExtension_IsFlacBesideMedia()
    {
        string media = Path.Combine("C:", "videos", "movie.mkv");
        string dub = DubbingOutputPathBuilder.BuildRussianDubPath(media);

        dub.Should().Be(Path.Combine("C:", "videos", "movie.ru.dub.flac"));
    }

    [Theory]
    [InlineData("m4a", "movie.ru.dub.m4a")]
    [InlineData(".M4A", "movie.ru.dub.m4a")]
    [InlineData("  opus ", "movie.ru.dub.opus")]
    public void BuildRussianDubPath_NormalizesExtension(string ext, string expectedName)
    {
        string media = Path.Combine(Path.GetTempPath(), "movie.mkv");
        string dub = DubbingOutputPathBuilder.BuildRussianDubPath(media, ext);

        Path.GetFileName(dub).Should().Be(expectedName);
    }

    [Fact]
    public void OutputExists_NonEmptyFile_True_EmptyOrMissing_False()
    {
        string dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string nonEmpty = Path.Combine(dir, "a.flac");
            string empty = Path.Combine(dir, "b.flac");
            string missing = Path.Combine(dir, "c.flac");
            File.WriteAllText(nonEmpty, "data");
            File.WriteAllText(empty, "");

            DubbingOutputPathBuilder.OutputExists(nonEmpty).Should().BeTrue();
            DubbingOutputPathBuilder.OutputExists(empty).Should().BeFalse();
            DubbingOutputPathBuilder.OutputExists(missing).Should().BeFalse();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void DubExists_DerivesFromMediaPath()
    {
        string dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string media = Path.Combine(dir, "movie.mkv");
            File.WriteAllText(media, "video");
            DubbingOutputPathBuilder.DubExists(media).Should().BeFalse();

            File.WriteAllText(DubbingOutputPathBuilder.BuildRussianDubPath(media), "dub");
            DubbingOutputPathBuilder.DubExists(media).Should().BeTrue();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ResolveExistingRussianDubPath_FindsAnyNonEmptyDubExtension()
    {
        string dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string media = Path.Combine(dir, "movie.mkv");
            string dub = Path.Combine(dir, "movie.ru.dub.opus");
            File.WriteAllText(media, "video");
            File.WriteAllText(dub, "dub");

            DubbingOutputPathBuilder.ResolveExistingRussianDubPath(media).Should().Be(dub);
            DubbingOutputPathBuilder.DubExistsAnyFormat(media).Should().BeTrue();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ResolveExistingRussianDubPath_SkipsCrashOrphanedTempFragment()
    {
        // HC-04: a killed sidecar leaves a truncated "<out>.part" that the "movie.ru.dub.*" glob still matches.
        // It must NOT be mistaken for a finished dub (which would block re-render / auto-attach a stub).
        string dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string media = Path.Combine(dir, "movie.mkv");
            string orphan = Path.Combine(dir, "movie.ru.dub.flac.part");
            File.WriteAllText(media, "video");
            File.WriteAllText(orphan, "truncated");

            DubbingOutputPathBuilder.ResolveExistingRussianDubPath(media).Should().BeNull();
            DubbingOutputPathBuilder.DubExistsAnyFormat(media).Should().BeFalse();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
