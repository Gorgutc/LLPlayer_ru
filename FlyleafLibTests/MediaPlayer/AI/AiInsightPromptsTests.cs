using System.Linq;
using AwesomeAssertions;
using FlyleafLib.MediaPlayer.AI;
using FlyleafLib.MediaPlayer.Translation.Services;

namespace FlyleafLib.MediaPlayer.AI;

public class AiInsightPromptsTests
{
    private static AiInsightOptions Opts(string transcript = "TRANSCRIPT_BODY", string? src = "English", string tgt = "Russian") =>
        new(transcript, src, tgt);

    private static string All(OpenAIMessage[] msgs) => string.Join("\n", msgs.Select(m => m.role + ":" + m.content));

    [Fact]
    public void SummarySingle_SubstitutesLanguages_AndTranscript()
    {
        OpenAIMessage[] msgs = AiInsightPrompts.BuildSummarySingle(Opts());

        msgs.Should().HaveCount(2);
        msgs[0].role.Should().Be("system");
        msgs[1].role.Should().Be("user");

        string all = All(msgs);
        all.Should().Contain("English");
        all.Should().Contain("Russian");
        all.Should().Contain("TRANSCRIPT_BODY");
        all.Should().NotContain("{source_lang}");
        all.Should().NotContain("{target_lang}");
        all.Should().NotContain("{transcript}");
    }

    [Fact]
    public void SummaryMap_IncludesPartNumbering_DistinctFromSingle()
    {
        OpenAIMessage[] map = AiInsightPrompts.BuildSummaryMap(Opts(), 2, 5);
        string all = All(map);

        all.Should().Contain("part 2/5");
        all.Should().NotContain("{part}");
        all.Should().NotContain("{total}");

        // Single prompt must NOT carry the per-part wording.
        All(AiInsightPrompts.BuildSummarySingle(Opts())).Should().NotContain("part 2/5");
    }

    [Fact]
    public void SummaryReduce_EmbedsOrderedPartials()
    {
        OpenAIMessage[] reduce = AiInsightPrompts.BuildSummaryReduce(Opts(transcript: ""), ["alpha notes", "beta notes"]);
        string all = All(reduce);

        all.Should().Contain("alpha notes");
        all.Should().Contain("beta notes");
        all.Should().Contain("Part 1:");
        all.Should().Contain("Part 2:");
        all.Should().NotContain("{partials}");
    }

    [Fact]
    public void Vocabulary_HasExactFiveFieldContract_AndCount()
    {
        OpenAIMessage[] msgs = AiInsightPrompts.BuildVocabulary(Opts(), maxCount: 25);
        string all = All(msgs);

        all.Should().Contain("term | reading | translation | definition | example");
        all.Should().Contain("25");
        all.Should().Contain("TRANSCRIPT_BODY");
        all.Should().NotContain("{count}");
        all.Should().NotContain("{source_lang}");
        all.Should().NotContain("{target_lang}");
        all.Should().NotContain("{transcript}");
    }

    [Fact]
    public void UnknownSourceLanguage_UsesNeutralPhrase_NoPlaceholderLeak()
    {
        OpenAIMessage[] msgs = AiInsightPrompts.BuildVocabulary(Opts(src: null), maxCount: 10);
        string all = All(msgs);

        all.Should().Contain("the source language");
        all.Should().NotContain("{source_lang}");
    }

    [Fact]
    public void AllBuilders_LeaveNoUnreplacedPlaceholders()
    {
        string[] all =
        [
            All(AiInsightPrompts.BuildSummarySingle(Opts())),
            All(AiInsightPrompts.BuildSummaryMap(Opts(), 1, 3)),
            All(AiInsightPrompts.BuildSummaryReduce(Opts(transcript: ""), ["x", "y"])),
            All(AiInsightPrompts.BuildVocabulary(Opts(), 30)),
        ];

        foreach (string s in all)
        {
            s.Should().NotContain("{source_lang}");
            s.Should().NotContain("{target_lang}");
            s.Should().NotContain("{transcript}");
            s.Should().NotContain("{count}");
            s.Should().NotContain("{part}");
            s.Should().NotContain("{total}");
            s.Should().NotContain("{partials}");
        }
    }
}
