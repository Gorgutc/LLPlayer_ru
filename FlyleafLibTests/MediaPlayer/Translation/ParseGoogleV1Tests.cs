using AwesomeAssertions;
using FlyleafLib.MediaPlayer.Translation.Services;

namespace FlyleafLib;

public class ParseGoogleV1Tests
{
    private const TranslateServiceType Svc = TranslateServiceType.GoogleV1;

    [Fact]
    public void ParseGoogleV1_ConcatenatesSegmentsWithoutAddingSeparators()
    {
        // [ [ ["Hello ","src"], ["world","src"] ] ]
        string json = "[[[\"Hello \",\"x\"],[\"world\",\"y\"]]]";
        GoogleV1TranslateService.ParseGoogleV1(json, "orig", Svc).Should().Be("Hello world");
    }

    [Fact]
    public void ParseGoogleV1_SkipsNonStringChunks()
    {
        string json = "[[[null,\"x\"],[\"Bonjour\",\"y\"]]]";
        GoogleV1TranslateService.ParseGoogleV1(json, "orig", Svc).Should().Be("Bonjour");
    }

    [Fact]
    public void ParseGoogleV1_ReturnsOriginal_WhenNothingTranslatable()
    {
        // root is an array but root[0] is not an array.
        string json = "[\"\",null]";
        GoogleV1TranslateService.ParseGoogleV1(json, "keep me", Svc).Should().Be("keep me");
    }

    [Theory]
    [InlineData("{}")]   // root is an object, not an array
    [InlineData("[]")]   // empty array
    public void ParseGoogleV1_Throws_OnUnexpectedRootShape(string json)
    {
        Action act = () => GoogleV1TranslateService.ParseGoogleV1(json, "orig", Svc);
        act.Should().Throw<TranslationException>();
    }
}
