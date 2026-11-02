using LLPlayer.Core.Services.Models;

namespace LLPlayer.Avalonia.Tests;

public class WindowsEngineArchiveExtractorTests
{
    [Theory]
    [InlineData("../escape.exe")]
    [InlineData("Faster-Whisper-XXL/../../escape.exe")]
    [InlineData("Faster-Whisper-XXL/file.exe:alternate")]
    [InlineData("Faster-Whisper-XXL/../outside.exe")]
    [InlineData("Faster-Whisper-XXL/ambiguous. ")]
    [InlineData("outside.exe")]
    [InlineData("C:\\outside.exe")]
    public void Unsafe_or_unexpected_paths_are_rejected_before_extraction(string entry)
    {
        Assert.Throws<IOException>(() => EngineArchivePolicy.ValidateEntryNames([entry], Path.GetTempPath()));
    }

    [Fact]
    public void Duplicate_and_case_colliding_entries_are_rejected()
    {
        Assert.Throws<IOException>(() => EngineArchivePolicy.ValidateEntryNames(
            ["Faster-Whisper-XXL/file.dll", "Faster-Whisper-XXL/FILE.dll"], Path.GetTempPath()));
    }

    [Fact]
    public void Expected_engine_layout_and_license_are_accepted_without_loading_native_code()
    {
        EngineArchivePolicy.ValidateEntryNames(
            ["Faster-Whisper-XXL/", "Faster-Whisper-XXL/faster-whisper-xxl.exe", "Faster-Whisper-XXL/lib/helper.dll", "license.txt"],
            Path.GetTempPath());
    }
}
