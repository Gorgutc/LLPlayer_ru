using AwesomeAssertions;
using FlyleafLib.MediaPlayer;
using System.Text.RegularExpressions;

namespace FlyleafLib;

public class FasterWhisperArgsTests
{
    [Fact]
    public void AntiHallucinationArgsFor_EmptyExtra_AddsAllFlags()
    {
        string args = FasterWhisperASRService.AntiHallucinationArgsFor("");

        args.Should().Contain("--condition_on_previous_text False");
        args.Should().Contain("--no_speech_threshold 0.4");
        args.Should().Contain("--vad_threshold 0.35");
    }

    [Fact]
    public void AntiHallucinationArgsFor_SkipsFlagAlreadySet_SpaceForm()
    {
        string args = FasterWhisperASRService.AntiHallucinationArgsFor("--device cuda --no_speech_threshold 0.2");

        args.Should().NotContain("--no_speech_threshold 0.4");  // user already set it → not duplicated
        args.Should().Contain("--condition_on_previous_text False");
        args.Should().Contain("--vad_threshold 0.35");
    }

    [Fact]
    public void AntiHallucinationArgsFor_SkipsFlagAlreadySet_EqualsForm()
    {
        string args = FasterWhisperASRService.AntiHallucinationArgsFor("--condition_on_previous_text=True");

        args.Should().NotContain("--condition_on_previous_text False");
    }

    [Fact]
    public void BuildCommand_AntiHallucinationOn_IncludesFlags_AndUserValueWins()
    {
        FasterWhisperConfig config = new()
        {
            AntiHallucination = true,
            ExtraArguments = "--device cuda --no_speech_threshold 0.2"
        };

        string args = FasterWhisperASRService.BuildCommand(config, new WhisperConfig()).Arguments;

        args.Should().Contain("--condition_on_previous_text False");
        args.Should().Contain("--vad_threshold 0.35");
        args.Should().NotContain("--no_speech_threshold 0.4");  // deduped against ExtraArguments
        args.Should().Contain("--no_speech_threshold 0.2");      // the user's explicit value is still present
    }

    [Fact]
    public void BuildCommand_AntiHallucinationOff_OmitsFlags()
    {
        FasterWhisperConfig config = new()
        {
            AntiHallucination = false,
            ExtraArguments = "--device cuda"
        };

        string args = FasterWhisperASRService.BuildCommand(config, new WhisperConfig()).Arguments;

        args.Should().NotContain("--condition_on_previous_text");
        args.Should().NotContain("--vad_threshold");
    }

    [Fact]
    public void BuildCommand_WordTimestampsOn_AddsJsonOutputAndWordTimestampsFlag()
    {
        FasterWhisperConfig config = new() { ExtraArguments = "--device cuda" };

        string args = FasterWhisperASRService.BuildCommand(config, new WhisperConfig(), wordTimestamps: true).Arguments;

        args.Should().Contain("--output_format");
        args.Should().Contain("srt");
        args.Should().Contain("json");
        args.Should().Contain("--word_timestamps");
        args.Should().Contain("True");
    }

    [Fact]
    public void BuildCommand_WordTimestampsOff_OmitsJsonOutputAndWordTimestampsFlag()
    {
        FasterWhisperConfig config = new() { ExtraArguments = "--device cuda" };

        string args = FasterWhisperASRService.BuildCommand(config, new WhisperConfig(), wordTimestamps: false).Arguments;

        args.Should().Contain("--output_format");
        args.Should().Contain("srt");
        args.Should().NotContain("json");
        args.Should().NotContain("--word_timestamps");
    }

    [Theory]
    [InlineData("--device cuda --compute_type float16", "--compute_type float32", "float16")]
    [InlineData("--device=cuda --compute_type=int8_float16", "--compute_type int8", "int8_float16")]
    public void BuildCommand_ForceCpu_RewritesDeviceAndGpuOnlyComputeType(
        string extraArguments,
        string expectedComputeType,
        string rejectedComputeType)
    {
        FasterWhisperConfig config = new() { ExtraArguments = extraArguments };

        string args = FasterWhisperASRService.BuildCommand(config, new WhisperConfig(), forceCpu: true).Arguments;

        AssertUnquotedFlagValue(args, "--device", "cpu", expectedCount: 1);
        AssertUnquotedFlagValue(args, "--device", "cuda", expectedCount: 0);
        AssertUnquotedArgument(args, expectedComputeType, expectedCount: 1);
        AssertUnquotedFlagValue(args, "--compute_type", rejectedComputeType, expectedCount: 0);
    }

