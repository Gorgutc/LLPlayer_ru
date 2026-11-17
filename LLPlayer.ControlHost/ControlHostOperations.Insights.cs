using System.Text.Json;
using LLPlayer.ControlApi;
using LLPlayer.Core.Automation;
using LLPlayer.Core.Services;

namespace LLPlayer.ControlHost;

internal sealed partial class ControlHostOperations
{
    private void RegisterInsights(string wordLibraryPath)
    {
        var insights = new ControlInsightsOperations();
        Job<ControlInsightRequest>("insights.generate", "insights", "Generate a summary or vocabulary for the selected subtitle revision.",
            async (context, args, ct) =>
            {
                var input = await documents.GetDocumentAsync(context.Request.Scope, new(args.JobId, args.Revision), ct);
                var current = settings.CaptureWorkflowSettings();
                context.VerifyIdentity("insight_settings", current.TranslationIdentity);
                var result = await insights.GenerateAsync(input, args, current.Config.Subtitles,
                    new InlineProgress<FlyleafLib.MediaPlayer.AI.AiInsightProgress>(status => context.Report(status.Phase,
                        status.Total > 0 ? Math.Clamp((double)status.Current / status.Total, 0, 1) : null)), ct);
                return Done(result);
            });
        Job<ControlInsightSave>("insights.save", "insights", "Save generated insights at their retained result revision.",
            async (context, args, ct) =>
            {
                var result = await ReadInsightResultAsync(context.Request.Scope, args.JobId, args.Revision, ct);
                var path = Absolute(args.Path);
                var prepared = new ControlPreparedTextExport(ControlInsightsOperations.FormatExport(result.Insights), includeUtf8Bom: false);
                await prepared.PublishAsync(context, path, args.Overwrite, ct, effectId: "insights");
                return Done(new { path, sha256 = prepared.ComputeSha256() });
            });
        Job<ControlInsightMerge>("insights.vocabulary.merge", "insights", "Merge vocabulary from a retained Insight result into the word library.",
            async (context, args, ct) =>
            {
                var result = await ReadInsightResultAsync(context.Request.Scope, args.JobId, args.Revision, ct);
                var entries = result.Insights.Vocabulary.Select(entry => new ControlVocabularyItem(entry.Term, entry.Reading,
                    entry.Translation, entry.Definition, entry.Example)).ToArray();
                ControlVocabularyMerge merge = new(entries, result.SourceLanguage, result.TargetLanguage, args.WordLibraryRevision);
                return await WriteWordsAsync(context, "insights.vocabulary.apply", merge,
                    writeToken => ControlWordOperations.MergeVocabulary(new WordLibrary(wordLibraryPath), merge, writeToken), ct);
            });
        Job<ControlDictionaryLookup>("dictionary.lookup", "dictionary", "Look up a definition through the configured dictionary mode.",
            async (context, args, ct) =>
            {
                var current = settings.CaptureWorkflowSettings();
                context.VerifyIdentity("dictionary_settings", current.TranslationIdentity);
                return Done(await ControlInsightsOperations.LookupAsync(args, current.Config.Subtitles, ct));
            });
        RegisterInsightCopy();
    }

    private void RegisterInsightCopy()
    {
        const string operation = "insights.copy";
        handlers.Add(operation, async (context, ct) =>
        {
            context.RequireOriginalHost();
            var args = context.Request.Arguments.Deserialize<ControlInsightCopy>(ControlProtocol.Json)!;
            var result = await ReadInsightResultAsync(context.Request.Scope, args.JobId, args.Revision, ct);
            var request = context.Request with
            {
                OperationId = "ui.clipboard.set",
                ExpectedRevision = null,
                Arguments = ControlProtocol.Encode(new ControlClipboardText(ControlInsightsOperations.FormatExport(result.Insights))),
            };
            return FromResult(await broker.ExecuteAsync(request, ct));
        });
        catalog.Add<ControlInsightCopy, ControlResult>(new(operation, "insights", "Copy a retained Insights result to a selected window's clipboard.",
                true, ControlOperationCompletion.Job, ControlOperationContext.Gui),
            (invocation, args, ct) => jobs.EnqueueAsync(Normalize(invocation.Request, args), ct),
            scope => broker.GetUnavailableReason(scope) ?? (scope.SessionId is null ? "session_required" : null));
    }

    private async Task<ControlInsightResult> ReadInsightResultAsync(ControlScope scope, Guid id, long revision, CancellationToken token)
    {
        var job = await jobs.GetAsync(scope, id, token);
        if (job is not { Status: ControlJobStatus.Completed, OperationId: "insights.generate", Value: JsonElement value })
            throw new ControlException(new("insights_unavailable", "The selected job has no completed AI Insights result."));
        if (job.Revision != revision)
            throw new ControlException(new("revision_conflict", "Read the current AI Insights result before using it."));
        return value.Deserialize<ControlInsightResult>(ControlProtocol.Json)!;
    }
}
