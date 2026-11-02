using LLPlayer.Avalonia.Services;

namespace LLPlayer.Avalonia.Tests;

public class EngineLogOutputTests
{
    [Theory]
    [InlineData("")]
    [InlineData(":console")]
    [InlineData(":debug")]
    [InlineData(":custom")]
    public void Non_file_log_outputs_do_not_request_a_filesystem_directory(string output)
        => Assert.Null(EngineBootstrap.GetLogDirectory(output));

    [Fact]
    public void Bare_log_filename_uses_the_current_directory_and_path_resolution_does_not_create_directories()
    {
        Assert.Equal(Path.GetFullPath(Directory.GetCurrentDirectory()), EngineBootstrap.GetLogDirectory("player.log"));
        using TempDir temporary = new();
        string directory = Path.Combine(temporary.Path, "not-created");
        Assert.Equal(directory, EngineBootstrap.GetLogDirectory(Path.Combine(directory, "player.log")));
        Assert.False(Directory.Exists(directory));
    }
}
