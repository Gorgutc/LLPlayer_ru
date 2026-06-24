using AwesomeAssertions;
using FlyleafLib.MediaPlayer.Translation.Services;

namespace FlyleafLib;

public class ContextWindowPromptTests
{
    [Fact]
    public void BuildContextWindowUserMessage_PlacesFocalBetweenLabelledContext()
    {
        string msg = OpenAIBaseTranslateService.BuildContextWindowUserMessage(
            ["prev one", "prev two"], "focal line", ["next one"]);

        msg.Should().Contain("Context before:");
        msg.Should().Contain("Line to translate:");
        msg.Should().Contain("Context after:");
        msg.Should().Contain("focal line");

        // Ordering must be before < focal < after so the model reads the surrounding sentence in order.
        msg.IndexOf("prev two", StringComparison.Ordinal)
            .Should().BeLessThan(msg.IndexOf("focal line", StringComparison.Ordinal));
        msg.IndexOf("focal line", StringComparison.Ordinal)
            .Should().BeLessThan(msg.IndexOf("next one", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildContextWindowUserMessage_RendersEmptySidesAsNone()
    {
        string msg = OpenAIBaseTranslateService.BuildContextWindowUserMessage([], "only line", []);

        msg.Should().Contain("Context before:\n(none)");
        msg.Should().Contain("Context after:\n(none)");
        msg.Should().Contain("only line");
    }

    [Fact]
    public void BuildContextWindowUserMessage_KeepsEachContextLineOnItsOwnLine()
    {
        string msg = OpenAIBaseTranslateService.BuildContextWindowUserMessage(
            ["a", "b", "c"], "focal", []);

        msg.Should().Contain("a\nb\nc\n");
    }
}
