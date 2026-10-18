using AwesomeAssertions;
using FlyleafLib.MediaFramework.MediaRenderer;

namespace FlyleafLib.Platform;

public class WindowsSnapshotEncoderTests
{
    [Theory]
    [InlineData(0, 1, 4, 4)]
    [InlineData(1, 0, 4, 4)]
    [InlineData(-1, 1, 4, 4)]
    [InlineData(2, 2, 7, 16)]
    [InlineData(2, 2, 8, 15)]
    public void InvalidBgraGeometry_IsRejectedBeforeCreatingOutput(int width, int height, int stride, int length)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows built-in image encoding boundary.");
            return;
        }
        string file = Path.Combine(Path.GetTempPath(), $"llplayer-invalid-snapshot-{Guid.NewGuid():N}.png");
        Action save = () =>
        {
            if (OperatingSystem.IsWindows())
                WindowsSnapshotEncoder.Save(file, "png", new byte[length], width, height, stride);
        };

        save.Should().Throw<ArgumentException>();
        File.Exists(file).Should().BeFalse();
    }

    [Fact]
    public void OverflowingRowSize_IsRejectedBeforeCreatingOutput()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows built-in image encoding boundary.");
            return;
        }
        string file = Path.Combine(Path.GetTempPath(), $"llplayer-invalid-snapshot-{Guid.NewGuid():N}.png");
        Action save = () =>
        {
            if (OperatingSystem.IsWindows())
                WindowsSnapshotEncoder.Save(file, "png", new byte[4], int.MaxValue, 1, int.MaxValue);
        };

        save.Should().Throw<OverflowException>();
        File.Exists(file).Should().BeFalse();
    }

    [Fact]
    public void UnsupportedFormat_IsRejectedBeforeCreatingOutput()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows built-in image encoding boundary.");
            return;
        }
        string file = Path.Combine(Path.GetTempPath(), $"llplayer-invalid-snapshot-{Guid.NewGuid():N}.gif");
        Action save = () =>
        {
            if (OperatingSystem.IsWindows())
                WindowsSnapshotEncoder.Save(file, "gif", new byte[4], 1, 1, 4);
        };

        save.Should().Throw<ArgumentException>().WithParameterName("extension");
        File.Exists(file).Should().BeFalse();
    }
}
