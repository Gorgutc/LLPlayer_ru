using AwesomeAssertions;
using FlyleafLib.MediaPlayer.Batch;

namespace FlyleafLib.MediaPlayer;

public class SubtitleOutputPathBuilderTests
{
    [Fact]
    public void BuildRussianSrtPath_ShouldUseVideoDirectoryAndBaseName()
    {
        string videoPath = Path.Combine("C:\\Videos", "Lesson 01.mkv");

        string result = SubtitleOutputPathBuilder.BuildRussianSrtPath(videoPath);

        result.Should().Be(Path.Combine("C:\\Videos", "Lesson 01.ru.srt"));
    }

    [Fact]
    public void BatchVideoScanner_ShouldReturnVideoFilesInNaturalOrder()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "lesson 10.mkv"), "");
            File.WriteAllText(Path.Combine(tempDir, "lesson 2.mp4"), "");
            File.WriteAllText(Path.Combine(tempDir, "lesson 1.avi"), "");
            File.WriteAllText(Path.Combine(tempDir, "notes.txt"), "");

            var result = BatchVideoScanner.Scan(tempDir, recursive: false);

            result.Select(Path.GetFileName).Should().Equal(
                "lesson 1.avi",
                "lesson 2.mp4",
                "lesson 10.mkv");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
