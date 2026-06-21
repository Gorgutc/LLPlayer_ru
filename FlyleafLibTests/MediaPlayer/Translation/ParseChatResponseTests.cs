using AwesomeAssertions;
using FlyleafLib.MediaPlayer.Translation.Services;

namespace FlyleafLib;

public class ParseChatResponseTests
{
    private const TranslateServiceType Svc = TranslateServiceType.OpenAI;

    [Fact]
    public void ParseChatResponse_ReturnsTrimmedContent()
    {
        string json = "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"  Привет  \"},\"finish_reason\":\"stop\"}]}";
        OpenAIBaseTranslateService.ParseChatResponse(json, Svc, reasonStripRequired: false)
            .Should().Be("Привет");
    }

    [Fact]
    public void ParseChatResponse_StripsReasoning_WhenRequired()
    {
        string json = "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"<think>thinking</think>Привет\"},\"finish_reason\":\"stop\"}]}";
        OpenAIBaseTranslateService.ParseChatResponse(json, Svc, reasonStripRequired: true)
            .Should().Be("Привет");
    }

    [Theory]
    [InlineData("{\"choices\":[]}")]                                                                              // empty choices
    [InlineData("{}")]                                                                                            // no choices
    [InlineData("{\"choices\":[{}]}")]                                                                            // choice has no message -> null content
    [InlineData("{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"x\"},\"finish_reason\":\"length\"}]}")] // truncated
    [InlineData("{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"<think>no closing tag\"}}]}")]   // reasoning-only -> empty
    public void ParseChatResponse_Throws_OnBadResponse(string json)
    {
        Action act = () => OpenAIBaseTranslateService.ParseChatResponse(json, Svc, reasonStripRequired: true);
        act.Should().Throw<TranslationException>();
    }
}
