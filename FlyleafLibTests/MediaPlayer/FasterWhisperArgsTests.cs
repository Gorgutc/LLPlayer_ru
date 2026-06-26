using AwesomeAssertions;
using FlyleafLib.MediaPlayer;

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