    [Fact]
    public void BuildCommand_ForceCpu_AddsExactlyOneDeviceWhenMissing()
    {
        FasterWhisperConfig config = new() { ExtraArguments = "--compute_type float16" };

        string args = FasterWhisperASRService.BuildCommand(config, new WhisperConfig(), forceCpu: true).Arguments;

        AssertUnquotedFlagValue(args, "--device", "cpu", expectedCount: 1);
        AssertUnquotedFlagValue(args, "--compute_type", "float32", expectedCount: 1);
    }

    [Fact]
    public void BuildCommand_ForceCpu_PreservesQuotedFlagText()
    {
        const string prompt = "keep --device cuda and --compute_type float16 verbatim";
        FasterWhisperConfig config = new()
        {
            Prompt = prompt,
            ExtraArguments = "--device=cuda --compute_type=int8_float16"
        };

        string args = FasterWhisperASRService.BuildCommand(config, new WhisperConfig(), forceCpu: true).Arguments;

        args.Should().Contain($"--initial_prompt \"{prompt}\"");
        AssertUnquotedFlagValue(args, "--device", "cpu", expectedCount: 1);
        AssertUnquotedFlagValue(args, "--compute_type", "int8", expectedCount: 1);
        AssertUnquotedFlagValue(args, "--device", "cuda", expectedCount: 0);
        AssertUnquotedFlagValue(args, "--compute_type", "int8_float16", expectedCount: 0);
    }

    [Fact]
    public void BuildCommand_PromptSet_AddsInitialPrompt()
    {
        // F-17/F-18: a first-class initial_prompt biases language/script and casing at the source.
        FasterWhisperConfig config = new()
        {
            Prompt = "Это пример русской речи.",
            ExtraArguments = "--device cuda"
        };

        string args = FasterWhisperASRService.BuildCommand(config, new WhisperConfig()).Arguments;

        args.Should().Contain("--initial_prompt");
        args.Should().Contain("Это пример русской речи.");
    }

    [Fact]
    public void BuildCommand_PromptEmpty_OmitsInitialPrompt()
    {
        FasterWhisperConfig config = new() { Prompt = "", ExtraArguments = "--device cuda" };

        string args = FasterWhisperASRService.BuildCommand(config, new WhisperConfig()).Arguments;

        args.Should().NotContain("--initial_prompt");
    }

    private static void AssertUnquotedArgument(string arguments, string expectedArgument, int expectedCount)
    {
        string[] parts = expectedArgument.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        parts.Should().HaveCount(2);
        AssertUnquotedFlagValue(arguments, parts[0], parts[1], expectedCount);
    }

    private static void AssertUnquotedFlagValue(string arguments, string flag, string value, int expectedCount)
    {
        string withoutQuotedArguments = Regex.Replace(arguments, "\"(?:\\\\.|[^\"\\\\])*\"", "\"\"");
        string pattern = $@"(?<!\S){Regex.Escape(flag)}(?:\s+|=){Regex.Escape(value)}(?=\s|$)";

        Regex.Matches(withoutQuotedArguments, pattern).Count.Should().Be(expectedCount);
    }

    [Fact]
    public void BuildCommand_PromptSet_ButExtraArgumentsAlreadyHasInitialPrompt_NotDuplicated()
    {
        FasterWhisperConfig config = new()
        {
            Prompt = "from the field",
            ExtraArguments = "--device cuda --initial_prompt \"explicit override\""
        };

        string args = FasterWhisperASRService.BuildCommand(config, new WhisperConfig()).Arguments;

        args.Should().NotContain("from the field"); // de-duped: explicit ExtraArguments value wins
        args.Should().Contain("explicit override");
    }
}
