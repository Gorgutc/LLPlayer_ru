using System.Text;
using FlyleafLib;
using FlyleafLib.MediaPlayer.AI;
using FlyleafLib.MediaPlayer.Translation;
using FlyleafLib.MediaPlayer.Translation.Services;
using LLPlayer.ControlApi;

namespace LLPlayer.Core.Automation;

public sealed record ControlInsightRequest(Guid JobId, string Revision, AiInsightMode Mode = AiInsightMode.Both,
    bool UseTranslated = false);
public sealed record ControlInsightResult(Guid DocumentId, string DocumentRevision, string SourceLanguage, string TargetLanguage,
    AiInsightResult Insights);
public sealed record ControlInsightSave(Guid JobId, long Revision, string Path, bool Overwrite = false);
public sealed record ControlInsightCopy(Guid JobId, long Revision);
public sealed record ControlInsightMerge(Guid JobId, long Revision, string WordLibraryRevision);
public sealed record ControlDictionaryLookup(string Term, string SourceLanguage, string? Context = null);

/// <summary>Uses the same provider resolver, transcript rules and bounded workflow as the desktop Insights dialog.</summary>
public sealed class ControlInsightsOperations(Func<Config.SubtitlesConfig, AiInsightService?>? resolve = null)
{
    public async Task<ControlInsightResult> GenerateAsync(ControlCueDocument document, ControlInsightRequest args,
        Config.SubtitlesConfig config, IProgress<AiInsightProgress>? progress, CancellationToken token)
    {
        if (args.Revision != document.Revision) throw new ControlException(new("revision_conflict", "Read the current subtitle document."));
        if (!Enum.IsDefined(args.Mode)) throw new ControlException(new("invalid_arguments", "Choose a supported insight mode."));
        string?[] cues = document.Cues.Select(cue => args.UseTranslated ? cue.TranslatedText : cue.Text).ToArray();
        if (!cues.Any(cue => !string.IsNullOrWhiteSpace(cue)))
            throw new ControlException(new("no_transcript", "There is no transcript to analyze in the selected text mode."));
        var service = (resolve ?? ResolveConfigured)(config) ??
            throw new ControlException(new("insights_not_configured", "Configure a usable LLM in Translation settings."));
        string sourceLanguage = (args.UseTranslated ? document.TargetLanguage : document.SourceLanguage) ?? "";
        string targetLanguage = config.TranslateTargetLanguage.ToLanguageTag();
        try
        {
            var result = await service.GenerateAsync(args.Mode, cues, sourceLanguage, targetLanguage, progress, token).ConfigureAwait(false);
            return new(document.DocumentId, document.Revision, sourceLanguage, targetLanguage, result);
        }
        catch (TranslationConfigException)
        { throw new ControlException(new("insights_not_configured", "Check the configured LLM endpoint, model and credentials.")); }
        catch (TranslationException error)
        {
            throw error.Kind == TranslationFailureKind.Truncated
                ? new ControlException(new("insights_reply_truncated", "The selected model cut its reply short."))
                : new ControlException(new("insights_provider_failed", "The configured LLM could not complete the request.", true));
        }
    }

    public static async Task<DictionaryEntry> LookupAsync(ControlDictionaryLookup args, Config.SubtitlesConfig config,
        CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(args.Term) || string.IsNullOrWhiteSpace(args.SourceLanguage))
            throw new ControlException(new("invalid_arguments", "Specify a term and its source language."));
        string sourceLanguage = Language.Get(args.SourceLanguage).ISO6391;
        var mode = config.WordDefinitionServiceType;
        using var service = WordDefinitionService.ForConfig(config);
        var provider = WordDefinitionSelector.Select(mode, sourceLanguage, service.LlmAvailable);
        if (provider == WordDefinitionProvider.None)
            throw new ControlException(new("dictionary_unavailable", "No configured dictionary can define a term in this source language."));
        return await service.GetDefinitionAsync(provider, WordDefinitionSelector.AllowLlmFallback(mode, provider, service.LlmAvailable),
            args.Term.Trim(), sourceLanguage, config.TranslateTargetLanguage.ToISO6391(), args.Context, token).ConfigureAwait(false);
    }

    public static string FormatExport(AiInsightResult result)
    {
        var text = new StringBuilder(result.SummaryText);
        string vocabulary = result.Vocabulary.Count > 0 ? VocabularyParser.ToTsv(result.Vocabulary) : result.RawVocabularyReply ?? "";
        if (vocabulary.Length > 0)
        {
            if (text.Length > 0) text.Append("\r\n\r\n");
            text.Append(vocabulary);
        }
        return text.ToString();
    }

    private static AiInsightService? ResolveConfigured(Config.SubtitlesConfig config) =>
        AiInsightLlmResolver.Resolve(config) is { } provider ? AiInsightService.ForSettings(provider) : null;
}
