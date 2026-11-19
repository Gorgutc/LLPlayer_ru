using System.Text.Json;
using FlyleafLib;
using FlyleafLib.MediaPlayer;
using FlyleafLib.MediaPlayer.AI;
using LLPlayer.ControlApi;
using LLPlayer.Core.Automation;

namespace FlyleafLibTests.Automation;

public sealed class ControlInsightsTests
{
    [Fact]
    public async Task Generate_uses_only_the_selected_revision_and_translated_lines()
    {
        string? prompt = null;
        var insights = new ControlInsightsOperations(_ => new AiInsightService((messages, _, token) =>
        {
            token.ThrowIfCancellationRequested();
            prompt = JsonSerializer.Serialize(messages);
            return Task.FromResult("A concise summary.");
        }));
        var document = ControlCueDocument.Capture(Guid.NewGuid(), "source", "en",
        [new SubtitleData { Text = "original cue", TranslatedText = "translated cue" }, new SubtitleData { Text = "untranslated cue" }], "fr");
        var request = new ControlInsightRequest(Guid.NewGuid(), document.Revision, AiInsightMode.Summary, UseTranslated: true);
        var config = new Config.SubtitlesConfig();
        var cancellation = TestContext.Current.CancellationToken;

        Assert.Equal("revision_conflict", (await Assert.ThrowsAsync<ControlException>(() =>
            insights.GenerateAsync(document, request with { Revision = "stale" }, config, null, cancellation))).Error.Code);
        Assert.Null(prompt);
        var generated = await insights.GenerateAsync(document, request, config, null, cancellation);
        Assert.Equal(document.Revision, generated.DocumentRevision);
        Assert.Equal("fr", generated.SourceLanguage);
        Assert.Contains("translated cue", prompt);
        Assert.DoesNotContain("original cue", prompt);
        Assert.DoesNotContain("untranslated cue", prompt);
        Assert.Equal("A concise summary.", ControlInsightsOperations.FormatExport(generated.Insights));
    }

    [Fact]
    public async Task Insight_export_and_dictionary_limit_follow_existing_owner_contract()
    {
        var result = new AiInsightResult("Summary", [new("term", "", "translation", "", "")], "raw reply", false);
        Assert.Equal("Summary\r\n\r\n" + VocabularyParser.ToTsv(result.Vocabulary), ControlInsightsOperations.FormatExport(result));
        var config = new Config.SubtitlesConfig();
        config.WordDefinitionServiceType = WordDefinitionServiceType.DictionaryApi;
        var error = await Assert.ThrowsAsync<ControlException>(() => ControlInsightsOperations.LookupAsync(new("猫", "ja"),
            config, TestContext.Current.CancellationToken));
        Assert.Equal("dictionary_unavailable", error.Error.Code);
    }
}
